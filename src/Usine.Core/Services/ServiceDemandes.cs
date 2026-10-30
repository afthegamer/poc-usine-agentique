using Usine.Core.BacASable;
using Usine.Core.Etat;
using Usine.Core.Orchestration;

namespace Usine.Core.Services;

public sealed class ServiceDemandes(DepotDemandes depot, ILanceurWorker lanceur, IFabriqueBacASable fabriqueBac, Orchestrateur orchestrateur)
{
    public const string EtapeBacklog = "backlog";

    public const string EtapeBesoin = "besoin";

    public const string EtapeTests = "tests";

    public const string EtapeLivraison = "livraison";

    public Demande Lancer(string texte, string? depotSource, bool modeAuto, string? titre, string? projet = null, bool cadrageParDemandeur = false)
    {
        var nomProjet = string.IsNullOrWhiteSpace(projet) ? null : projet.Trim();
        if (nomProjet is null && !string.IsNullOrWhiteSpace(depotSource) && Projets.NomDuDossier(depot, depotSource) is { } nomDuDossier)
        {
            nomProjet = nomDuDossier;
            depotSource = null;
        }

        if (nomProjet is not null)
        {
            if (!string.IsNullOrWhiteSpace(depotSource))
            {
                throw new ArgumentException("Indique soit un projet de l'usine (projet), soit un dépôt extérieur (depot), pas les deux.", nameof(projet));
            }

            depotSource = Projets.Chemin(depot, nomProjet);
            RefuserSiProjetOccupe(nomProjet, null);
        }

        if (!string.IsNullOrWhiteSpace(depotSource))
        {
            var chemin = Path.GetFullPath(depotSource);
            if (!BacASableDocker.EstDepotGit(chemin))
            {
                throw new ArgumentException($"« {chemin} » n'est pas un dépôt git (.git introuvable).", nameof(depotSource));
            }
        }

        var demande = depot.Creer(texte, depotSource, modeAuto, titre, nomProjet, cadrageParDemandeur);
        try
        {
            lanceur.Lancer(demande.Id);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"La demande {demande.Id} est créée, mais son worker n'a pas pu être lancé : {ex.Message} Une fois la cause corrigée, consulte-la pour relancer le traitement (outil MCP voir_avancement, ou en ligne de commande : usine statut {demande.Id}), sans lancer une nouvelle demande.", ex);
        }

        return demande;
    }

    public async Task<Demande> ReevaluerAsync(string id, bool nouveauxTests, CancellationToken ct)
    {
        var source = depot.Charger(id);
        if (source.Statut != StatutDemande.Terminee)
        {
            throw new InvalidOperationException($"La demande {id} n'est pas terminée (statut : {source.Statut}, étape : {source.Etape}) : seule la livraison d'une demande terminée peut être réévaluée.");
        }

        if (source.Livrable is not { } livrable || !BacASableDocker.EstDepotGit(livrable.RepertoireCode))
        {
            throw new InvalidOperationException($"La demande {id} n'a pas de livrable git à réévaluer{(source.Livrable is { } sansGit ? $" ({sansGit.RepertoireCode} n'est pas un dépôt git)" : "")}.");
        }

        if (source.Backlog is null || source.Tests is null)
        {
            throw new InvalidOperationException($"La demande {id} n'a pas de backlog ou de tests d'acceptation verrouillés à reprendre pour la réévaluer.");
        }

        var demande = depot.Creer(source.Texte, source.DepotSource, modeAuto: true, $"Réévaluation de {id}{(nouveauxTests ? " avec de nouveaux tests" : "")} : {source.Titre}");
        demande.ReevaluationDe = id;
        demande.NouveauxTests = nouveauxTests;
        demande.Backlog = source.Backlog;
        if (nouveauxTests)
        {
            demande.Etape = EtapeDemande.Cadrage;
            demande.CalculsCorriges = [.. source.CalculsCorriges];
            var contrat = Path.Combine(depot.RepertoireAcceptation(id), "CONTRAT.md");
            if (File.Exists(contrat))
            {
                var cible = Path.Combine(depot.RepertoireAcceptation(demande.Id), "CONTRAT.md");
                File.Copy(contrat, cible);
                if (OperatingSystem.IsLinux())
                {
                    File.SetUnixFileMode(cible, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
                }
            }

            depot.AjouterEvenement(demande, "Usine", $"Réévaluation de la livraison de la demande {id} avec de nouveaux tests : même demande, même backlog et même contrat, sans ses tests ; le PO, relancé sur la même demande, n'y ajoute que les notions de la demande ; le QA amont écrit de nouveaux tests d'acceptation, verrouillés d'office, puis l'oracle vérifie directement l'espace de travail importé depuis son livrable ({livrable.RepertoireCode}), sans passe de construction préalable ; sans coût, vérification ni évaluation reprises.");
        }
        else
        {
            demande.Tests = source.Tests;
            demande.VotesNotions = [.. source.VotesNotions];
            demande.Etape = EtapeDemande.Verification;
            var acceptation = depot.RepertoireAcceptation(id);
            foreach (var relatif in Oracle.Empreinte.ListerFichiers(acceptation))
            {
                var cible = Path.Combine(depot.RepertoireAcceptation(demande.Id), relatif);
                Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
                File.Copy(Path.Combine(acceptation, relatif), cible);
            }

            depot.AjouterEvenement(demande, "Usine", $"Réévaluation de la livraison de la demande {id} : même demande, même backlog, mêmes tests verrouillés et même contrat ; espace de travail importé depuis son livrable ({livrable.RepertoireCode}), sans coût, vérification ni évaluation reprises.");
        }

        depot.Enregistrer(demande);
        var bac = fabriqueBac.Creer(demande.Id);
        try
        {
            await bac.InitialiserAsync(livrable.RepertoireCode, ct);
            await bac.SupprimerAsync(avecVolume: false, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            try
            {
                await bac.SupprimerAsync(avecVolume: true, CancellationToken.None);
                BacASableDocker.SupprimerRepertoire(depot.Repertoire(demande.Id));
            }
            catch (Exception nettoyage) when (nettoyage is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Réévaluation de la demande {id} impossible : l'espace de travail n'a pas pu être importé depuis son livrable ({ex.Message}), et la demande {demande.Id} créée pour elle n'a pas pu être retirée ({nettoyage.Message}) : annule-la.", ex);
            }

            throw new InvalidOperationException($"Réévaluation de la demande {id} impossible : l'espace de travail n'a pas pu être importé depuis son livrable ({ex.Message}).", ex);
        }

        try
        {
            lanceur.Lancer(demande.Id);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"La demande {demande.Id} est créée, mais son worker n'a pas pu être lancé : {ex.Message} Une fois la cause corrigée, consulte-la pour relancer le traitement (outil MCP voir_avancement, ou en ligne de commande : usine statut {demande.Id}), sans lancer une nouvelle réévaluation.", ex);
        }

        return demande;
    }

    public Demande Avancement(string id)
    {
        var workerActif = lanceur.EstActif(id);
        var demande = depot.Charger(id);
        if (demande.Statut == StatutDemande.EnCours && !workerActif)
        {
            depot.AjouterEvenement(demande, "Usine", "Aucun worker actif : reprise du traitement à l'étape en cours.");
            depot.Enregistrer(demande);
            try
            {
                lanceur.Lancer(id);
            }
            catch (InvalidOperationException ex)
            {
                depot.AjouterEvenement(demande, "Usine", $"Relance impossible : {ex.Message}");
                depot.Enregistrer(demande);
                throw;
            }
        }

        return demande;
    }

    public Demande RepondreQuestions(string id, string reponses)
    {
        if (string.IsNullOrWhiteSpace(reponses))
        {
            throw new ArgumentException("Les réponses sont vides.", nameof(reponses));
        }

        var demande = depot.Charger(id);
        if (demande.Statut != StatutDemande.AttenteReponses)
        {
            throw new InvalidOperationException($"La demande {id} n'attend pas de réponses (statut : {demande.Statut}, étape : {demande.Etape}).");
        }

        demande.Echanges.Add(new EchangeQuestions { Questions = [.. demande.Questions], Reponses = reponses.Trim() });
        demande.Questions = [];
        demande.Etape = EtapeDemande.Cadrage;
        demande.Statut = StatutDemande.EnCours;
        depot.AjouterEvenement(demande, "Demandeur", "Réponses transmises au PO.");
        depot.RetirerAnnulation(id);
        depot.Enregistrer(demande);
        try
        {
            lanceur.Lancer(id);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Les réponses de la demande {id} sont bien enregistrées, mais le worker de la demande n'a pas pu être lancé : {ex.Message} Une fois la cause corrigée, consulte la demande pour relancer le traitement (outil MCP voir_avancement, ou en ligne de commande : usine statut {id}), sans répondre de nouveau.", ex);
        }

        return demande;
    }

    public async Task<Demande> ValiderEtapeAsync(string id, string etape, bool approuver, string? commentaire, CancellationToken ct)
    {
        var demande = depot.Charger(id);
        var attendue = EtapeAValider(demande.Etape);
        if (demande.Statut != StatutDemande.AttenteValidation || attendue is null)
        {
            throw new InvalidOperationException($"La demande {id} n'attend pas de validation (statut : {demande.Statut}, étape : {demande.Etape}).");
        }

        var normalisee = etape.Trim().ToLowerInvariant();
        if (normalisee != attendue)
        {
            throw new InvalidOperationException($"La validation attendue porte sur « {attendue} », pas sur « {etape} ».");
        }

        if (!approuver && string.IsNullOrWhiteSpace(commentaire))
        {
            throw new ArgumentException("Un refus doit être accompagné d'un commentaire qui explique quoi changer.", nameof(commentaire));
        }

        var titresAjoutes = !approuver && attendue == EtapeLivraison ? await ControlerTestsAjoutesAsync(demande, ct) : null;
        if (approuver)
        {
            depot.AjouterEvenement(demande, "Demandeur", $"Étape « {attendue} » approuvée.{Suffixe(commentaire)}");
            if (attendue == EtapeTests && demande.Tests is not null)
            {
                Oracle.Empreinte.Verrouiller(demande.Tests, depot.RepertoireAcceptation(id));
                depot.AjouterEvenement(demande, "Usine", "Tests d'acceptation verrouillés dans leur version approuvée.");
            }

            if (attendue == EtapeLivraison && !string.IsNullOrWhiteSpace(commentaire))
            {
                demande.CommentaireApprobationLivraison = commentaire.Trim();
            }

            if (attendue == EtapeBesoin)
            {
                demande.BesoinValide = true;
            }

            demande.Etape = attendue switch
            {
                EtapeBacklog => EtapeDemande.TestsAcceptation,
                EtapeBesoin => EtapeDemande.Cadrage,
                EtapeTests => EtapeDemande.Construction,
                _ => EtapeDemande.Terminee,
            };
        }
        else
        {
            depot.AjouterEvenement(demande, "Demandeur", $"Étape « {attendue} » refusée : {commentaire}");
            switch (attendue)
            {
                case EtapeBacklog:
                    demande.CommentairesBacklog.Add(commentaire!.Trim());
                    demande.Etape = EtapeDemande.Cadrage;
                    break;
                case EtapeBesoin:
                    demande.CommentairesBesoin.Add(commentaire!.Trim());
                    demande.Etape = EtapeDemande.Cadrage;
                    break;
                case EtapeTests:
                    demande.CommentairesTests.Add(commentaire!.Trim());
                    if (demande.Tests is not null)
                    {
                        demande.Tests.Empreinte = Oracle.Empreinte.Calculer(depot.RepertoireAcceptation(id));
                    }

                    demande.Etape = EtapeDemande.TestsAcceptation;
                    break;
                default:
                    demande.CommentairesLivraison.Add(commentaire!.Trim());
                    if (demande.Livrable?.EmpreinteCode is { } empreinteRefusee)
                    {
                        demande.EmpreintesCodeRefuse.Add(empreinteRefusee);
                    }

                    demande.RetourPourDev = $"Le demandeur a refusé la livraison :\n{commentaire!.Trim()}{VerrouillerTestsAjoutes(demande, titresAjoutes)}";
                    demande.Corrections = 0;
                    demande.CorrectionsEvaluation = 0;
                    demande.CorrectionsSignaux = 0;
                    demande.DernierVertRestaure = false;
                    demande.RestaurationLancee = false;
                    demande.VersionRefuseeRestauree = false;
                    demande.EvaluationDernierVert = demande.EvaluationAnterieureAuDernierVert ? null : demande.Evaluation;
                    demande.EvaluationAnterieureAuDernierVert = false;
                    demande.Evaluation = null;
                    demande.Etape = EtapeDemande.Construction;
                    break;
            }
        }

        if (demande.Etape == EtapeDemande.Terminee)
        {
            await fabriqueBac.Creer(id).SupprimerAsync(avecVolume: true, ct);
            demande.Statut = StatutDemande.Terminee;
            await orchestrateur.PublierProjetAsync(demande);
            depot.AjouterEvenement(demande, "Usine", "Demande terminée.");
            depot.Enregistrer(demande);
            orchestrateur.ActualiserRapport(demande);
            return demande;
        }

        depot.RetirerAnnulation(id);
        demande.Statut = StatutDemande.EnCours;
        depot.Enregistrer(demande);
        try
        {
            lanceur.Lancer(id);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"L'étape « {attendue} » de la demande {id} est bien {(approuver ? "approuvée" : "refusée")}, mais le worker de la demande n'a pas pu être lancé : {ex.Message} Une fois la cause corrigée, consulte la demande pour relancer le traitement (outil MCP voir_avancement, ou en ligne de commande : usine statut {id}), sans valider l'étape de nouveau.", ex);
        }

        return demande;
    }

    public IReadOnlyList<Demande> Lister() => depot.Lister();

    public IReadOnlyList<ProjetUsine> ListerProjets() => Projets.Lister(depot);

    public async Task<Demande> ReprendreAsync(string id, CancellationToken ct)
    {
        var workerActif = lanceur.EstActif(id);
        var demande = depot.Charger(id);
        if (demande.Statut != StatutDemande.Echouee)
        {
            throw new InvalidOperationException($"La demande {id} n'a pas échoué (statut : {demande.Statut}, étape : {demande.Etape}) : seule une demande échouée peut être reprise.");
        }

        if (workerActif)
        {
            throw new InvalidOperationException(MessageWorkerEnEchec(id));
        }

        if (demande.Projet is { } nomProjet)
        {
            RefuserSiProjetOccupe(nomProjet, id);
        }

        var bac = fabriqueBac.Creer(id);
        if (!await bac.EspaceDeTravailExisteAsync(ct))
        {
            if (!await bac.DockerRepondAsync(ct))
            {
                throw new InvalidOperationException($"Docker ne répond pas : l'espace de travail de la demande {id} ne peut pas être vérifié, et il est peut-être intact. Démarre Docker (Docker Desktop sous Windows), attends qu'il soit prêt, puis réessaie la reprise. N'annule pas la demande : cela supprimerait cet espace de travail.");
            }

            if (demande.Etape != EtapeDemande.Cadrage || demande.ReevaluationDe is not null)
            {
                throw new InvalidOperationException($"L'espace de travail de la demande {id} n'existe plus, alors que Docker répond : elle ne peut pas être reprise à l'étape {demande.Etape}. Annule-la (outil MCP annuler_demande, ou en ligne de commande : usine annuler {id}) : le demandeur pourra en lancer une nouvelle.");
            }

            await bac.SupprimerAsync(avecVolume: true, ct);
        }

        depot.RetirerAnnulation(id);
        demande.Statut = StatutDemande.EnCours;
        demande.Erreur = null;
        depot.AjouterEvenement(demande, "Demandeur", $"Reprise après l'échec : le traitement reprend à l'étape {demande.Etape}.");
        depot.Enregistrer(demande);
        try
        {
            lanceur.Lancer(id);
        }
        catch (InvalidOperationException ex)
        {
            demande.Statut = StatutDemande.Echouee;
            demande.Erreur = ex.Message;
            depot.AjouterEvenement(demande, "Usine", $"Reprise impossible, la demande reste échouée : {ex.Message}");
            depot.Enregistrer(demande);
            throw;
        }

        return demande;
    }

    public async Task<Demande> AnnulerAsync(string id, CancellationToken ct)
    {
        var workerActif = lanceur.EstActif(id);
        var demande = depot.Charger(id);
        if (demande.Statut is StatutDemande.Terminee or StatutDemande.Annulee)
        {
            return demande;
        }

        if (demande.Statut == StatutDemande.Echouee && workerActif)
        {
            throw new InvalidOperationException(MessageWorkerEnEchec(id));
        }

        depot.DemanderAnnulation(id);
        if (demande.Statut == StatutDemande.EnCours && workerActif)
        {
            depot.AjouterEvenement(demande, "Demandeur", "Annulation demandée : le worker s'arrête dans quelques secondes.");
            return demande;
        }

        await fabriqueBac.Creer(id).SupprimerAsync(avecVolume: true, ct);
        demande.Statut = StatutDemande.Annulee;
        depot.AjouterEvenement(demande, "Demandeur", "Demande annulée.");
        depot.Enregistrer(demande);
        if (demande.Etape == EtapeDemande.ValidationLivraison)
        {
            orchestrateur.ActualiserRapport(demande);
        }

        return demande;
    }

    public Demande Charger(string id) => depot.Charger(id);

    private async Task<IReadOnlyList<string>?> ControlerTestsAjoutesAsync(Demande demande, CancellationToken ct)
    {
        var repertoire = depot.RepertoireAcceptation(demande.Id);
        if (demande.Tests is not { } verrouilles
            || !Oracle.Empreinte.ListerFichiers(repertoire).Except(verrouilles.Fichiers, StringComparer.Ordinal).Any()
            || !string.Equals(Oracle.Empreinte.Calculer(repertoire, verrouilles.Fichiers), verrouilles.Empreinte, StringComparison.Ordinal))
        {
            return null;
        }

        (string? Motif, IReadOnlyList<string> Titres) controle;
        try
        {
            controle = await orchestrateur.ControlerTestsAjoutesAsync(demande, ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Refus de la livraison non enregistré : le contrôle des fichiers ajoutés au dossier d'acceptation ({repertoire}) a échoué : {ex.Message} Une fois la cause corrigée (Docker démarré, par exemple), refuse de nouveau la livraison avec ton commentaire.", ex);
        }

        if (controle.Motif is { } motif)
        {
            var message = $"Refus de la livraison non enregistré : les fichiers ajoutés au dossier d'acceptation ({repertoire}) ne sont pas acceptés : {motif}\nCorrige-les ou retire-les, puis refuse de nouveau la livraison avec ton commentaire. La demande attend toujours la validation de la livraison, et les tests verrouillés restent la référence.";
            depot.AjouterEvenement(demande, "Usine", message);
            depot.Enregistrer(demande);
            throw new InvalidOperationException(message);
        }

        return controle.Titres;
    }

    private string VerrouillerTestsAjoutes(Demande demande, IReadOnlyList<string>? titres)
    {
        if (titres is null || demande.Tests is not { } tests)
        {
            return "";
        }

        var repertoire = depot.RepertoireAcceptation(demande.Id);
        var fichiers = Oracle.Empreinte.ListerFichiers(repertoire).Except(tests.Fichiers, StringComparer.Ordinal).ToList();
        var nouveaux = Orchestrateur.TitresAjoutes(titres, tests.Titres);
        Oracle.Empreinte.Verrouiller(tests, repertoire);
        tests.Titres = [.. titres];
        demande.ResultatsParVersion = [];
        if (nouveaux.Count > 0)
        {
            demande.VerificationsAvantTestsAjoutes = demande.Verifications.Count;
            demande.MeilleureTentative = null;
            demande.TestsAjoutesAuxRefus[demande.CommentairesLivraison.Count] = nouveaux;
        }

        depot.AjouterEvenement(demande, "Usine", $"Fichiers ajoutés par le demandeur au dossier d'acceptation, contrôlés puis verrouillés avec les tests approuvés : {string.Join(", ", fichiers)}. {(nouveaux.Count == 0 ? "Aucun nouveau test." : $"Nouveaux tests ({nouveaux.Count}) : {string.Join(" ; ", nouveaux)}.")}");
        if (demande.Backlog is { } backlog)
        {
            orchestrateur.SignalerTestsHorsBacklog(demande, backlog, nouveaux);
        }

        return nouveaux.Count == 0
            ? ""
            : $"\n\nLe demandeur a aussi ajouté ces tests d'acceptation, verrouillés avec les autres et rejoués à chaque vérification : ils doivent passer, comme les tests déjà présents.\n{string.Join("\n", nouveaux.Select(t => $"- {t}"))}";
    }

    private void RefuserSiProjetOccupe(string nomProjet, string? id)
    {
        if (depot.Lister().FirstOrDefault(d => d.Id != id && !d.EstTerminale && string.Equals(d.Projet, nomProjet, StringComparison.Ordinal)) is { } enCours)
        {
            throw new InvalidOperationException($"Le projet « {nomProjet} » a déjà une évolution en cours : demande {enCours.Id} ({enCours.Statut}, étape {enCours.Etape}). Une seule demande à la fois par projet : attends qu'elle se termine, ou annule-la, avant d'en lancer une autre.");
        }
    }

    internal static string? EtapeAValider(EtapeDemande etape) => etape switch
    {
        EtapeDemande.ValidationBacklog => EtapeBacklog,
        EtapeDemande.ValidationBesoin => EtapeBesoin,
        EtapeDemande.ValidationTests => EtapeTests,
        EtapeDemande.ValidationLivraison => EtapeLivraison,
        _ => null,
    };

    private static string MessageWorkerEnEchec(string id) =>
        $"Le worker de la demande {id} termine encore le traitement de l'échec : réessaie dans quelques secondes.";

    private static string Suffixe(string? commentaire) => string.IsNullOrWhiteSpace(commentaire) ? "" : $" Commentaire : {commentaire.Trim()}";
}
