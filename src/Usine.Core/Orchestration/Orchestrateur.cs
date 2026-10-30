using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Usine.Core.Agents;
using Usine.Core.BacASable;
using Usine.Core.Configuration;
using Usine.Core.Etat;
using Usine.Core.Oracle;
using Usine.Core.Rapports;
using static Usine.Core.Texte;

namespace Usine.Core.Orchestration;

public sealed partial class Orchestrateur(
    DepotDemandes depot,
    ConfigUsine config,
    IFabriqueBacASable fabriqueBac,
    IExecuteurAgent executeur,
    IOracle oracle)
{
    public const string ChefDeProjet = "Chef de projet";

    public const string QaReverification = "QA (revérification)";

    private const string Espace = "/workspace";

    private const string SuffixeSorties = "-sorties.log";

    private const string Acceptation = "/acceptance";

    private const string RefDernierVert = "refs/usine/dernier-vert";

    private const string RefDernierVertComplet = "refs/usine/dernier-vert-complet";

    private const string RefMeilleureTentative = "refs/usine/meilleure-tentative";

    private const string RefMeilleureTentativeComplete = "refs/usine/meilleure-tentative-complete";

    private const string RefVersionEvaluee = "refs/usine/version-evaluee";

    private const int MaxCaracteresDiff = 8000;

    private const int MaxCaracteresScriptClos = 3000;

    private const string IndexComplet = "/tmp/usine-index-complet";

    private const string ListeDossiersVides = ".git/usine-dossiers-vides";

    private const string RecreerDossiersVides = $"if [ -f {ListeDossiersVides} ]; then xargs -0 -r mkdir -p -- < {ListeDossiersVides}; fi";

    private const string PrefixeRefusTests = "Tests refusés : ";

    private const string RefusResumeMarqueur = "Refusé : ton résumé est un marqueur de l'usine, pas un résumé : décris ce que tu as fait.";

    private const string RefusNonRegressionModifiee = $"{PrefixeRefusTests}{Acceptation}/tests/{NonRegression.Dossier} est réservé aux tests des demandes précédentes et vient d'être remis dans son état d'origine. N'y écris rien, ne modifie ni ne supprime ce qu'il contient, puis soumets à nouveau.";

    internal const string ConsigneRegroupement = "Regroupe les stories et les critères proches (un critère peut vérifier plusieurs règles d'un même écran ou d'un même endpoint) au lieu de retirer une partie du besoin : tout le besoin doit rester couvert.";

    internal const string ResumeAdoption = "Tests fournis dans le dossier d'acceptation, adoptés tels quels.";

    private const string ConseilInstallationTests = "vérifie que tes tests se chargent avec lister_tests ; le contrôle de types TypeScript est fait par soumettre_tests.";

    private const string ConseilInstallationScripts = "lance un script Playwright avec la commande donnée dans tes consignes.";

    private const string MotifContratAbsent = Acceptation + "/CONTRAT.md est absent ou vide. Écris-y ce que le Dev doit respecter (pages, libellés, champs, messages, routes).";

    internal const string ControleTypes = "node /opt/acceptance-runner/node_modules/typescript/bin/tsc -p /opt/acceptance-runner/tsconfig.controle.json --pretty false";

    internal const string CommandeEmpreinteCode = "rm -f /tmp/usine-index-empreinte && export GIT_INDEX_FILE=/tmp/usine-index-empreinte && { git read-tree HEAD 2>/dev/null || git read-tree --empty; } && git add -A && git write-tree";

    internal const string CommandeDependances = "node -";

    internal const string CommandeFichiersSuivis = "git -c core.quotepath=off ls-files";

    private const int MaxTestsContestes = 2;

    private const string DossierQa = "qa";

    private const string ScriptsQaDev = "/tmp/qa";

    private const int ReprisesBac = 2;

    private const int MaxFichiersModifiesAnnonces = 20;

    private const string MotifBacIndisponible = "le conteneur du bac à sable n'a pas répondu, même après deux nouvelles tentatives";

    private static readonly TimeSpan DelaiScriptQa = TimeSpan.FromMinutes(3);

    private static readonly string[] ExtensionsScriptQa = [".js", ".cjs", ".sh"];

    private const string ControleFait = "fait";

    private const string ControleNonFait = "non fait";

    private static readonly string[] StatutsControle = [ControleFait, ControleNonFait, "sans objet"];

    private const string GardeNode = "data:text/javascript,process.on(\"uncaughtException\",e=>{console.error(e);process.exit(2)});process.on(\"unhandledRejection\",e=>{console.error(e);process.exit(2)})";

    private const string MotifRepereAmbigu = $"Le repère de ces tests désigne plusieurs éléments de la page à la fois (« {Consignes.ViolationModeStrict} » de Playwright) : le code peut être juste, et le test trop vague.";

    private const int LongueurMinStack = 8;

    private const int LongueurMinTexte = 15;

    private const int MotsSignificatifsMin = 4;

    private const int MotsCommunsMin = 2;

    private const int MaxNotions = 3;

    private const int AvisParNotion = 3;

    private const int MaxToursAvis = 2;

    private const string MarqueRegleRemplacee = "[règle d'origine remplacée]";

    private const string OutilAvis = "donner_avis";

    private const string Echantillons = "/opt/echantillons";

    private const string SuffixeTexteDeReference = "-texte-attendu.txt";

    private const string LancementDeScript = @"\b(?:node|bash|sh|python3?)\s+(?:-\S+\s+(?:'[^']*'\s+|""[^""]*""\s+)?)*['""]?[^\s'""]+\.(?:[cm]?js|ts|sh|py)\b";

    private static readonly string[] MotifsHorsInstantane = ["node_modules", ".angular", "__pycache__", ".venv", "venv", ".pytest_cache", ".next", ".nuxt", ".cache", "test-results", "playwright-report", "*.log"];

    private static readonly string[] MotifsDonnees = ["*.db", "*.sqlite", "*.sqlite3"];

    private static readonly string[] DossiersDuQaAmont = [Acceptation, Echantillons, Espace];

    private static readonly string[] DossiersDuQaAmontSansCode = [Acceptation, Echantillons];

    private static readonly string[] DossiersDuQa = [Espace, "/tmp", Echantillons];

    private static readonly HashSet<string> MotsOutils = new(
        [
            "afin", "ainsi", "alors", "après", "aucun", "aucune", "aura", "auront", "aussi", "autant", "autre", "autres", "avant", "avec", "avoir", "ayant",
            "bien", "cela", "ceci", "celle", "celles", "celui", "certains", "certaines", "ceux", "chaque", "chez", "comme", "dans", "déjà", "depuis", "doit", "doivent", "donc", "donné", "donnée", "dont",
            "elle", "elles", "encore", "entre", "étaient", "était", "étant", "être", "faire", "fait", "faut", "jamais", "laquelle", "lequel", "leur", "leurs", "lors", "lorsque",
            "mais", "même", "mêmes", "moins", "notre", "nous", "parmi", "pendant", "peut", "peuvent", "plus", "plusieurs", "pour", "puis", "puisse", "quand", "quel", "quelle", "quelles", "quels", "quelques", "quoi",
            "rien", "sans", "selon", "sera", "serait", "seront", "soit", "sont", "sous", "tant", "toujours", "tous", "tout", "toute", "toutes", "très", "vers", "votre", "vous",
        ],
        StringComparer.Ordinal);

    private static string Conserves => string.Join(' ', MotifsHorsInstantane.Select(m => $"-e {OutilsAgent.Quoter(m)}"));

    private readonly Consignes consignes = new(config);

    public TimeSpan AttenteRepriseBac { get; init; } = TimeSpan.FromSeconds(5);

    public async Task ExecuterAsync(string id, CancellationToken ct)
    {
        var demande = depot.Charger(id);
        var bac = fabriqueBac.Creer(id);
        using var arret = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var surveillance = SurveillerAnnulationAsync(id, arret);
        try
        {
            while (demande.Statut == StatutDemande.EnCours)
            {
                if (depot.AnnulationDemandee(id))
                {
                    await AnnulerAsync(demande, bac);
                    return;
                }

                try
                {
                    config.ExigerValide();
                    await ExecuterEtapeAsync(demande, bac, arret.Token);
                }
                catch (OperationCanceledException) when (depot.AnnulationDemandee(id))
                {
                    await AnnulerAsync(demande, bac);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    depot.AjouterEvenement(demande, ChefDeProjet, "Traitement interrompu ; il reprendra à cette étape.");
                    depot.Enregistrer(demande);
                    throw;
                }
                catch (Exception ex)
                {
                    await EchouerAsync(demande, bac, ex);
                }

                if (depot.AnnulationDemandee(id) && demande.Statut is not (StatutDemande.Terminee or StatutDemande.Annulee))
                {
                    await AnnulerAsync(demande, bac);
                    return;
                }

                depot.Enregistrer(demande);
            }
        }
        finally
        {
            await arret.CancelAsync();
            await surveillance;
        }
    }

    private async Task SurveillerAnnulationAsync(string id, CancellationTokenSource arret)
    {
        try
        {
            while (!depot.AnnulationDemandee(id))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), arret.Token);
            }

            await arret.CancelAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task EchouerAsync(Demande demande, IBacASable bac, Exception ex)
    {
        demande.Statut = StatutDemande.Echouee;
        demande.Erreur = ex.Message;
        depot.AjouterEvenement(demande, ChefDeProjet, $"Échec à l'étape {demande.Etape} : {ex.Message}");
        depot.Journaliser(demande.Id, ex.ToString());
        depot.Enregistrer(demande);
        await ConserverTravailPartielAsync(demande, bac);
        await SupprimerConteneurAsync(demande, bac, avecVolume: false);
    }

    private Task ExecuterEtapeAsync(Demande demande, IBacASable bac, CancellationToken ct) => demande.Etape switch
    {
        EtapeDemande.Cadrage => CadrageAsync(demande, bac, ct),
        EtapeDemande.TestsAcceptation => TestsAcceptationAsync(demande, bac, ct),
        EtapeDemande.Construction => ConstructionAsync(demande, bac, ct),
        EtapeDemande.ArbitrageTests => ArbitrageTestsAsync(demande, bac, ct),
        EtapeDemande.Verification => VerificationAsync(demande, bac, ct),
        EtapeDemande.Evaluation => EvaluationAsync(demande, bac, ct),
        EtapeDemande.Livraison => LivraisonAsync(demande, bac, ct),
        EtapeDemande.Terminee => TerminerAsync(demande, bac, ct),
        EtapeDemande.Questions => Attendre(demande, StatutDemande.AttenteReponses),
        _ => Attendre(demande, StatutDemande.AttenteValidation),
    };

    private async Task CadrageAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        depot.AjouterEvenement(demande, ChefDeProjet, demande switch
        {
            { BesoinValide: true, Backlog: null } => "Besoin validé : le PO écrit les user stories.",
            { Echanges.Count: 0, CommentairesBesoin.Count: 0, CommentairesBacklog.Count: 0 } => "Préparation du bac à sable, puis cadrage par le PO.",
            _ => "Reprise du cadrage par le PO.",
        });
        depot.Enregistrer(demande);
        await bac.InitialiserAsync(demande.DepotSource, ct);
        await bac.PreparerAsync(ModeMontage.Exploration, ct);

        var premierTemps = demande.CadrageHumain && !demande.BesoinValide && demande.Backlog is null;
        var fin = new SignalFin();
        List<string>? questions = null;
        Backlog? backlog = null;
        Besoin? besoin = null;
        List<Lacune> lacunes = [];
        var outils = demande.EstProjetExistant && !demande.NouveauxTests ? new List<AIFunction>(OutilsDe(bac).Communs(Espace, racineEcriture: null)) : [];
        if (premierTemps)
        {
            outils.Add(OutilPoserQuestions(fin, q => questions = q));
            outils.Add(OutilSoumettreBesoin(fin, b => besoin = b));
        }
        else
        {
            outils.Add(OutilSoumettreBacklog(demande, fin, new RappelLacunes(), (b, l) =>
            {
                backlog = b;
                lacunes = l;
            }));
        }

        var resultat = await ExecuterAvecRelancesAsync(
            demande,
            RoleAgent.Po,
            premierTemps ? consignes.PromptPoBesoin(demande) : consignes.PromptPo(demande),
            consignes.MessagePo(demande),
            outils,
            fin,
            repli: null,
            ct);

        if (questions is { Count: > 0 })
        {
            demande.Questions = questions;
            demande.Etape = EtapeDemande.Questions;
            demande.Statut = StatutDemande.AttenteReponses;
            depot.AjouterEvenement(demande, RolesAgent.Libelle(RoleAgent.Po), $"{questions.Count} question(s) pour le demandeur.");
            return;
        }

        if (premierTemps)
        {
            if (besoin is null)
            {
                throw new InvalidOperationException(SansResultat(RolesAgent.Libelle(RoleAgent.Po), resultat));
            }

            demande.Besoin = besoin;
            demande.Questions = [];
            if (!demande.TitreImpose)
            {
                demande.Titre = besoin.Titre;
            }

            depot.AjouterEvenement(demande, RolesAgent.Libelle(RoleAgent.Po), $"Besoin proposé : « {besoin.Titre} ».");
            demande.Etape = EtapeDemande.ValidationBesoin;
            demande.Statut = StatutDemande.AttenteValidation;
            return;
        }

        if (backlog is null)
        {
            throw new InvalidOperationException(SansResultat(RolesAgent.Libelle(RoleAgent.Po), resultat));
        }

        if (demande.NouveauxTests && demande.Backlog is { } origine)
        {
            (origine.Notions, origine.SansNotions) = (backlog.Notions, backlog.SansNotions);
            depot.AjouterEvenement(demande, ChefDeProjet, $"Réévaluation avec de nouveaux tests : du backlog proposé par le PO, seules les notions de la demande sont retenues ({(backlog.Notions.Count > 0 ? string.Join(", ", backlog.Notions.Select(n => $"{n.Id} « {n.Nom} »")) : $"aucune : {backlog.SansNotions.Trim()}")}) ; les stories, le hors-périmètre et les hypothèses restent ceux du backlog d'origine.");
            demande.Etape = EtapeDemande.TestsAcceptation;
            return;
        }

        if (demande.Besoin is { } valide)
        {
            backlog.Titre = valide.Titre;
            backlog.Resume = valide.Resume;
        }

        demande.Backlog = backlog;
        demande.Questions = [];
        if (!demande.TitreImpose && !string.IsNullOrWhiteSpace(backlog.Titre))
        {
            demande.Titre = backlog.Titre.Trim();
        }

        var criteres = backlog.Stories.Sum(s => s.Criteres.Count);
        depot.AjouterEvenement(demande, RolesAgent.Libelle(RoleAgent.Po), $"Backlog proposé : {backlog.Stories.Count} stories, {criteres} critères, stack « {backlog.Stack.Choix} ».");
        demande.CalculsCorriges = [.. lacunes.Select(l => l.Correction).OfType<string>()];
        if (lacunes.Count > 0)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : backlog accepté sans traiter : {Couper(string.Join(" ; ", lacunes.Select(l => l.Message)), 1000)}");
        }

        if (!demande.CadrageHumain)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, "Mode auto : backlog validé automatiquement.");
            demande.Etape = EtapeDemande.TestsAcceptation;
        }
        else
        {
            demande.Etape = EtapeDemande.ValidationBacklog;
            demande.Statut = StatutDemande.AttenteValidation;
        }
    }

    private async Task TestsAcceptationAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        var repertoire = depot.RepertoireAcceptation(demande.Id);
        var refusesInchanges = demande.Tests is { } refuses && string.Equals(Empreinte.Calculer(repertoire), refuses.Empreinte, StringComparison.Ordinal)
            ? refuses
            : null;
        var precedentes = NonRegression.Precedentes(depot, demande);
        var reprises = NonRegression.Reprendre(depot, demande, precedentes);
        foreach (var reprise in reprises)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, $"Tests de non-régression repris de la demande {reprise.Id} : {reprise.Identifiants} identifiant(s) de critère préfixé(s) « [NR ».");
        }

        if (refusesInchanges is not null)
        {
            Empreinte.Verrouiller(refusesInchanges, repertoire);
            depot.Enregistrer(demande);
        }

        var repriseDeLEtape = demande.PdfDeposes is not null;
        if (demande.PdfDeposes is null)
        {
            demande.PdfDeposes = [.. PdfPresents(repertoire).Order(StringComparer.Ordinal)];
            depot.Enregistrer(demande);
        }

        var pdfDeposes = demande.PdfDeposes.ToHashSet(StringComparer.Ordinal);
        var backlog = demande.Backlog ?? throw new InvalidOperationException("Backlog absent.");
        string? resume = null;
        IReadOnlyList<string> titres = [];
        List<TestFrontiere> frontieres = [];
        ResultatOracle? initiale = null;
        var adoptes = false;
        string? motifNonAdoption = null;
        if (demande.Tests is null && TestsPresents(repertoire))
        {
            await bac.PreparerAsync(ModeMontage.Exploration, ct);
            var pdfFabriques = PdfFabriques(repertoire, pdfDeposes);
            var (motif, presents) = pdfFabriques.Count > 0
                ? ($"PDF fabriqué par le QA amont : {CheminsAcceptation(pdfFabriques)} ; {ConsignePdfFabriques(pdfFabriques)}", [])
                : await ControlerTestsPresentsAsync(bac, demande, repertoire, backlog, ct);
            motif ??= repriseDeLEtape ? FrontieresSansCitation(presents, backlog) : null;
            if (motif is null)
            {
                (motif, initiale) = await ExecuterContreApplicationVideAsync(bac, demande, ct);
            }

            if (motif is null)
            {
                SignalerControleImpossible(demande, initiale);
                adoptes = true;
                resume = ResumeAdoption;
                titres = presents;
                depot.AjouterEvenement(demande, ChefDeProjet, "Tests déjà présents dans le dossier d'acceptation : ils se chargent, couvrent tous les critères et respectent les limites. Adoptés tels quels, sans appel au QA amont.");
            }
            else
            {
                motifNonAdoption = motif;
                depot.AjouterEvenement(demande, ChefDeProjet, $"Tests déjà présents dans le dossier d'acceptation, mais non adoptés : {PremierePhrase(motif)} Le QA amont prend le relais.");
                depot.Journaliser(demande.Id, $"Tests présents non adoptés : {motif}");
            }
        }

        if (!adoptes)
        {
            await bac.PreparerAsync(demande.NouveauxTests ? ModeMontage.EcritureTestsSansCode : ModeMontage.EcritureTests, ct);
            var fin = new SignalFin();
            var outils = new List<AIFunction>(OutilsDe(bac).Communs(Acceptation, Acceptation, ConseilInstallationTests, demande.NouveauxTests ? DossiersDuQaAmontSansCode : DossiersDuQaAmont))
            {
                OutilListerTests(bac),
                OutilSoumettreTests(bac, demande, backlog, precedentes, pdfDeposes, fin, (r, t, f, e) =>
                {
                    resume = r;
                    titres = t;
                    frontieres = f;
                    initiale = e;
                }),
            };
            var resultat = await ExecuterAvecRelancesAsync(
                demande,
                RoleAgent.QaAmont,
                consignes.PromptQaAmont(demande, reprises.Count > 0),
                consignes.MessageQaAmont(demande, motifNonAdoption),
                outils,
                fin,
                async echec =>
                {
                    try
                    {
                        if (!TestsPresents(repertoire))
                        {
                            return false;
                        }

                        NonRegression.Restaurer(depot, demande, precedentes);
                        if (demande.Tests is not null && string.Equals(Empreinte.Calculer(repertoire), demande.Tests.Empreinte, StringComparison.Ordinal))
                        {
                            return false;
                        }

                        var pdfFabriques = PdfFabriques(repertoire, pdfDeposes);
                        if (pdfFabriques.Count > 0)
                        {
                            depot.Journaliser(demande.Id, $"Tests présents non adoptés en repli : PDF fabriqué par le QA amont : {CheminsAcceptation(pdfFabriques)}.");
                            return false;
                        }

                        var (motif, presents) = await ControlerTestsPresentsAsync(bac, demande, repertoire, backlog, ct);
                        motif ??= FrontieresSansCitation(presents, backlog);
                        if (motif is null)
                        {
                            (motif, initiale) = await ExecuterContreApplicationVideAsync(bac, demande, ct);
                        }

                        if (motif is not null)
                        {
                            depot.Journaliser(demande.Id, $"Tests présents non adoptés en repli : {motif}");
                            return false;
                        }

                        SignalerControleImpossible(demande, initiale);
                        depot.AjouterEvenement(demande, ChefDeProjet, $"{SansResultat(RolesAgent.Libelle(RoleAgent.QaAmont), echec)} Les tests présents dans le dossier d'acceptation se chargent, couvrent tous les critères et respectent les limites : adoptés tels quels.");
                        resume = ResumeAdoption;
                        titres = presents;
                        adoptes = true;
                        return true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        depot.Journaliser(demande.Id, $"Repli d'adoption impossible : {ex.Message}");
                        return false;
                    }
                },
                ct);
            if (!fin.Termine && !adoptes)
            {
                throw new InvalidOperationException(SansResultat(RolesAgent.Libelle(RoleAgent.QaAmont), resultat));
            }
        }

        await bac.PreparerAsync(ModeMontage.Exploration, ct);
        var executionInitiale = initiale ?? throw new InvalidOperationException("Exécution des tests contre l'application vide absente.");
        MarquerReglesDOrigineRemplacees(demande, backlog);
        demande.PdfDeposes = null;
        demande.Tests = new TestsAcceptation
        {
            Resume = resume ?? "",
            Fichiers = [.. Empreinte.ListerFichiers(repertoire)],
            Titres = [.. titres],
            Empreinte = Empreinte.Calculer(repertoire),
            ExecutionInitiale = executionInitiale,
            Frontieres = frontieres,
        };
        depot.AjouterEvenement(
            demande,
            adoptes ? ChefDeProjet : RolesAgent.Libelle(RoleAgent.QaAmont),
            $"{titres.Count(t => !NonRegression.EstTitre(t))} tests {(adoptes ? "adoptés" : "écrits")}. Exécution avant tout code : {(NonRegression.ControleAvantCodeImpossible(executionInitiale) ? NonRegression.AvantCodeImpossible : $"{executionInitiale.Echoues} en échec sur {executionInitiale.Total}{NonRegression.SansReprisAvantCode(executionInitiale, titres)} (attendu : tous rouges)")}.");

        SignalerTestsHorsBacklog(demande, backlog, titres);

        if (demande.ModeAuto)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, "Mode auto : tests validés automatiquement et verrouillés.");
            demande.Etape = EtapeDemande.Construction;
            if (demande.NouveauxTests)
            {
                depot.AjouterEvenement(demande, ChefDeProjet, $"Vérification directe du livrable importé de la demande {demande.ReevaluationDe} avec ces nouveaux tests, sans passe de construction préalable.");
                demande.Etape = EtapeDemande.Verification;
            }
        }
        else
        {
            demande.Etape = EtapeDemande.ValidationTests;
            demande.Statut = StatutDemande.AttenteValidation;
        }
    }

    private async Task ConstructionAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        var tests = demande.Tests ?? throw new InvalidOperationException("Tests d'acceptation absents.");
        await bac.PreparerAsync(ModeMontage.Construction, ct);
        await PreparerGitAsync(bac, ct);
        await DeposerScriptsQaAsync(demande, bac, ct);
        if (!demande.ModeAuto && demande.Verifications.Count == 0)
        {
            var liste = await oracle.ListerTestsAsync(bac, ct);
            if (liste.Reussi)
            {
                tests.Titres = [.. liste.Titres];
            }
        }

        if (demande.Verifications.Count > 0 && !VerificationsDesTests(demande).Any())
        {
            await VerifierCodeRefuseAsync(demande, bac, tests, ct);
            if (demande.Etape == EtapeDemande.Livraison)
            {
                return;
            }
        }

        var maxTours = config.Limites.MaxIterationsAgent;
        if (demande.ToursPasseConstruction >= maxTours)
        {
            var modifiesAvantArret = await FichiersModifiesDeLaPasseAsync(demande, bac, ct);
            TerminerPasse(demande);
            depot.AjouterEvenement(demande, RolesAgent.Libelle(RoleAgent.Dev), $"Construction arrêtée sans conclusion (limite de {maxTours.ToString(CultureInfo.InvariantCulture)} tours atteinte avant l'interruption du traitement) ; l'oracle tranche.{modifiesAvantArret}");
            demande.Etape = EtapeDemande.Verification;
            return;
        }

        if (demande.ToursPasseConstruction > 0)
        {
            depot.Journaliser(demande.Id, $"Reprise de la construction interrompue : {demande.ToursPasseConstruction.ToString(CultureInfo.InvariantCulture)} tours déjà faits sur {maxTours.ToString(CultureInfo.InvariantCulture)}, il en reste {(maxTours - demande.ToursPasseConstruction).ToString(CultureInfo.InvariantCulture)} au Dev.");
        }

        demande.EmpreinteDebutPasse ??= await EmpreinteCodeAsync(bac, ct);
        var repertoire = depot.RepertoireAcceptation(demande.Id);
        var fin = new SignalFin();
        string? resume = null;
        var suivi = new SuiviTests();
        var outils = new List<AIFunction>(OutilsDe(bac).Communs(Espace, Espace).Select(o => SuiviTests.Modifie(o) ? new OutilQuiPerimeLesTests(o, suivi) : o))
        {
            OutilLancerTests(demande, bac, repertoire, tests.Empreinte, suivi, JournalOracle(demande.Id), SondesActives(demande) && demande.SignauxSignales.Count > 0),
            OutilTerminerConstruction(bac, fin, r => resume = r, suivi),
        };
        if (demande.ModeAuto)
        {
            outils.Add(OutilContesterTests(demande, fin, suivi));
        }

        var resultat = await ExecuterAgentAsync(
            demande,
            RoleAgent.Dev,
            consignes.PromptDev(demande),
            consignes.MessageDev(demande),
            outils,
            fin,
            ct);

        while (!fin.Termine
            && resultat.ContexteDepasse
            && demande.RelancesContexteNeuf < config.Limites.MaxRelancesRole
            && !resultat.BudgetEpuise
            && !PlafondAtteint(demande))
        {
            ExigerNonAnnulee(demande, ct);
            demande.RelancesContexteNeuf++;
            demande.ToursPasseConstruction = 0;
            depot.AjouterEvenement(
                demande,
                ChefDeProjet,
                $"Construction relancée avec un contexte neuf : le Dev a été arrêté par la limite de contexte ({Raison(resultat)}) ; relance n°{demande.RelancesContexteNeuf.ToString(CultureInfo.InvariantCulture)} sur {config.Limites.MaxRelancesRole.ToString(CultureInfo.InvariantCulture)}, dans la même passe.");
            depot.Enregistrer(demande);
            resultat = await ExecuterAgentAsync(
                demande,
                RoleAgent.Dev,
                consignes.PromptDev(demande),
                consignes.MessageDev(demande),
                outils,
                fin,
                ct);
        }

        var modifies = await FichiersModifiesDeLaPasseAsync(demande, bac, ct);
        TerminerPasse(demande);
        if (fin.Outil is "contester_tests" && demande.ContestationTests is { } contestation)
        {
            depot.AjouterEvenement(demande, RolesAgent.Libelle(RoleAgent.Dev), $"Tests contestés : {Couper(string.Join(" ; ", contestation.Titres), 500)}. Motif : {Couper(contestation.Motif, 300)}");
            demande.Etape = EtapeDemande.ArbitrageTests;
            return;
        }

        depot.AjouterEvenement(demande, RolesAgent.Libelle(RoleAgent.Dev), fin.Termine
            ? $"Construction terminée : {Couper(resume ?? "", 300)}{modifies}"
            : $"Construction arrêtée sans conclusion ({Raison(resultat)}) ; l'oracle tranche.{modifies}");
        demande.Etape = EtapeDemande.Verification;
        if (demande.ModeAuto && demande.ContestationTests is null && !PlafondAtteint(demande) && suivi.AJour && SoumissionRepereAmbigu(tests, suivi.DernierResultat) is { } soumission)
        {
            demande.ContestationTests = soumission;
            depot.AjouterEvenement(demande, ChefDeProjet, $"Tests au repère ambigu (« {Consignes.ViolationModeStrict} »), seuls encore rouges et non contestés par le Dev : l'usine les soumet d'office à l'arbitrage du QA amont : {Couper(string.Join(" ; ", soumission.Titres), 500)}.");
            demande.Etape = EtapeDemande.ArbitrageTests;
        }
    }

    private static void TerminerPasse(Demande demande)
    {
        demande.RetourPourDev = null;
        demande.ToursPasseConstruction = 0;
        demande.NaturePasse = NaturePasse.Ordinaire;
        demande.BloquantsQaDansLaPasse = false;
        demande.EmpreinteDebutPasse = null;
        demande.RelancesContexteNeuf = 0;
    }

    private async Task<string> FichiersModifiesDeLaPasseAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        if (demande.EmpreinteDebutPasse is not { } debut || await EmpreinteCodeAsync(bac, ct) is not { } fin)
        {
            return "";
        }

        if (string.Equals(debut, fin, StringComparison.Ordinal))
        {
            return "\nAucun fichier modifié pendant la passe.";
        }

        var difference = await bac.ExecuterAsync($"git -c core.quotepath=off diff --no-renames --numstat {debut} {fin}", TimeSpan.FromMinutes(1), ct: ct);
        var fichiers = difference.Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(ligne => ligne.Split('\t', 3))
            .Where(colonnes => colonnes.Length == 3)
            .Select(colonnes => colonnes[0] == "-" ? $"{colonnes[2]} (binaire)" : $"{colonnes[2]} (+{colonnes[0]} -{colonnes[1]})")
            .ToList();
        if (!difference.Reussi || fichiers.Count == 0)
        {
            return "";
        }

        var autres = fichiers.Count > MaxFichiersModifiesAnnonces ? $" et {(fichiers.Count - MaxFichiersModifiesAnnonces).ToString(CultureInfo.InvariantCulture)} autres" : "";
        return $"\nFichiers modifiés pendant la passe : {string.Join(", ", fichiers.Take(MaxFichiersModifiesAnnonces))}{autres}.";
    }

    private static ContestationTests? SoumissionRepereAmbigu(TestsAcceptation tests, ResultatOracle? dernier)
    {
        if (dernier is not { Succes: false, EtapeEchec: "tests" })
        {
            return null;
        }

        var rouges = dernier.Echecs.Select(e => e.Titre).Distinct(StringComparer.Ordinal).ToList();
        if (rouges.Count is 0 or > MaxTestsContestes
            || Consignes.TestsAuRepereAmbigu(dernier).Count != rouges.Count
            || rouges.Exists(t => !tests.Titres.Contains(t, StringComparer.Ordinal)))
        {
            return null;
        }

        return new ContestationTests
        {
            Titres = rouges,
            Motif = MotifRepereAmbigu,
            Preuve = string.Join(" ; ", dernier.Echecs
                .Where(e => e.Erreur.Contains(Consignes.ViolationModeStrict, StringComparison.Ordinal))
                .Select(e => $"{e.Titre} : {e.Erreur.Split('\n').Select(l => l.Trim()).First(l => l.Contains(Consignes.ViolationModeStrict, StringComparison.Ordinal))}")),
            Dependants = [.. dernier.Dependants.Intersect(rouges, StringComparer.Ordinal)],
            Echec = Consignes.FormaterResultat(dernier, budget: Consignes.BudgetRetourDev),
            ParLUsine = true,
        };
    }

    private async Task ArbitrageTestsAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        var contestation = demande.ContestationTests ?? throw new InvalidOperationException("Contestation des tests absente.");
        var tests = demande.Tests ?? throw new InvalidOperationException("Tests d'acceptation absents.");
        var backlog = demande.Backlog ?? throw new InvalidOperationException("Backlog absent.");
        var repertoire = depot.RepertoireAcceptation(demande.Id);
        var sauvegarde = Path.Combine(depot.Repertoire(demande.Id), "acceptance-contestee");
        if (!string.Equals(Empreinte.Calculer(sauvegarde), tests.Empreinte, StringComparison.Ordinal))
        {
            BacASableDocker.SupprimerRepertoire(sauvegarde);
            CopierFichiers(repertoire, sauvegarde);
        }

        await bac.PreparerAsync(ModeMontage.EcritureTests, ct);
        var fin = new SignalFin();
        string? decision = null;
        IReadOnlyList<string>? titres = null;
        var outils = new List<AIFunction>(OutilsDe(bac).Communs(Acceptation, Acceptation, ConseilInstallationTests, DossiersDuQaAmont))
        {
            OutilListerTests(bac),
            OutilSoumettreTestsReecrits(bac, demande, backlog, sauvegarde, fin, (r, t) =>
            {
                decision = r;
                titres = t;
            }),
            OutilMaintenirTests(fin, j => decision = j),
        };
        var resultat = await ExecuterAgentAsync(demande, RoleAgent.QaAmont, consignes.PromptQaAmont(demande, tests.Titres.Any(NonRegression.EstTitre)), Consignes.MessageArbitrage(demande), outils, fin, ct);
        if (titres is not null)
        {
            Empreinte.Verrouiller(tests, repertoire);
            tests.Titres = [.. titres];
            demande.VerificationsAvantTestsAjoutes = demande.Verifications.Count;
            demande.MeilleureTentative = null;
            demande.ResultatsParVersion = [];
            contestation.Reecrits = true;
            contestation.Decision = decision ?? "";
            depot.AjouterEvenement(demande, RolesAgent.Libelle(RoleAgent.QaAmont), $"Tests contestés réécrits, puis verrouillés de nouveau : {Couper(contestation.Decision, 500)}");
        }
        else
        {
            RestaurerFichiers(sauvegarde, repertoire);
            contestation.Reecrits = false;
            contestation.Decision = decision ?? $"{SansResultat(RolesAgent.Libelle(RoleAgent.QaAmont), resultat)} Les tests contestés sont maintenus.";
            depot.AjouterEvenement(demande, fin.Termine ? RolesAgent.Libelle(RoleAgent.QaAmont) : ChefDeProjet, $"Tests contestés maintenus : {Couper(contestation.Decision, 500)}");
        }

        demande.Etape = EtapeDemande.Verification;
        depot.Enregistrer(demande);
        BacASableDocker.SupprimerRepertoire(sauvegarde);
    }

    private static void CopierFichiers(string source, string destination)
    {
        foreach (var relatif in Empreinte.ListerFichiers(source))
        {
            var cible = Path.Combine(destination, relatif);
            Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
            File.Copy(Path.Combine(source, relatif), cible, overwrite: true);
        }
    }

    private static bool RestaurerNonRegression(string sauvegarde, string repertoire)
    {
        var reference = Path.Combine(sauvegarde, "tests", NonRegression.Dossier);
        var actuel = Path.Combine(repertoire, "tests", NonRegression.Dossier);
        if (string.Equals(Empreinte.Calculer(actuel), Empreinte.Calculer(reference), StringComparison.Ordinal))
        {
            return false;
        }

        BacASableDocker.SupprimerRepertoire(actuel);
        CopierFichiers(reference, actuel);
        return true;
    }

    private static HashSet<string> FichiersDeTestsModifies(string sauvegarde, string repertoire) =>
        Empreinte.ListerFichiers(sauvegarde).Union(Empreinte.ListerFichiers(repertoire), StringComparer.Ordinal)
            .Where(f => f.StartsWith("tests/", StringComparison.Ordinal) && !string.Equals(Empreinte.Calculer(sauvegarde, [f]), Empreinte.Calculer(repertoire, [f]), StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    private static void RestaurerFichiers(string sauvegarde, string repertoire)
    {
        foreach (var dossier in Directory.EnumerateDirectories(repertoire))
        {
            BacASableDocker.SupprimerRepertoire(dossier);
        }

        foreach (var fichier in Directory.EnumerateFiles(repertoire))
        {
            File.Delete(fichier);
        }

        CopierFichiers(sauvegarde, repertoire);
    }

    private async Task VerificationAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        var tests = demande.Tests ?? throw new InvalidOperationException("Tests d'acceptation absents.");
        await bac.PreparerAsync(ModeMontage.Construction, ct);
        if (demande.Verifications.Count == 0)
        {
            await PreparerGitAsync(bac, ct);
        }

        if (demande.RestaurationLancee && !demande.DernierVertRestaure)
        {
            await RevenirAuDernierVertAsync(demande, bac, ct);
        }

        depot.AjouterEvenement(demande, "Oracle", "Vérification : construction, démarrage, tests d'acceptation.");
        depot.Enregistrer(demande);
        var resultat = await oracle.VerifierAsync(bac, depot.RepertoireAcceptation(demande.Id), new OptionsVerification(true, true, tests.Empreinte, Journaliser: JournalOracle(demande.Id), Sondes: SondesActives(demande), TestsInstables: [.. demande.TestsInstables], IsolerEchecs: demande.ModeAuto, PersistanceExigee: ExigePersistance(demande)), ct);
        await ClasserInstablesAsync(demande, bac, resultat, essai: false, cumuler: true, ct);
        demande.Verifications.Add(resultat);
        if (demande.NouveauxTests && demande.Verifications.Count == 1)
        {
            depot.AjouterEvenement(demande, "Oracle", VerificationDuLivrableDOrigine(resultat));
        }

        if (resultat.Succes)
        {
            await CommiterVertAsync(demande, bac, resultat, ct);
            depot.AjouterEvenement(demande, "Oracle", $"Vérification réussie : {resultat.Reussis}/{resultat.Total} tests d'acceptation{Consignes.DontInstables(resultat, titres: true)}.");
            if (demande.DernierVertRestaure && !VerificationsDesTests(demande).SkipLast(1).Any(v => v.Succes))
            {
                demande.DernierVertRestaure = false;
                demande.RestaurationLancee = false;
            }

            var dernierVertNonEvalue = demande.DernierVertNonEvalue;
            demande.DernierVertNonEvalue = false;
            var relancePossible = !demande.DernierVertRestaure && demande.Corrections < config.Limites.MaxCorrections && !PlafondAtteint(demande);
            if (relancePossible
                && demande.EmpreintesCodeRefuse.Count > 0
                && await EmpreinteCodeAsync(bac, ct) is { } empreinte
                && demande.EmpreintesCodeRefuse.Contains(empreinte, StringComparer.Ordinal))
            {
                demande.Corrections++;
                demande.RetourPourDev = $"Ta dernière session de travail n'a rien changé au code : il est identique à la version que le demandeur a refusée. Son refus reste entièrement à traiter :\n{demande.CommentairesLivraison[^1]}";
                depot.AjouterEvenement(demande, ChefDeProjet, $"Livraison identique à la version refusée : le code n'a pas changé depuis le refus. Correction n°{demande.Corrections} demandée au Dev.");
                demande.Etape = EtapeDemande.Construction;
                return;
            }

            var dependances = await DependancesNonImporteesAsync(bac, ct);
            if (dependances.Count > 0)
            {
                depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : dépendance déclarée mais jamais importée : {string.Join(", ", dependances)}.");
            }

            var restes = await RestesDeDebogageAsync(bac, ct);
            if (restes.Count > 0)
            {
                depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : reste de débogage ou fichier de secours suivi par git : {string.Join(", ", restes)}.");
            }

            var regressions = await RejouerScriptsNonRegressionAsync(demande, bac, ct);
            var nouvellesDependances = dependances.Except(demande.DependancesNonImporteesSignalees, StringComparer.Ordinal).Any();
            var nouveauxRestes = restes.Except(demande.RestesDebogageSignales, StringComparer.Ordinal).Any();
            var relanceControles = relancePossible && (nouvellesDependances || nouveauxRestes);
            var relanceRegressions = relancePossible && regressions.Count > 0;
            var relanceSignaux = resultat.Signaux.Count > 0
                && demande.CorrectionsSignaux < config.Limites.MaxCorrectionsSignaux
                && !demande.DernierVertRestaure
                && !PlafondAtteint(demande);
            if (relanceControles || relanceRegressions || relanceSignaux)
            {
                var retoursPourDev = new List<string>();
                if (relanceControles)
                {
                    demande.DependancesNonImporteesSignalees = [.. demande.DependancesNonImporteesSignalees.Union(dependances, StringComparer.Ordinal)];
                    demande.RestesDebogageSignales = [.. demande.RestesDebogageSignales.Union(restes, StringComparer.Ordinal)];
                    demande.Corrections++;
                    var retours = new List<string>();
                    var objets = new List<string>();
                    if (nouvellesDependances)
                    {
                        retours.Add($"package.json déclare des dépendances que le code n'importe nulle part (aucun require, import ni import() ne les nomme) : {string.Join(", ", dependances)}.\n"
                            + "Retire chacune d'elles (npm uninstall <nom>, qui met aussi à jour package-lock.json). Les options de npm (--no-fund, --no-audit) ne sont pas des paquets : « npm install --no fund » installe le paquet « fund ». "
                            + "Si l'une d'elles est vraiment nécessaire sans être importée (chargée par l'outil de construction ou par la configuration d'un framework), garde-la et justifie-la dans le résumé de terminer_construction.");
                        objets.Add("ces dépendances");
                    }

                    if (nouveauxRestes)
                    {
                        retours.Add($"Ces fichiers suivis par git ressemblent à des restes de débogage ou à des fichiers de secours (copie .orig, .bak ou .rej, script tmp-* ou debug-*…) : {string.Join(", ", restes)}.\n"
                            + "Supprime chacun d'eux avec rm : le livrable ne contient que ce que demandent le besoin et le contrat. Retire aussi les routes de diagnostic ou de purge que tu as ajoutées pour te déboguer. "
                            + "Si l'un d'eux est vraiment nécessaire à l'application, garde-le et justifie-le dans le résumé de terminer_construction.");
                        objets.Add("ces fichiers");
                    }

                    retoursPourDev.Add($"{string.Join("\n\n", retours)} Relance ensuite lancer_tests_acceptation pour vérifier que rien n'est cassé.");
                    depot.AjouterEvenement(demande, ChefDeProjet, $"Correction n°{demande.Corrections} demandée au Dev : retirer ou justifier {string.Join(" et ", objets)}.");
                }

                if (regressions.Count > 0)
                {
                    retoursPourDev.Add(Consignes.RetourRegressions(regressions, ScriptsQaDev));
                }

                if (relanceRegressions)
                {
                    if (!relanceControles)
                    {
                        demande.Corrections++;
                    }

                    depot.AjouterEvenement(demande, ChefDeProjet, $"Correction n°{demande.Corrections} demandée au Dev : {regressions.Count.ToString(CultureInfo.InvariantCulture)} régression(s) d'un bloquant corrigé par une demande précédente du projet.");
                }

                if (relanceSignaux)
                {
                    demande.CorrectionsSignaux++;
                    demande.NaturePasse = NaturePasse.EcartsSonde;
                    retoursPourDev.Add(Consignes.MessageSignaux(resultat.Signaux, demande.SignauxSignales));
                    demande.SignauxSignales = [.. demande.SignauxSignales.Union(resultat.Signaux.Select(s => s.Cle), StringComparer.Ordinal)];
                    depot.AjouterEvenement(demande, ChefDeProjet, $"Correction des écarts relevés par la sonde demandée au Dev (passe n°{demande.CorrectionsSignaux} sur {config.Limites.MaxCorrectionsSignaux}) : {resultat.Signaux.Count} écart(s).");
                }

                demande.RetourPourDev = string.Join("\n\n", retoursPourDev);
                demande.DernierVertNonEvalue = true;
                demande.Etape = EtapeDemande.Construction;
                return;
            }

            if (resultat.Signaux.Count > 0)
            {
                depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : {resultat.Signaux.Count} écart(s) relevé(s) par la sonde restent non corrigés : {Couper(string.Join(" ; ", resultat.Signaux.Select(s => s.Message)), 1000)}");
            }

            foreach (var (script, _) in regressions)
            {
                depot.AjouterEvenement(demande, ChefDeProjet, $"Attention, plus de correction possible pour cette régression. {Consignes.Regression(script)}");
            }

            demande.Etape = (demande.DernierVertRestaure ? dernierVertNonEvalue || BloquantsARejouer(demande) : DoitEvaluer(demande)) ? EtapeDemande.Evaluation : EtapeDemande.Livraison;
            demande.EvaluationAnterieureAuDernierVert |= demande.Etape == EtapeDemande.Evaluation && BloquantsARejouer(demande) && (!demande.DernierVertRestaure || dernierVertNonEvalue);
            if (demande.Etape == EtapeDemande.Livraison && !demande.DernierVertRestaure && demande.Evaluation is { } evaluation && evaluation.Bloquants.Exists(b => b.Copie is null))
            {
                demande.EvaluationAnterieureAuDernierVert = true;
                depot.AjouterEvenement(demande, ChefDeProjet, "Livraison sans nouvelle évaluation du QA : le QA a relevé des bloquants sur une version vérifiée par l'oracle ; le Dev a été relancé pour les corriger ; la version livrée, vérifiée à son tour, n'a pas été réévaluée par le QA.");
            }

            return;
        }

        depot.AjouterEvenement(demande, "Oracle", $"Vérification en échec (étape « {resultat.EtapeEchec} ») : {resultat.Reussis}/{resultat.Total} tests{Consignes.DontInstables(resultat, titres: true)}.");
        if (!resultat.EmpreinteValide)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, "Alerte : les tests d'acceptation ont été modifiés depuis leur validation. Livraison en échec.");
            demande.Etape = EtapeDemande.Livraison;
            return;
        }

        await RetenirMeilleureTentativeAsync(demande, bac, resultat, ct);
        var budgetEpuise = PlafondAtteint(demande);
        if (demande.Corrections < config.Limites.MaxCorrections && !budgetEpuise)
        {
            var impasse = VerificationsDesTests(demande).SkipLast(1).LastOrDefault() is { } precedente && MemesEchecs(precedente, resultat) ? $"{Consignes.MemeEchec}\n\n" : "";
            var budget = Consignes.BudgetRetourDev - impasse.Length;
            var retour = await RepartirDeLaMeilleureTentativeAsync(demande, bac, resultat, budget, ct) ?? Consignes.FormaterRetourDev(resultat, demande, budget);
            demande.Corrections++;
            demande.RetourPourDev = impasse + retour;
            depot.AjouterEvenement(demande, ChefDeProjet, $"Correction n°{demande.Corrections} demandée au Dev.");
            demande.Etape = EtapeDemande.Construction;
            return;
        }

        if ((VerificationsDesTests(demande).Any(v => v.Succes) || demande.MeilleureTentative < demande.Verifications.Count) && !demande.DernierVertRestaure)
        {
            demande.RestaurationLancee = true;
            depot.Enregistrer(demande);
            await RevenirAuDernierVertAsync(demande, bac, ct);
            demande.Etape = EtapeDemande.Verification;
            return;
        }

        depot.AjouterEvenement(demande, ChefDeProjet, budgetEpuise
            ? "Plafond de coût atteint : livraison en l'état."
            : $"Nombre maximum de corrections atteint ({config.Limites.MaxCorrections}) : livraison en l'état.");
        demande.Etape = EtapeDemande.Livraison;
    }

    private bool SondesActives(Demande demande) => config.Limites.Sondes ?? demande.ModeAuto;

    private static bool ExigePersistance(Demande demande) => Phrase().Matches(demande.Texte).Any(m => ExigencePersistance().IsMatch(SansAccents(m.Value.Trim())));

    private async Task RevenirAuDernierVertAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        var vert = VerificationsDesTests(demande).Any(v => v.Succes);
        await RestaurerOuEchouerAsync(bac, vert, ct);

        demande.DernierVertRestaure = true;
        if (!vert && demande.MeilleureTentative is { } numero)
        {
            var meilleure = demande.Verifications[numero - 1];
            depot.AjouterEvenement(demande, ChefDeProjet, $"Plus de correction possible et aucune vérification verte : retour à la meilleure tentative (vérification n°{numero} : {meilleure.Reussis}/{meilleure.Total} tests réussis), puis nouvelle vérification.");
            return;
        }

        depot.AjouterEvenement(demande, ChefDeProjet, "Plus de correction possible : retour au dernier état vérifié (dernier commit vert), puis nouvelle vérification.");
        var empreinte = demande.EmpreintesCodeRefuse.Count > 0 ? await EmpreinteCodeAsync(bac, ct) : null;
        demande.VersionRefuseeRestauree = empreinte is not null
            ? demande.EmpreintesCodeRefuse.Contains(empreinte, StringComparer.Ordinal)
            : demande.CommentairesLivraison.Count > 0 && demande.Evaluation is null;
        if (demande.VersionRefuseeRestauree)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, "Attention : ce dernier état vert est la version que le demandeur a refusée ; son commentaire n'a pas pu être appliqué.");
        }

        if (demande.VersionRefuseeRestauree && demande.Evaluation is null && demande.EvaluationDernierVert is { } evaluationDernierVert)
        {
            demande.Evaluation = evaluationDernierVert;
            depot.AjouterEvenement(demande, ChefDeProjet, demande.EvaluationSansAvis
                ? $"{Consignes.SansAvisQa} {evaluationDernierVert.Synthese}"
                : "Évaluation du QA du dernier état vert reprise : le code livré est celui qu'il avait évalué.");
        }
    }

    private Action<string> JournalOracle(string id) => ligne => depot.Journaliser(id, $"[Oracle] {ligne}");

    private static async Task RestaurerOuEchouerAsync(IBacASable bac, bool vert, CancellationToken ct)
    {
        var restauration = vert
            ? await RestaurerAsync(bac, RefDernierVert, RefDernierVertComplet, ct)
            : await RestaurerAsync(bac, RefMeilleureTentative, RefMeilleureTentativeComplete, ct);
        if (!restauration.Reussi)
        {
            throw new InvalidOperationException($"{(vert ? "Retour au dernier état vérifié impossible" : "Retour à la meilleure tentative impossible")} : {Couper(restauration.SortieComplete, 300)}");
        }
    }

    private static Task<ResultatCommande> RestaurerAsync(IBacASable bac, string reference, string referenceComplete, CancellationToken ct) =>
        bac.ExecuterAsync(
            $"git reset --hard -q {reference} "
            + $"&& if git rev-parse -q --verify {referenceComplete} >/dev/null; then git clean -fdxq {Conserves} && git checkout {referenceComplete} -- . && git reset -q; else git clean -fdq; fi "
            + $"&& {RecreerDossiersVides}",
            TimeSpan.FromMinutes(1),
            ct: ct);

    private async Task VerifierCodeRefuseAsync(Demande demande, IBacASable bac, TestsAcceptation tests, CancellationToken ct)
    {
        depot.AjouterEvenement(demande, "Oracle", "Vérification du code refusé avec les tests ajoutés au refus, avant le travail du Dev : construction, démarrage, tests d'acceptation.");
        depot.Enregistrer(demande);
        var resultat = await oracle.VerifierAsync(bac, depot.RepertoireAcceptation(demande.Id), new OptionsVerification(true, true, tests.Empreinte, Journaliser: JournalOracle(demande.Id), TestsInstables: [.. demande.TestsInstables]), ct);
        await ClasserInstablesAsync(demande, bac, resultat, essai: false, cumuler: true, ct);
        demande.Verifications.Add(resultat);
        if (!resultat.EmpreinteValide)
        {
            depot.AjouterEvenement(demande, "Oracle", $"Vérification du code refusé en échec (étape « {resultat.EtapeEchec} ») : {resultat.Reussis}/{resultat.Total} tests.");
            depot.AjouterEvenement(demande, ChefDeProjet, "Alerte : les tests d'acceptation ont été modifiés depuis leur validation. Livraison en échec.");
            demande.Etape = EtapeDemande.Livraison;
            return;
        }

        if (resultat.Succes)
        {
            await CommiterVertAsync(demande, bac, resultat, ct);
        }
        else
        {
            await RetenirMeilleureTentativeAsync(demande, bac, resultat, ct);
        }

        depot.AjouterEvenement(demande, "Oracle", $"Code refusé, avec les tests ajoutés : {resultat.Reussis}/{resultat.Total} tests réussis{Consignes.DontInstables(resultat, titres: true)}{(resultat.Succes ? ". C'est le dernier état vert." : $" (étape « {resultat.EtapeEchec} »). C'est la référence des passes du Dev qui suivent.")}");
        depot.Enregistrer(demande);
    }

    private async Task CommiterVertAsync(Demande demande, IBacASable bac, ResultatOracle resultat, CancellationToken ct)
    {
        var commit = await CommiterAsync(demande, bac, $"{PrefixeCommit(demande)}vérification n°{demande.Verifications.Count} réussie ({resultat.Reussis}/{resultat.Total})", RefDernierVert, RefDernierVertComplet, "l'état vérifié", ct);
        if (!commit.Reussi)
        {
            throw new InvalidOperationException($"Commit de l'état vérifié impossible : {Couper(commit.SortieComplete, 300)}");
        }
    }

    private async Task RetenirMeilleureTentativeAsync(Demande demande, IBacASable bac, ResultatOracle resultat, CancellationToken ct)
    {
        if (VerificationsDesTests(demande).Any(v => v.Succes)
            || (demande.MeilleureTentative is { } meilleure && ComparerVerifications(resultat, demande.Verifications[meilleure - 1]) < 0))
        {
            return;
        }

        var commit = await CommiterAsync(demande, bac, $"{PrefixeCommit(demande)}vérification n°{demande.Verifications.Count} en échec ({resultat.Reussis}/{resultat.Total}), meilleure tentative", RefMeilleureTentative, RefMeilleureTentativeComplete, "la meilleure tentative", ct);
        if (commit.Reussi)
        {
            demande.MeilleureTentative = demande.Verifications.Count;
        }
        else
        {
            depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : la vérification n°{demande.Verifications.Count}, meilleure tentative à ce stade, n'a pas pu être enregistrée ({Couper(commit.SortieComplete, 300)}) ; l'usine ne pourra pas y revenir.");
        }
    }

    private static string PrefixeCommit(Demande demande) =>
        demande.ReevaluationDe is { } source ? $"usine : réévaluation de la demande {source}, " : "usine : ";

    private async Task<string?> RepartirDeLaMeilleureTentativeAsync(Demande demande, IBacASable bac, ResultatOracle resultat, int budget, CancellationToken ct)
    {
        var dernierVert = demande.Verifications.FindLastIndex(v => v.Succes) + 1;
        var vert = dernierVert > demande.VerificationsAvantTestsAjoutes;
        if ((vert ? dernierVert : demande.MeilleureTentative) is not { } numero
            || ComparerVerifications(resultat, demande.Verifications[numero - 1]) >= 0)
        {
            return null;
        }

        await RestaurerOuEchouerAsync(bac, vert, ct);

        var meilleure = demande.Verifications[numero - 1];
        var avant = $"{meilleure.Reussis}/{meilleure.Total}";
        var apres = $"{resultat.Reussis}/{meilleure.Total}{(resultat.Reussis == meilleure.Reussis ? RegressionAvantLesTests(resultat) : "")}";
        depot.AjouterEvenement(demande, ChefDeProjet, $"La dernière passe du Dev a fait passer les tests réussis de {avant} à {apres} : retour {(vert ? "au dernier état vert" : "à la meilleure tentative")} (vérification n°{numero}), dont repart la correction.");
        var refus = demande.CommentairesLivraison.Count > 0
            && ((demande.VerificationsAvantTestsAjoutes > 0 && numero == demande.VerificationsAvantTestsAjoutes + 1)
                || (demande.EmpreintesCodeRefuse.Count > 0 && await EmpreinteCodeAsync(bac, ct) is { } empreinte && demande.EmpreintesCodeRefuse.Contains(empreinte, StringComparer.Ordinal)))
            ? $"Cette version est celle que le demandeur a refusée ; son refus reste entièrement à traiter :\n{demande.CommentairesLivraison[^1]}\n"
            : "";
        var bloquants = vert && demande.Evaluation is { } evaluation && evaluation.Bloquants.Exists(b => b.Corrige is null)
            ? $"Le QA a relevé ces problèmes bloquants en utilisant l'application ; ils restent à corriger, sans casser les tests :\n{Consignes.ListeBloquantsQa(evaluation, ScriptsQaDev)}{(evaluation.Bloquants.Exists(b => b.ARejouer) ? $"{Consignes.ConsigneScriptsQa}\n" : "")}{Consignes.PointsSecondairesQa(evaluation)}"
            : "";
        demande.BloquantsQaDansLaPasse = bloquants.Length > 0;
        var aRetirer = new List<string>();
        if (vert && demande.DernierVertNonEvalue)
        {
            if (await DependancesNonImporteesAsync(bac, ct) is { Count: > 0 } dependances)
            {
                aRetirer.Add($"dépendance déclarée mais jamais importée : {string.Join(", ", dependances)}");
            }

            if (await RestesDeDebogageAsync(bac, ct) is { Count: > 0 } restes)
            {
                aRetirer.Add($"reste de débogage ou fichier de secours suivi par git : {string.Join(", ", restes)}");
            }
        }

        var controles = aRetirer.Count > 0
            ? $"Le contrôle de cette version a relevé ce qui reste à retirer, ou à justifier dans le résumé de terminer_construction si c'est vraiment nécessaire : {string.Join(" ; ", aRetirer)}.\n"
            : "";
        var entete = $"Ta dernière passe a fait passer les tests d'acceptation réussis de {avant} à {apres}. Le code de /workspace a été remis à la version à {avant} (vérification n°{numero}) : repars de là, sans réécrire ce qui y marche déjà. {refus}{bloquants}{controles}Résultat de l'oracle pour cette version :\n";
        return entete + Consignes.FormaterRetourDev(meilleure, demande, budget - entete.Length);
    }

    private static IEnumerable<ResultatOracle> VerificationsDesTests(Demande demande) => demande.Verifications.Skip(demande.VerificationsAvantTestsAjoutes);

    private static bool MemesEchecs(ResultatOracle precedente, ResultatOracle resultat) =>
        resultat.Echecs.Count > 0 && SignatureEchecs(precedente).SetEquals(SignatureEchecs(resultat));

    private static HashSet<string> SignatureEchecs(ResultatOracle resultat) =>
        resultat.Echecs.Select(e => $"{e.Titre}\n{Consignes.PremiereLigne(e.Erreur)}").ToHashSet(StringComparer.Ordinal);

    private static int ComparerVerifications(ResultatOracle a, ResultatOracle b) => (ReussisOuDependants(a), RangEtape(a)).CompareTo((ReussisOuDependants(b), RangEtape(b)));

    private static int ReussisOuDependants(ResultatOracle resultat) => resultat.Reussis + resultat.Echecs.Count(e => resultat.Dependants.Contains(e.Titre, StringComparer.Ordinal));

    private static int RangEtape(ResultatOracle resultat) => resultat.Succes || (resultat.Dependants.Count > 0 && resultat.Echecs.TrueForAll(e => resultat.Dependants.Contains(e.Titre, StringComparer.Ordinal))) ? 3 : resultat.EtapeEchec switch
    {
        "tests" => 2,
        "demarrage" => 1,
        _ => 0,
    };

    private static string RegressionAvantLesTests(ResultatOracle resultat) => resultat.EtapeEchec switch
    {
        "build" => " (ne se construit plus)",
        "demarrage" => " (ne démarre plus)",
        _ => "",
    };

    private bool PlafondAtteint(Demande demande) => config.Limites.PlafondCoutDemande is { } plafond && plafond - demande.CoutTotal <= 0;

    private void ExigerNonAnnulee(Demande demande, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depot.AnnulationDemandee(demande.Id))
        {
            throw new OperationCanceledException("Annulation demandée.");
        }
    }

    private bool CorrectionEvaluationPossible(Demande demande) =>
        demande.CorrectionsEvaluation < config.Limites.MaxCorrectionsEvaluation && !demande.DernierVertRestaure && !PlafondAtteint(demande);

    private async Task<ResultatCommande> ConstruireEtDemarrerAsync(IBacASable bac, CancellationToken ct)
    {
        var demarrage = await oracle.ConstruireSiBesoinAsync(bac, ct);
        if (demarrage.Code == 0)
        {
            demarrage = await oracle.DemarrerApplicationAsync(bac, ct);
        }

        return demarrage;
    }

    private bool DoitEvaluer(Demande demande) =>
        demande.Evaluation is null
        || BloquantsARejouer(demande)
        || (demande.Evaluation.Bloquants.Exists(b => b.Copie is null) && demande.CorrectionsEvaluation < config.Limites.MaxCorrectionsEvaluation);

    private static bool BloquantsARejouer(Demande demande) => demande.Evaluation is { } evaluation && evaluation.Bloquants.Exists(b => b.ARejouer);

    private static bool RejoueParLUsine(Bloquant bloquant) => bloquant is { Copie: not null, Corrige: not false };

    private async Task EvaluationAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        if (!config.Limites.EvaluationQa)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, "Livraison sans évaluation du QA (désactivée par la configuration).");
            demande.Etape = EtapeDemande.Livraison;
            return;
        }

        if (BloquantsARejouer(demande))
        {
            await RejouerBloquantsAsync(demande, demande.Evaluation!, bac, ct);
            return;
        }

        if (demande.Evaluation is { ReverificationDemandee: true } evaluee)
        {
            await ReverifierAsync(demande, evaluee, bac, ct);
            return;
        }

        Evaluation? evaluation = null;
        var rendue = false;
        if (PlafondAtteint(demande))
        {
            evaluation = new Evaluation { Synthese = "Évaluation non réalisée : plafond de coût atteint." };
        }
        else
        {
            await bac.PreparerAsync(ModeMontage.Construction, ct);
            await bac.ExecuterAsync($"git update-ref {RefVersionEvaluee} HEAD", TimeSpan.FromMinutes(1), ct: ct);
            var demarrage = await ConstruireEtDemarrerAsync(bac, ct);

            if (demarrage.Code != 0)
            {
                await OracleAcceptation.ExigerBacDisponibleAsync(bac, ct);
                evaluation = new Evaluation
                {
                    Bloquants = [new Bloquant { Constat = $"L'application ne démarre pas pour l'évaluation : {Couper(demarrage.SortieComplete, 1500)}" }],
                    Synthese = "Évaluation impossible : l'application ne démarre pas.",
                };
            }
            else
            {
                try
                {
                    var fin = new SignalFin();
                    string? dernierRefus = null;
                    var outils = new List<AIFunction>(OutilsDe(bac).Communs("/tmp", "/tmp", ConseilInstallationScripts, DossiersDuQa))
                    {
                        OutilSoumettreEvaluation(
                            demande,
                            bac,
                            fin,
                            $"{ProchaineConversation(demande, RoleAgent.Qa)}{SuffixeSorties}",
                            e =>
                            {
                                evaluation = e;
                                rendue = true;
                            },
                            refusee: r => dernierRefus = r),
                    };
                    var resultat = await ExecuterAgentAsync(demande, RoleAgent.Qa, consignes.PromptQa(), consignes.MessageQa(demande, LireContrat(demande)), outils, fin, ct);
                    if (resultat.BudgetEpuise || resultat.MotifArret is { Length: > 0 })
                    {
                        evaluation ??= new Evaluation { Synthese = $"Évaluation non rendue : {Raison(resultat)}{(dernierRefus is null ? "" : $" ; sa dernière évaluation soumise avait été refusée : {Couper(dernierRefus.ReplaceLineEndings(" ").TrimEnd('.'), 1000)}")}." };
                    }
                }
                catch (Exception ex) when (EstPanneAgent(ex, demande, ct))
                {
                    depot.Journaliser(demande.Id, ex.ToString());
                    evaluation ??= new Evaluation { Synthese = $"Évaluation interrompue par une erreur du modèle : {Couper(ex.Message, 300)}" };
                }
                finally
                {
                    await ArreterApplicationEtRestaurerAsync(bac);
                }
            }
        }

        evaluation ??= new Evaluation { Synthese = "Le QA n'a pas rendu d'évaluation." };
        if (!rendue && evaluation.Bloquants.Count == 0 && demande.Evaluation is { Bloquants.Count: > 0 })
        {
            demande.EvaluationAnterieureAuDernierVert = true;
            depot.AjouterEvenement(demande, ChefDeProjet, $"Livraison sans nouvelle évaluation du QA : les bloquants relevés sur la version précédente n'ont pas été revérifiés. {evaluation.Synthese}");
            demande.Etape = EtapeDemande.Livraison;
            return;
        }

        demande.Evaluation = evaluation;
        var sansAvis = !rendue && demande.Evaluation.Bloquants.Count == 0;
        demande.EvaluationSansAvis = sansAvis;
        depot.AjouterEvenement(
            demande,
            sansAvis ? ChefDeProjet : RolesAgent.Libelle(RoleAgent.Qa),
            sansAvis
                ? $"{Consignes.SansAvisQa} {demande.Evaluation.Synthese}"
                : $"Évaluation : {demande.Evaluation.Bloquants.Count} bloquant(s), {demande.Evaluation.Remarques.Count} remarque(s).");
        if (demande.Evaluation.Bloquants.Count > 0 && CorrectionEvaluationPossible(demande))
        {
            demande.CorrectionsEvaluation++;
            demande.NaturePasse = NaturePasse.BloquantsQa;
            demande.Evaluation.TransmiseAuDev = true;
            demande.RetourPourDev = Consignes.RetourBloquantsQa("Le QA a relevé ces problèmes bloquants en utilisant l'application :", demande.Evaluation, ScriptsQaDev);
            depot.AjouterEvenement(demande, ChefDeProjet, "Correction demandée au Dev suite à l'évaluation.");
            demande.Etape = EtapeDemande.Construction;
            return;
        }

        demande.Etape = EtapeDemande.Livraison;
    }

    private async Task RejouerBloquantsAsync(Demande demande, Evaluation evaluation, IBacASable bac, CancellationToken ct)
    {
        var aRejouer = Enumerable.Range(0, evaluation.Bloquants.Count).Where(i => RejoueParLUsine(evaluation.Bloquants[i])).ToList();
        var dejaCorriges = aRejouer.Where(i => evaluation.Bloquants[i].Corrige == true).ToHashSet();
        depot.AjouterEvenement(demande, ChefDeProjet, $"Rejeu des scripts de reproduction du QA sur cette version, application démarrée : {aRejouer.Count} bloquant(s).");
        depot.Enregistrer(demande);
        await bac.PreparerAsync(ModeMontage.Construction, ct);
        var demarrage = await ConstruireEtDemarrerAsync(bac, ct);

        var epreuves = new Dictionary<int, EpreuveScript>();
        try
        {
            if (demarrage.Code != 0)
            {
                await OracleAcceptation.ExigerBacDisponibleAsync(bac, ct);
            }
            else
            {
                foreach (var i in aRejouer)
                {
                    var copie = evaluation.Bloquants[i].Copie!;
                    epreuves[i] = await EprouverScriptQaAsync(demande, bac, copie, Path.ChangeExtension(copie, $".rejeu-{demande.CorrectionsEvaluation.ToString(CultureInfo.InvariantCulture)}.txt"), IssueScript.Passe, ct);
                }
            }
        }
        finally
        {
            await ArreterApplicationEtRestaurerAsync(bac);
        }

        var sorties = new Dictionary<int, string>();
        if (demarrage.Code != 0)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, $"Rejeu des scripts de reproduction du QA impossible : l'application ne démarre pas : {Couper(demarrage.SortieComplete.Trim().ReplaceLineEndings(" "), 300)}");
            foreach (var i in aRejouer)
            {
                evaluation.Bloquants[i].Corrige = null;
                evaluation.Bloquants[i].ErreurRejeu = Consignes.ApplicationArreteeAuRejeu;
                sorties[i] = Consignes.RejeuImpossible(demarrage.SortieComplete);
            }
        }

        foreach (var (i, epreuve) in epreuves)
        {
            var bloquant = evaluation.Bloquants[i];
            var numero = (i + 1).ToString(CultureInfo.InvariantCulture);
            bloquant.ErreurRejeu = epreuve.Issue == IssueScript.Erreur ? ErreurScript(epreuve.Execution) : null;
            bloquant.Corrige = epreuve.Issue == IssueScript.Passe ? true : null;
            switch (epreuve.Issue)
            {
                case IssueScript.Passe when !dejaCorriges.Contains(i):
                    depot.AjouterEvenement(demande, ChefDeProjet, $"Bloquant n°{numero} corrigé : {bloquant.Attendu}");
                    break;
                case IssueScript.Reproduit:
                    sorties[i] = Consignes.SortieReproduite(epreuve.Execution.SortieComplete);
                    break;
                case IssueScript.Erreur:
                    depot.AjouterEvenement(demande, ChefDeProjet, $"Rejeu du bloquant n°{numero} en erreur : {bloquant.ErreurRejeu}");
                    sorties[i] = Consignes.SortieEnErreur(MotifErreurScript(epreuve.Execution), epreuve.Execution.SortieComplete);
                    break;
            }
        }

        if (sorties.Count == 0)
        {
            if (CorrectionEvaluationPossible(demande))
            {
                evaluation.ReverificationDemandee = true;
                depot.AjouterEvenement(demande, ChefDeProjet, "Revérification du QA demandée : le rejeu clôt les bloquants ; le QA vérifie leurs variantes et cherche les régressions de la correction, application démarrée, sans refaire toute l'évaluation.");
                return;
            }

            depot.AjouterEvenement(demande, ChefDeProjet, $"Pas de revérification du QA : {MotifSansReverification(demande)} ; livraison après le rejeu.");
            demande.Etape = EtapeDemande.Livraison;
            return;
        }

        if (CorrectionEvaluationPossible(demande))
        {
            demande.CorrectionsEvaluation++;
            demande.NaturePasse = NaturePasse.BloquantsQa;
            foreach (var i in sorties.Keys.Where(i => epreuves.GetValueOrDefault(i)?.Issue == IssueScript.Reproduit))
            {
                depot.AjouterEvenement(demande, ChefDeProjet, $"Bloquant n°{(i + 1).ToString(CultureInfo.InvariantCulture)} {(dejaCorriges.Contains(i) ? "de nouveau" : "toujours")} reproduit par son script : {evaluation.Bloquants[i].Attendu}");
            }

            evaluation.TransmiseAuDev = true;
            demande.RetourPourDev = Consignes.RetourBloquantsQa(
                demarrage.Code != 0
                    ? "Après ta correction, l'usine n'a pas pu rejouer les scripts de reproduction du QA sur ta version : l'application ne démarre pas. Ces problèmes bloquants restent à corriger et à vérifier."
                    : "Après ta correction, l'usine a rejoué les scripts de reproduction du QA sur ta version, application démarrée : ces problèmes bloquants ne sont pas clos.",
                evaluation,
                ScriptsQaDev,
                sorties);
            depot.AjouterEvenement(demande, ChefDeProjet, "Correction demandée au Dev : des bloquants du QA ne sont pas clos par le rejeu de leur script.");
            demande.Etape = EtapeDemande.Construction;
            return;
        }

        foreach (var i in sorties.Keys)
        {
            var bloquant = evaluation.Bloquants[i];
            bloquant.Corrige = false;
            depot.AjouterEvenement(demande, ChefDeProjet, bloquant.ErreurRejeu switch
            {
                null => $"Bloquant non résolu : {bloquant.Attendu}",
                Consignes.ApplicationArreteeAuRejeu => $"Bloquant non résolu, script non rejoué ({Consignes.ApplicationArreteeAuRejeu}) : {bloquant.Attendu}",
                _ => $"Bloquant non résolu, rejeu de son script en erreur : {bloquant.Attendu}",
            });
        }

        demande.Etape = EtapeDemande.Livraison;
    }

    private string MotifSansReverification(Demande demande) => demande switch
    {
        { DernierVertRestaure: true } => "retour au dernier état vert, plus de correction possible",
        _ when PlafondAtteint(demande) => "plafond de coût atteint",
        _ => $"la correction n°{demande.CorrectionsEvaluation.ToString(CultureInfo.InvariantCulture)} des bloquants du QA était la dernière possible ({demande.CorrectionsEvaluation.ToString(CultureInfo.InvariantCulture)}/{config.Limites.MaxCorrectionsEvaluation.ToString(CultureInfo.InvariantCulture)})",
    };

    private async Task ReverifierAsync(Demande demande, Evaluation evaluation, IBacASable bac, CancellationToken ct)
    {
        Evaluation? soumise = null;
        string? motif = null;
        if (PlafondAtteint(demande))
        {
            motif = "plafond de coût atteint";
        }
        else
        {
            await bac.PreparerAsync(ModeMontage.Construction, ct);
            var diff = await bac.ExecuterAsync(
                $"git -c core.quotepath=off diff --no-color --stat {RefVersionEvaluee} HEAD && git -c core.quotepath=off diff --no-color {RefVersionEvaluee} HEAD",
                TimeSpan.FromMinutes(1),
                ct: ct);
            var clos = new List<(Bloquant Bloquant, string Script, string Chemin)>();
            foreach (var bloquant in evaluation.Bloquants.Where(b => b is { Corrige: true, Copie: not null }))
            {
                clos.Add((bloquant, Couper(File.ReadAllText(Path.Combine(depot.Repertoire(demande.Id), bloquant.Copie!)).Trim(), MaxCaracteresScriptClos), await DeposerScriptQaAsync(demande, bac, bloquant.Copie!, ct)));
            }

            var message = consignes.MessageQaReverification(demande, LireContrat(demande), evaluation, clos, diff.Reussi ? Couper(diff.Sortie.Trim(), MaxCaracteresDiff) : null);
            var demarrage = await ConstruireEtDemarrerAsync(bac, ct);

            if (demarrage.Code != 0)
            {
                await OracleAcceptation.ExigerBacDisponibleAsync(bac, ct);
                motif = $"l'application ne démarre pas : {Couper(demarrage.SortieComplete.Trim().ReplaceLineEndings(" "), 300)}";
            }
            else
            {
                try
                {
                    var fin = new SignalFin();
                    var outils = new List<AIFunction>(OutilsDe(bac).Communs("/tmp", "/tmp", ConseilInstallationScripts, DossiersDuQa))
                    {
                        OutilSoumettreEvaluation(demande, bac, fin, $"{ProchaineConversation(demande, RoleAgent.Qa)}{SuffixeSorties}", e => soumise = e, evaluation.Bloquants.Count),
                    };
                    var resultat = await ExecuterAgentAsync(demande, RoleAgent.Qa, QaReverification, config.Limites.MaxIterationsReverification, consignes.PromptQaReverification(), message, outils, fin, ct);
                    if (soumise is null)
                    {
                        motif = Raison(resultat);
                    }
                }
                catch (Exception ex) when (EstPanneAgent(ex, demande, ct))
                {
                    depot.Journaliser(demande.Id, ex.ToString());
                    motif = $"erreur du modèle : {Couper(ex.Message, 300)}";
                }
                finally
                {
                    await ArreterApplicationEtRestaurerAsync(bac);
                }
            }
        }

        evaluation.ReverificationDemandee = false;
        if (soumise is null)
        {
            motif ??= "le QA n'a pas rendu de revérification";
            evaluation.Reverifications.Add(new Reverification { Synthese = motif });
            depot.AjouterEvenement(demande, ChefDeProjet, $"Revérification du QA sans avis : {motif} ; livraison.");
            demande.Etape = EtapeDemande.Livraison;
            return;
        }

        demande.EvaluationAnterieureAuDernierVert = false;
        foreach (var bloquant in soumise.Bloquants)
        {
            bloquant.ReleveALaReverification = true;
            evaluation.Bloquants.Add(bloquant);
        }

        evaluation.Reverifications.Add(new Reverification
        {
            Rendue = true,
            Bloquants = soumise.Bloquants.Count,
            Remarques = soumise.Remarques,
            Synthese = soumise.Synthese,
            Controles = soumise.Controles,
            NonTeste = soumise.NonTeste,
        });
        depot.AjouterEvenement(demande, QaReverification, soumise.Bloquants.Count == 0
            ? $"Revérification sans bloquant, {soumise.Remarques.Count.ToString(CultureInfo.InvariantCulture)} remarque(s)."
            : $"Revérification : {soumise.Bloquants.Count.ToString(CultureInfo.InvariantCulture)} bloquant(s) nouveau(x), {soumise.Remarques.Count.ToString(CultureInfo.InvariantCulture)} remarque(s).");
        if (soumise.Bloquants.Count > 0 && CorrectionEvaluationPossible(demande))
        {
            demande.CorrectionsEvaluation++;
            demande.NaturePasse = NaturePasse.BloquantsQa;
            evaluation.TransmiseAuDev = true;
            demande.RetourPourDev = Consignes.RetourBloquantsQa("Après ta correction, la revérification du QA a relevé ces problèmes bloquants en utilisant l'application :", evaluation, ScriptsQaDev);
            depot.AjouterEvenement(demande, ChefDeProjet, "Correction demandée au Dev suite à la revérification.");
            demande.Etape = EtapeDemande.Construction;
            return;
        }

        demande.Etape = EtapeDemande.Livraison;
    }

    private async Task ArreterApplicationEtRestaurerAsync(IBacASable bac)
    {
        await oracle.ArreterApplicationAsync(bac, CancellationToken.None);
        await bac.ExecuterAsync(
            "{ git checkout -- .; git clean -fdq; "
            + $"if git rev-parse -q --verify {RefDernierVertComplet} >/dev/null && git checkout {RefDernierVertComplet} -- .; then git clean -fdxq {Conserves}; fi; "
            + $"git reset -q; {RecreerDossiersVides}; }} 2>/dev/null; true",
            TimeSpan.FromMinutes(1),
            ct: CancellationToken.None);
    }

    private async Task<List<(ScriptNonRegression Script, string Sortie)>> RejouerScriptsNonRegressionAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        var reprises = NonRegression.Repertoire(depot, demande.Id);
        var scripts = NonRegression.ReprendreScripts(depot, demande, [.. NonRegression.Precedentes(depot, demande).Where(p => Directory.Exists(Path.Combine(reprises, p.Id)))]);
        if (scripts.Count == 0)
        {
            return [];
        }

        depot.AjouterEvenement(demande, ChefDeProjet, $"Rejeu des scripts de non-régression du projet (bloquants corrigés par ses demandes précédentes) sur cette version, application démarrée : {scripts.Count.ToString(CultureInfo.InvariantCulture)} script(s).");
        depot.Enregistrer(demande);
        var demarrage = await ConstruireEtDemarrerAsync(bac, ct);

        var regressions = new List<(ScriptNonRegression Script, string Sortie)>();
        try
        {
            if (demarrage.Code != 0)
            {
                await OracleAcceptation.ExigerBacDisponibleAsync(bac, ct);
                depot.AjouterEvenement(demande, ChefDeProjet, $"Rejeu des scripts de non-régression impossible : l'application ne démarre pas : {Couper(demarrage.SortieComplete.Trim().ReplaceLineEndings(" "), 300)}");
                return regressions;
            }

            foreach (var script in scripts)
            {
                var epreuve = await EprouverScriptQaAsync(demande, bac, script.Copie, Path.ChangeExtension(script.Copie, $".verification-{demande.Verifications.Count.ToString(CultureInfo.InvariantCulture)}.txt"), IssueScript.Reproduit, ct);
                switch (epreuve.Issue)
                {
                    case IssueScript.Reproduit:
                        regressions.Add((script, epreuve.Execution.SortieComplete));
                        depot.AjouterEvenement(demande, ChefDeProjet, Consignes.Regression(script));
                        break;
                    case IssueScript.Erreur:
                        depot.AjouterEvenement(demande, ChefDeProjet, $"Script de non-régression du bloquant n°{script.Numero.ToString(CultureInfo.InvariantCulture)} de la demande {script.Demande} en erreur, sans verdict : {ErreurScript(epreuve.Execution)}");
                        break;
                }
            }
        }
        finally
        {
            await ArreterApplicationEtRestaurerAsync(bac);
        }

        return regressions;
    }

    private async Task DeposerScriptsQaAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        var nonRegression = Path.Combine(depot.Repertoire(demande.Id), NonRegression.DossierScripts);
        if (Directory.Exists(nonRegression))
        {
            foreach (var fichier in Directory.EnumerateFiles(nonRegression).Where(f => ExtensionsScriptQa.Contains(Path.GetExtension(f), StringComparer.Ordinal)).Order(StringComparer.Ordinal))
            {
                await DeposerScriptQaAsync(demande, bac, $"{NonRegression.DossierScripts}/{Path.GetFileName(fichier)}", ct);
            }
        }

        if (demande.Evaluation is not { } evaluation)
        {
            return;
        }

        foreach (var bloquant in evaluation.Bloquants.Where(b => b.ARejouer))
        {
            await DeposerScriptQaAsync(demande, bac, bloquant.Copie!, ct);
        }
    }

    private async Task<string> DeposerScriptQaAsync(Demande demande, IBacASable bac, string copie, CancellationToken ct)
    {
        var cible = $"{ScriptsQaDev}/{Path.GetFileName(copie)}";
        var contenu = Convert.ToBase64String(await File.ReadAllBytesAsync(Path.Combine(depot.Repertoire(demande.Id), copie), ct));
        var depose = await ExecuterAvecReprisesAsync(demande, () => bac.ExecuterAsync($"mkdir -p {ScriptsQaDev} && rm -rf -- {OutilsAgent.Quoter(cible)} && base64 -d > {OutilsAgent.Quoter(cible)}", TimeSpan.FromMinutes(1), "/tmp", contenu, ct), ct);
        if (EstErreurDocker(depose))
        {
            throw new BacIndisponibleException($"Dépôt du script de reproduction {cible} dans le bac à sable impossible : {MotifBacIndisponible}.");
        }

        if (!depose.Reussi)
        {
            throw new InvalidOperationException($"Dépôt du script de reproduction {cible} dans le bac à sable impossible : {Couper(depose.SortieComplete, 300)}");
        }

        return cible;
    }

    private async Task<ResultatCommande> ExecuterAvecReprisesAsync(Demande demande, Func<Task<ResultatCommande>> commande, CancellationToken ct)
    {
        var resultat = await commande();
        for (var reprise = 1; reprise <= ReprisesBac && EstErreurDocker(resultat); reprise++)
        {
            depot.Journaliser(demande.Id, $"Bac à sable indisponible ({Couper(resultat.Erreurs.Trim(), 300)}) : nouvelle tentative n°{reprise.ToString(CultureInfo.InvariantCulture)} sur {ReprisesBac.ToString(CultureInfo.InvariantCulture)} dans {AttenteRepriseBac.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s.");
            await Task.Delay(AttenteRepriseBac, ct);
            resultat = await commande();
        }

        return resultat;
    }

    internal static bool EstErreurDocker(ResultatCommande resultat) =>
        !resultat.Reussi && resultat.Erreurs.TrimStart().StartsWith("Error response from daemon:", StringComparison.Ordinal);

    private async Task<ResultatCommande> ExecuterScriptQaAsync(Demande demande, IBacASable bac, string copie, string? sortie, bool ajouter, CancellationToken ct)
    {
        var chemin = await DeposerScriptQaAsync(demande, bac, copie, ct);
        var execution = await ExecuterAvecReprisesAsync(demande, () => bac.ExecuterAsync(CommandeScriptQa(chemin, config.BacASable.Port), DelaiScriptQa, "/tmp", ct: ct), ct);
        if (EstErreurDocker(execution))
        {
            throw new BacIndisponibleException($"Exécution du script de reproduction {chemin} dans le bac à sable impossible : {MotifBacIndisponible}.");
        }

        if (sortie is not null)
        {
            var fichier = Path.Combine(depot.Repertoire(demande.Id), sortie);
            var texte = $"code de sortie : {execution.Code.ToString(CultureInfo.InvariantCulture)}\n{execution.SortieComplete}";
            if (ajouter)
            {
                await File.AppendAllTextAsync(fichier, $"\n\n{texte}", ct);
            }
            else
            {
                await File.WriteAllTextAsync(fichier, texte, ct);
            }
        }

        return execution;
    }

    private async Task<EpreuveScript> EprouverScriptQaAsync(Demande demande, IBacASable bac, string copie, string? sortie, IssueScript aConfirmer, CancellationToken ct)
    {
        var premiere = await ExecuterScriptQaAsync(demande, bac, copie, sortie, ajouter: false, ct);
        if (Issue(premiere) != aConfirmer)
        {
            return new EpreuveScript(Issue(premiere), premiere, AuPremierEssai: true);
        }

        var seconde = await ExecuterScriptQaAsync(demande, bac, copie, sortie, ajouter: true, ct);
        return new EpreuveScript(Issue(seconde), seconde, AuPremierEssai: false);
    }

    private static IssueScript Issue(ResultatCommande execution) => execution switch
    {
        { DelaiDepasse: true } => IssueScript.Erreur,
        { Code: 0 } => IssueScript.Passe,
        { Code: 1 } => IssueScript.Reproduit,
        _ => IssueScript.Erreur,
    };

    private static string MotifErreurScript(ResultatCommande execution) =>
        execution.DelaiDepasse
            ? $"délai de {DelaiScriptQa.TotalMinutes.ToString(CultureInfo.InvariantCulture)} min dépassé"
            : $"code de sortie {execution.Code.ToString(CultureInfo.InvariantCulture)}";

    private static string ErreurScript(ResultatCommande execution) =>
        execution.SortieComplete.Trim() is { Length: > 0 } sortie
            ? $"{MotifErreurScript(execution)} : {Couper(sortie.ReplaceLineEndings(" "), 300)}"
            : MotifErreurScript(execution);

    internal static string CommandeScriptQa(string chemin, int port) =>
        $"USINE_URL=http://localhost:{port.ToString(CultureInfo.InvariantCulture)} "
        + (string.Equals(Path.GetExtension(chemin), ".sh", StringComparison.Ordinal)
            ? $"bash {OutilsAgent.Quoter(chemin)}"
            : $"NODE_PATH=/opt/acceptance-runner/node_modules node --import {OutilsAgent.Quoter(GardeNode)} {OutilsAgent.Quoter(chemin)}");

    internal static string? PauseFixe(string script, string extension)
    {
        if (!string.Equals(extension, ".sh", StringComparison.Ordinal))
        {
            return PauseFixeNode().Matches(script).FirstOrDefault(m => !DansUneBoucle(script, m.Index)) is { } pause ? pause.Groups["pause"].Value : null;
        }

        var boucles = 0;
        foreach (Match mot in MotBoucleOuPause().Matches(script).Where(m => m.Groups["mot"].Success))
        {
            switch (mot.Groups["mot"].Value)
            {
                case "do":
                    boucles++;
                    break;
                case "done":
                    boucles = Math.Max(0, boucles - 1);
                    break;
                default:
                    if (boucles == 0)
                    {
                        return mot.Groups["mot"].Value;
                    }

                    break;
            }
        }

        return null;
    }

    private static bool DansUneBoucle(string script, int position)
    {
        var blocs = new Stack<bool>();
        var debut = 0;
        for (var i = 0; i < position; i++)
        {
            if (script[i] == '{')
            {
                blocs.Push(EnTeteDeBoucle().IsMatch(script[debut..i]));
                debut = i + 1;
            }
            else if (script[i] == '}')
            {
                blocs.TryPop(out _);
                debut = i + 1;
            }
        }

        return blocs.Contains(true);
    }

    private async Task<string> RejouerScriptsPourLeDevAsync(Demande demande, IBacASable bac, ResultatOracle resultat, CancellationToken ct)
    {
        const string Entete = "Scripts de reproduction des bloquants du QA";
        if (!resultat.EmpreinteValide || Consignes.EchecAvantLesTests(resultat) is not null)
        {
            return $"\n{Entete} non rejoués : l'application ne se construit pas ou ne démarre pas.\n";
        }

        var texte = new StringBuilder();
        var demarrage = await oracle.DemarrerApplicationAsync(bac, ct);
        try
        {
            if (demarrage.Code != 0)
            {
                return $"\n{Entete} non rejoués : l'application ne démarre pas : {Couper(demarrage.SortieComplete, 1500)}\n";
            }

            texte.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"{Entete}, rejoués après les tests sur cette version, application démarrée (le verdict reste le rejeu de l'usine après une vérification verte) :");
            var bloquants = demande.Evaluation!.Bloquants;
            try
            {
                for (var i = 0; i < bloquants.Count; i++)
                {
                    if (!RejoueParLUsine(bloquants[i]))
                    {
                        continue;
                    }

                    var epreuve = await EprouverScriptQaAsync(demande, bac, bloquants[i].Copie!, null, IssueScript.Passe, ct);
                    var sortie = epreuve.Execution.SortieComplete.Trim();
                    var verdict = epreuve.Issue switch
                    {
                        IssueScript.Passe => "passe.",
                        IssueScript.Reproduit => "reproduit encore.",
                        _ => $"erreur du script ({MotifErreurScript(epreuve.Execution)}).",
                    };
                    texte.AppendLine(CultureInfo.InvariantCulture, $"- Bloquant n°{i + 1} ({ScriptsQaDev}/{Path.GetFileName(bloquants[i].Copie)}) : {verdict}{(epreuve.Issue == IssueScript.Passe || sortie.Length == 0 ? "" : $" Sortie : {Couper(sortie, 600)}")}");
                }
            }
            catch (BacIndisponibleException ex)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- Rejeu interrompu : {ex.Message} Relance lancer_tests_acceptation.");
            }
        }
        finally
        {
            await oracle.ArreterApplicationAsync(bac, CancellationToken.None);
        }

        return texte.ToString();
    }

    private async Task LivraisonAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        await bac.PreparerAsync(ModeMontage.Construction, ct);
        var succes = demande.Verifications.LastOrDefault()?.Succes == true;
        var repertoireLivrable = depot.RepertoireLivrable(demande.Id);
        var repertoireCode = Path.Combine(repertoireLivrable, "code");
        var avertissement = await bac.ExporterAsync(repertoireCode, ct);
        if (avertissement is not null)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, avertissement);
        }

        var empreinte = await EmpreinteCodeAsync(bac, ct);
        demande.LivraisonIdentiqueRefusee = empreinte is not null && demande.EmpreintesCodeRefuse.Contains(empreinte, StringComparer.Ordinal);
        if (demande.LivraisonIdentiqueRefusee)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, "Attention : livraison identique à la version refusée : le code n'a pas changé depuis ce refus, et le commentaire du demandeur n'a pas été appliqué.");
        }

        var cheminRapport = Path.Combine(repertoireLivrable, "RAPPORT.md");
        demande.Livrable = new Livrable { RepertoireCode = repertoireCode, Rapport = cheminRapport, Succes = succes, EmpreinteCode = empreinte };
        await File.WriteAllTextAsync(cheminRapport, GenerateurRapport.Generer(demande, config), ct);
        SupprimerTravailPartiel(demande);

        await SupprimerConteneurAsync(demande, bac, avecVolume: false);
        depot.AjouterEvenement(demande, ChefDeProjet, (succes, Consignes.EchecAvantLesTests(demande.Verifications.LastOrDefault())) switch
        {
            (true, _) when demande.LivraisonIdentiqueRefusee => $"Livraison identique à la version refusée, sans aucun changement depuis le refus : {repertoireCode}",
            (true, _) => $"Livraison prête : {repertoireCode}",
            (false, { } horsService) => $"Livraison en échec, {horsService} : {repertoireCode}",
            _ => $"Livraison en échec partiel (tests non tous verts) : {repertoireCode}",
        });
        if (demande.ModeAuto)
        {
            demande.Etape = EtapeDemande.Terminee;
        }
        else
        {
            demande.Etape = EtapeDemande.ValidationLivraison;
            demande.Statut = StatutDemande.AttenteValidation;
        }
    }

    public void ActualiserRapport(Demande demande)
    {
        var chemin = Path.Combine(depot.RepertoireLivrable(demande.Id), "RAPPORT.md");
        if (!File.Exists(chemin))
        {
            return;
        }

        try
        {
            File.WriteAllText(chemin, GenerateurRapport.Generer(demande, config));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            depot.Journaliser(demande.Id, $"Mise à jour du rapport impossible : {ex.Message}");
        }
    }

    private async Task TerminerAsync(Demande demande, IBacASable bac, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await SupprimerConteneurAsync(demande, bac, avecVolume: true);
        if (demande.CadrageParDemandeur && demande.Livrable is { Succes: false } livrable)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, $"Projet non publié : la livraison est en échec. Le livrable reste disponible : {livrable.RepertoireCode}.");
        }
        else
        {
            await PublierProjetAsync(demande);
        }

        demande.Statut = StatutDemande.Terminee;
        depot.AjouterEvenement(demande, ChefDeProjet, demande.Livrable?.Succes == true ? "Demande terminée avec succès." : "Demande terminée.");
        if (demande.PublicationProjet is not null)
        {
            ActualiserRapport(demande);
        }
    }

    public async Task PublierProjetAsync(Demande demande)
    {
        if (demande.PublicationProjet is not null)
        {
            return;
        }

        (bool Integree, string Message)? publication;
        try
        {
            publication = await Projets.PublierAsync(depot, demande);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            if (!demande.EstProjetExistant)
            {
                demande.Projet = null;
            }

            publication = (false, $"Projet non publié : {ex.Message} Le livrable reste disponible : {demande.Livrable?.RepertoireCode}.");
        }

        if (publication is not { } resultat)
        {
            return;
        }

        depot.AjouterEvenement(demande, ChefDeProjet, resultat.Message);
        demande.PublicationProjet = new PublicationProjet { Integree = resultat.Integree, Date = demande.Evenements[^1].Date, Message = resultat.Message };
        depot.Enregistrer(demande);
    }

    private Task Attendre(Demande demande, StatutDemande statut)
    {
        demande.Statut = statut;
        return Task.CompletedTask;
    }

    private async Task AnnulerAsync(Demande demande, IBacASable bac)
    {
        demande.Statut = StatutDemande.Annulee;
        depot.AjouterEvenement(demande, ChefDeProjet, "Demande annulée.");
        await SupprimerConteneurAsync(demande, bac, avecVolume: true);
        depot.Enregistrer(demande);
    }

    private void SupprimerTravailPartiel(Demande demande)
    {
        try
        {
            BacASableDocker.SupprimerRepertoire(depot.RepertoireTravailPartiel(demande.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            depot.Journaliser(demande.Id, $"Suppression du travail partiel impossible : {ex.Message}");
        }
    }

    private async Task ConserverTravailPartielAsync(Demande demande, IBacASable bac)
    {
        if (demande.Etape < EtapeDemande.Construction)
        {
            return;
        }

        var repertoire = depot.RepertoireTravailPartiel(demande.Id);
        SupprimerTravailPartiel(demande);

        try
        {
            await bac.PreparerAsync(ModeMontage.Construction, CancellationToken.None);
            var avertissement = await bac.ExporterAsync(repertoire, CancellationToken.None);
            depot.AjouterEvenement(demande, ChefDeProjet, $"Travail partiel conservé : {repertoire}{(avertissement is null ? "" : $" ({avertissement})")}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            depot.Journaliser(demande.Id, $"Export du travail partiel impossible : {ex.Message}");
        }

        depot.Enregistrer(demande);
    }

    private async Task SupprimerConteneurAsync(Demande demande, IBacASable bac, bool avecVolume)
    {
        try
        {
            await bac.SupprimerAsync(avecVolume, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            depot.Journaliser(demande.Id, $"Suppression du bac à sable impossible : {ex.Message}");
        }
    }

    private Task<ResultatAgent> ExecuterAgentAsync(
        Demande demande,
        RoleAgent role,
        string prompt,
        string message,
        IReadOnlyList<AIFunction> outils,
        SignalFin fin,
        CancellationToken ct) =>
        ExecuterAgentAsync(demande, role, RolesAgent.Libelle(role), null, prompt, message, outils, fin, ct);

    private async Task<ResultatAgent> ExecuterAgentAsync(
        Demande demande,
        RoleAgent role,
        string libelle,
        int? maxTours,
        string prompt,
        string message,
        IReadOnlyList<AIFunction> outils,
        SignalFin fin,
        CancellationToken ct)
    {
        if (PlafondAtteint(demande))
        {
            throw new PlafondAtteintException($"Plafond de coût de la demande atteint ({config.Limites.PlafondCoutDemande?.ToString("0.00", CultureInfo.InvariantCulture)} $).");
        }

        var references = config.ModelesPour(role);
        var passeDeConstruction = role == RoleAgent.Dev;
        var effort = config.EffortAppliquePour(role, references[0]) is { } valeur ? $", effort de raisonnement : {valeur}" : "";
        depot.AjouterEvenement(demande, libelle, $"Au travail ({references[0]}{(references.Count > 1 ? $", secours : {string.Join(", ", references.Skip(1))}" : "")}{effort}).");
        depot.Enregistrer(demande);
        var conversation = ProchaineConversation(demande, role);
        var definition = new DefinitionAgent
        {
            Role = role,
            Modeles = references,
            PromptSysteme = prompt,
            Message = message,
            Outils = outils,
            OutilsTerminaux = outils.Select(o => o.Name).Where(EstOutilTerminal).ToList(),
            Fin = fin,
            Libelle = libelle,
            MaxIterations = maxTours,
        };
        var contexte = new ContexteAgent
        {
            BudgetRestant = config.Limites.PlafondCoutDemande - demande.CoutTotal,
            AnnulationDemandee = () => depot.AnnulationDemandee(demande.Id),
            Journal = ligne => depot.Journaliser(demande.Id, ligne),
            Signaler = evenement =>
            {
                depot.AjouterEvenement(demande, libelle, evenement);
                depot.Enregistrer(demande);
            },
            Consommer = consommation =>
            {
                AjouterConsommation(demande, libelle, consommation);
                if (passeDeConstruction)
                {
                    demande.ToursPasseConstruction++;
                }

                depot.Enregistrer(demande);
            },
            ToursDejaConsommes = passeDeConstruction ? demande.ToursPasseConstruction : 0,
            CheminTranscription = $"{conversation}.md",
            CheminSorties = $"{conversation}{SuffixeSorties}",
        };
        var resultat = await executeur.ExecuterAsync(definition, contexte, ct);
        depot.AjouterEvenement(demande, libelle, $"{resultat.Iterations} tours, {resultat.TokensEntree + resultat.TokensSortie} tokens, {Consignes.FormaterCout(resultat.Cout, resultat.CoutConnu)} ({resultat.Modele}).");
        return resultat;
    }

    private string ProchaineConversation(Demande demande, RoleAgent role)
    {
        var repertoireConversations = depot.RepertoireConversations(demande.Id);
        Directory.CreateDirectory(repertoireConversations);
        var numero = Directory.EnumerateFiles(repertoireConversations)
            .Select(f => int.TryParse(Path.GetFileName(f).Split('-', 2)[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return Path.Combine(repertoireConversations, $"{numero.ToString("00", CultureInfo.InvariantCulture)}-{role}");
    }

    private async Task<ResultatAgent> ExecuterAvecRelancesAsync(
        Demande demande,
        RoleAgent role,
        string prompt,
        string message,
        IReadOnlyList<AIFunction> outils,
        SignalFin fin,
        Func<ResultatAgent, Task<bool>>? repli,
        CancellationToken ct)
    {
        var libelle = RolesAgent.Libelle(role);
        var messageCourant = message;
        for (var relance = 0; ; relance++)
        {
            var resultat = await ExecuterAgentAsync(demande, role, prompt, messageCourant, outils, fin, ct);
            if (fin.Termine
                || (repli is not null && await repli(resultat))
                || relance >= config.Limites.MaxRelancesRole
                || resultat.BudgetEpuise
                || PlafondAtteint(demande))
            {
                return resultat;
            }

            ExigerNonAnnulee(demande, ct);
            depot.AjouterEvenement(
                demande,
                ChefDeProjet,
                $"{SansResultat(libelle, resultat)} Nouvelle exécution du rôle, relance n°{(relance + 1).ToString(CultureInfo.InvariantCulture)} sur {config.Limites.MaxRelancesRole.ToString(CultureInfo.InvariantCulture)}, avec un contexte neuf.");
            depot.Enregistrer(demande);
            messageCourant = $"{message}\n## Tentative précédente\n\nLa tentative précédente a été arrêtée sans résultat, pour ce motif : {Raison(resultat)}\nTiens-en compte pour ne pas reproduire cet arrêt.\n";
        }
    }

    public async Task<(string? Motif, IReadOnlyList<string> Titres)> ControlerTestsAjoutesAsync(Demande demande, CancellationToken ct)
    {
        var backlog = demande.Backlog ?? throw new InvalidOperationException("Backlog absent.");
        var bac = fabriqueBac.Creer(demande.Id);
        try
        {
            await bac.PreparerAsync(ModeMontage.Exploration, ct);
            return await ControlerTestsAsync(bac, demande, backlog, ct, demande.Tests?.Titres ?? [], demande.Tests?.Fichiers ?? []);
        }
        finally
        {
            await SupprimerConteneurAsync(demande, bac, avecVolume: false);
        }
    }

    public void SignalerTestsHorsBacklog(Demande demande, Backlog backlog, IEnumerable<string> titres)
    {
        var horsBacklog = TestsHorsBacklog(backlog, titres);
        if (horsBacklog.Count > 0)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : {horsBacklog.Count} test(s) ne portent l'identifiant d'aucun critère du backlog : {Couper(string.Join(" ; ", horsBacklog), 1000)}.");
        }
    }

    public static List<string> TitresAjoutes(IEnumerable<string> titres, IEnumerable<string> verrouilles)
    {
        var restants = verrouilles.CountBy(t => t, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);
        var ajoutes = new List<string>();
        foreach (var titre in titres)
        {
            if (restants.GetValueOrDefault(titre) > 0)
            {
                restants[titre]--;
            }
            else
            {
                ajoutes.Add(titre);
            }
        }

        return ajoutes;
    }

    private async Task<(string? Motif, IReadOnlyList<string> Titres)> ControlerTestsAsync(IBacASable bac, Demande demande, Backlog backlog, CancellationToken ct, IReadOnlyCollection<string>? verrouilles = null, IReadOnlyCollection<string>? fichiersVerrouilles = null, ResultatListe? liste = null)
    {
        liste ??= await oracle.ListerTestsAsync(bac, ct);
        if (!liste.Reussi)
        {
            return ($"ils ne se chargent pas.\n{Couper(liste.Sortie, 6000)}", []);
        }

        var manquants = CriteresNonCouverts(backlog, liste.Titres);
        if (manquants.Count > 0)
        {
            return ($"critères sans test (le titre d'un test doit contenir l'identifiant du critère entre crochets, par exemple [{manquants[0]}]) : {string.Join(", ", manquants)}.", []);
        }

        var ecrits = (verrouilles is null ? liste.Titres : TitresAjoutes(liste.Titres, verrouilles)).Count(t => !NonRegression.EstTitre(t));
        if (ecrits > config.Limites.MaxTests)
        {
            return ($"{ecrits} tests{(verrouilles is null ? "" : " ajoutés")}, {config.Limites.MaxTests} au maximum.{(backlog.Notions.Count > 0 ? " Les tests de frontière des notions comptent dans ce maximum : un même test peut couvrir plusieurs critères, son titre porte alors tous leurs identifiants (par exemple « [S1-C1] [S1-C2] … » ou « [F1] [S1-C3] … »)." : "")}", []);
        }

        var intrus = await bac.ExecuterAsync(
            $"find {Acceptation} -mindepth 1 -maxdepth 1 ! -name CONTRAT.md ! -name tests -printf '%f\\n'; find {Acceptation} -type l -printf '%P\\n'",
            TimeSpan.FromSeconds(20),
            ct: ct);
        if (!string.IsNullOrWhiteSpace(intrus.Sortie))
        {
            return ($"{Acceptation} ne doit contenir que CONTRAT.md et le dossier tests/, sans lien symbolique. Playwright est déjà fourni : n'installe rien ici. Supprime :\n{Couper(intrus.Sortie, 1000)}", []);
        }

        var contrat = await bac.ExecuterAsync($"test -s {Acceptation}/CONTRAT.md", TimeSpan.FromSeconds(20), ct: ct);
        if (!contrat.Reussi)
        {
            return (MotifContratAbsent, []);
        }

        var desactives = await bac.ExecuterAsync(
            $"grep -rnE '\\.(skip|fixme)[[:space:]]*\\(' {Acceptation}/tests || true",
            TimeSpan.FromSeconds(20),
            ct: ct);
        var desactivesModifiables = LignesModifiables(demande, desactives.Sortie, fichiersVerrouilles, "un test désactivé (skip ou fixme)");
        if (!string.IsNullOrWhiteSpace(desactivesModifiables))
        {
            return ($"un test désactivé (skip ou fixme) compte comme un échec et ne pourra jamais passer. Retire-le ou rends-le exécutable :\n{Couper(desactivesModifiables, 2000)}", []);
        }

        var enSerie = await bac.ExecuterAsync(
            $"grep -rnE 'describe\\.serial|mode[[:space:]]*:[[:space:]]*[\"'\\''`]serial[\"'\\''`]' {Acceptation}/tests || true",
            TimeSpan.FromSeconds(20),
            ct: ct);
        var enSerieModifiables = LignesModifiables(demande, enSerie.Sortie, fichiersVerrouilles, "le mode serial");
        if (!string.IsNullOrWhiteSpace(enSerieModifiables))
        {
            return ($"les tests doivent être indépendants : un test sauté après un échec n'est jamais vérifié. Retire le mode serial (test.describe.serial, test.describe.configure({{ mode: 'serial' }})) :\n{Couper(enSerieModifiables, 2000)}", []);
        }

        var types = await bac.ExecuterAsync(ControleTypes, TimeSpan.FromMinutes(2), Acceptation, ct: ct);
        var erreurs = ErreursDeTypesBloquantes(types);
        if (erreurs is not null)
        {
            return ($"le contrôle de types TypeScript y trouve des erreurs, qui feraient échouer ces tests même avec une application correcte (nom, module, export, propriété ou méthode qui n'existe pas…). Corrige chaque ligne signalée :\n{Couper(erreurs, 4000)}", []);
        }

        return (null, liste.Titres);
    }

    private string LignesModifiables(Demande demande, string sortie, IReadOnlyCollection<string>? fichiersVerrouilles, string constat)
    {
        var lignes = sortie.Split('\n');
        var figees = lignes.Where(l => EstLigneFigee(l, fichiersVerrouilles)).Select(l => l.TrimEnd('\r')).ToList();
        if (figees.Count > 0)
        {
            SignalerUneFois(demande, $"Attention : des tests repris d'une demande précédente ou déjà verrouillés, que l'auteur des tests ne peut pas modifier, contiennent {constat} ; signalé sans refus :\n{Couper(string.Join('\n', figees), 2000)}");
        }

        return string.Join('\n', lignes.Where(l => !EstLigneFigee(l, fichiersVerrouilles)));
    }

    private void SignalerUneFois(Demande demande, string message)
    {
        if (!demande.Evenements.Exists(e => string.Equals(e.Message, message, StringComparison.Ordinal)))
        {
            depot.AjouterEvenement(demande, ChefDeProjet, message);
        }
    }

    private static bool EstLigneFigee(string ligne, IReadOnlyCollection<string>? fichiersVerrouilles) =>
        ligne.StartsWith($"{Acceptation}/tests/{NonRegression.Dossier}/", StringComparison.Ordinal)
        || (fichiersVerrouilles?.Any(f => ligne.StartsWith($"{Acceptation}/{f}:", StringComparison.Ordinal)) ?? false);

    internal static string? ErreursDeTypesBloquantes(ResultatCommande types)
    {
        if (types.Reussi)
        {
            return null;
        }

        var diagnostics = types.SortieComplete.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => DiagnosticTypeScript().IsMatch(l)).ToList();
        if (diagnostics.Count == 0)
        {
            return types.SortieComplete;
        }

        var bloquants = diagnostics.Where(l => DiagnosticTypeScriptBloquant().IsMatch(l)).ToList();
        return bloquants.Count == 0 ? null : string.Join('\n', bloquants);
    }

    private async Task<(string? Motif, IReadOnlyList<string> Titres)> ControlerTestsPresentsAsync(IBacASable bac, Demande demande, string repertoire, Backlog backlog, CancellationToken ct)
    {
        var contrat = new FileInfo(Path.Combine(repertoire, "CONTRAT.md"));
        if (contrat is not { Exists: true, Length: > 0 })
        {
            return (MotifContratAbsent, []);
        }

        var (motif, titres) = await ControlerTestsAsync(bac, demande, backlog, ct);
        return motif is null && RefusNotionsHorsContrat(demande, backlog) is { } horsContrat ? (horsContrat, []) : (motif, titres);
    }

    private string? LireContrat(Demande demande)
    {
        var chemin = Path.Combine(depot.RepertoireAcceptation(demande.Id), "CONTRAT.md");
        return File.Exists(chemin) ? File.ReadAllText(chemin) : null;
    }

    private static bool TestsPresents(string repertoire)
    {
        var tests = Path.Combine(repertoire, "tests");
        if (!Directory.Exists(tests))
        {
            return false;
        }

        var reserve = Path.Combine(tests, NonRegression.Dossier) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(tests, "*.spec.ts", SearchOption.AllDirectories)
            .Any(f => !f.StartsWith(reserve, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> PdfFabriques(string repertoire, IEnumerable<string> deposes) => PdfPresents(repertoire).Except(deposes).Order(StringComparer.Ordinal).ToList();

    private static string CheminsAcceptation(IEnumerable<string> fichiers) => string.Join(", ", fichiers.Select(f => $"{Acceptation}/{f}"));

    private static HashSet<string> PdfPresents(string repertoire)
    {
        var tests = Path.Combine(repertoire, "tests");
        if (!Directory.Exists(tests))
        {
            return [];
        }

        var reserve = Path.Combine(tests, NonRegression.Dossier) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(tests, "*", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(reserve, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(repertoire, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ConsignePdfFabriques(IReadOnlyList<string> pdfFabriques) =>
        $"ne fabrique pas de PDF toi-même : un PDF écrit à la main est souvent faux ou illisible. Supprime {CheminsAcceptation(pdfFabriques)} et utilise à la place, par leur chemin absolu, les fichiers d'exemple de l'image : {Echantillons}/document.pdf (vrai PDF de 3 pages, avec plusieurs lignes par page et des accents, dont le texte exact est dans {Echantillons}/document-texte-attendu.txt), {Echantillons}/sans-texte.pdf (PDF valide d'une page, sans aucun texte), {Echantillons}/tronque.pdf (PDF tronqué : il commence par %PDF- mais ne se lit pas), {Echantillons}/faux.pdf (porte l'extension .pdf sans être un PDF) et {Echantillons}/pas-un-pdf.txt (fichier texte).";

    private static string PremierePhrase(string texte)
    {
        var ligne = texte.Trim().Split('\n', 2)[0].Trim();
        var fin = ligne.IndexOf(". ", StringComparison.Ordinal);
        return fin < 0 ? ligne : ligne[..(fin + 1)];
    }

    private bool EstPanneAgent(Exception ex, Demande demande, CancellationToken ct) =>
        ex is not PlafondAtteintException && !ct.IsCancellationRequested && !depot.AnnulationDemandee(demande.Id);

    private static bool EstOutilTerminal(string nom) =>
        nom is "poser_questions" or "soumettre_besoin" or "soumettre_backlog" or "soumettre_tests" or "maintenir_tests" or "terminer_construction" or "soumettre_evaluation";

    private static void AjouterConsommation(Demande demande, string role, Consommation consommation, bool avis = false)
    {
        var modele = consommation.Modele.ToString();
        var existant = demande.Couts.FirstOrDefault(c => c.Role == role && c.Modele == modele);
        if (existant is null)
        {
            existant = new CoutRole { Role = role, Modele = modele, CoutConnu = consommation.CoutConnu, TokensCache = 0 };
            demande.Couts.Add(existant);
        }

        existant.TokensEntree += consommation.TokensEntree;
        existant.TokensCache += consommation.TokensCache;
        existant.TokensSortie += consommation.TokensSortie;
        existant.Cout += consommation.Cout;
        existant.Appels++;
        if (avis)
        {
            existant.AppelsAvis++;
        }

        existant.CoutConnu &= consommation.CoutConnu;
    }

    private OutilsAgent OutilsDe(IBacASable bac) => new(bac, config.Limites);

    private AIFunction OutilPoserQuestions(SignalFin fin, Action<List<string>> recevoir) => AIFunctionFactory.Create(
        ([Description("Questions courtes et précises pour le demandeur.")] List<string> questions) =>
        {
            var nettoyees = questions.Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q.Trim()).ToList();
            if (nettoyees.Count == 0)
            {
                return "Erreur : aucune question fournie.";
            }

            if (config.Limites.MaxQuestions is { } maximum && nettoyees.Count > maximum)
            {
                return $"Erreur : {maximum} questions au maximum. Garde les plus importantes.";
            }

            recevoir(nettoyees);
            fin.Terminer("poser_questions");
            return "Questions transmises au demandeur. Ton travail s'arrête ici pour le moment.";
        },
        "poser_questions",
        "Pose des questions au demandeur quand une information manquante change ce que l'utilisateur verra et que tu ne peux pas proposer un choix raisonnable.");

    private AIFunction OutilSoumettreBacklog(Demande demande, SignalFin fin, RappelLacunes rappel, Action<Backlog, List<Lacune>> recevoir) => AIFunctionFactory.Create(
        ([Description("Le backlog complet.")] Backlog backlog) =>
        {
            backlog.Mode = demande.EstProjetExistant ? "existant" : "nouveau";
            var erreurs = ValiderBacklog(backlog, demande);
            var lacunes = LacunesBacklog.Trouver(backlog, demande);
            if (!rappel.Fait && lacunes.Count > 0)
            {
                rappel.Fait = true;
                return "Backlog refusé, corrige puis soumets à nouveau :\n" + string.Join("\n", erreurs.Concat(lacunes.Select(l => l.Message)).Select(e => $"- {e}")) + "\nRappel unique : si un point est sans objet, soumets de nouveau sans le changer.";
            }

            if (erreurs.Count > 0)
            {
                return "Backlog refusé, corrige puis soumets à nouveau :\n" + string.Join("\n", erreurs.Select(e => $"- {e}"));
            }

            foreach (var notion in backlog.Notions)
            {
                (notion.Nom, notion.Phrase) = (notion.Nom.Trim(), SansBlancsMultiples(notion.Phrase));
            }

            recevoir(backlog, lacunes);
            fin.Terminer("soumettre_backlog");
            return "Backlog enregistré.";
        },
        OptionsStrictes("soumettre_backlog", "Soumet le backlog final (stories et critères d'acceptation). Termine ton travail."));

    private static AIFunction OutilSoumettreBesoin(SignalFin fin, Action<Besoin> recevoir) => AIFunctionFactory.Create(
        ([Description("Le besoin du demandeur, tel que tu l'as compris.")] Besoin besoin) =>
        {
            besoin.Titre = besoin.Titre.Trim();
            besoin.Resume = besoin.Resume.Trim();
            var erreurs = new List<string>();
            if (besoin.Titre.Length == 0)
            {
                erreurs.Add("Le titre est vide.");
            }

            if (besoin.Resume.Length == 0)
            {
                erreurs.Add("Le résumé du besoin est vide.");
            }

            if (erreurs.Count > 0)
            {
                return "Besoin refusé, corrige puis soumets à nouveau :\n" + string.Join("\n", erreurs.Select(e => $"- {e}"));
            }

            recevoir(besoin);
            fin.Terminer("soumettre_besoin");
            return "Besoin enregistré. Ton travail s'arrête ici : le demandeur va le relire.";
        },
        OptionsStrictes("soumettre_besoin", "Soumet le besoin tel que tu l'as compris, pour que le demandeur le valide avant toute user story. Termine ton travail."));

    private static AIFunctionFactoryOptions OptionsStrictes(string nom, string description) => new()
    {
        Name = nom,
        Description = description,
        SerializerOptions = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { RespectNullableAnnotations = true },
        JsonSchemaCreateOptions = new AIJsonSchemaCreateOptions
        {
            TransformOptions = new AIJsonSchemaTransformOptions { RequireAllProperties = true },
        },
    };

    private List<string> ValiderBacklog(Backlog backlog, Demande demande)
    {
        var erreurs = new List<string>();
        if (string.IsNullOrWhiteSpace(backlog.Stack.Choix))
        {
            erreurs.Add("La stack (stack.choix) est obligatoire.");
        }

        if (backlog.Stories.Count == 0)
        {
            erreurs.Add("Il faut au moins une story.");
        }

        if (backlog.Stories.Count > config.Limites.MaxStories)
        {
            erreurs.Add($"Trop de stories ({backlog.Stories.Count}) : {config.Limites.MaxStories} au maximum. {ConsigneRegroupement}");
        }

        var totalCriteres = backlog.Stories.Sum(s => s.Criteres.Count);
        if (totalCriteres > config.Limites.MaxCriteres)
        {
            erreurs.Add($"Trop de critères ({totalCriteres}) : {config.Limites.MaxCriteres} au maximum. {ConsigneRegroupement}");
        }

        var identifiants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var story in backlog.Stories)
        {
            if (!IdStory().IsMatch(story.Id))
            {
                erreurs.Add($"Identifiant de story invalide « {story.Id} » (attendu : S1, S2…).");
            }

            if (story.Criteres.Count == 0)
            {
                erreurs.Add($"La story {story.Id} n'a aucun critère d'acceptation.");
            }

            foreach (var critere in story.Criteres)
            {
                if (!IdCritere().IsMatch(critere.Id) || !critere.Id.StartsWith(story.Id + "-", StringComparison.Ordinal))
                {
                    erreurs.Add($"Identifiant de critère invalide « {critere.Id} » dans {story.Id} (attendu : {story.Id}-C1, {story.Id}-C2…).");
                }

                if (!identifiants.Add(critere.Id))
                {
                    erreurs.Add($"Identifiant de critère en double : {critere.Id}.");
                }

                if (string.IsNullOrWhiteSpace(critere.Texte))
                {
                    erreurs.Add($"Le critère {critere.Id} est vide.");
                }
            }
        }

        erreurs.AddRange(ErreursDeSubstance(backlog, demande));
        erreurs.AddRange(ErreursDesNotions(backlog, demande));
        return erreurs;
    }

    private static List<string> ErreursDesNotions(Backlog backlog, Demande demande)
    {
        var erreurs = new List<string>();
        if (backlog.Notions.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(backlog.SansNotions))
            {
                erreurs.Add($"Liste dans notions les notions de la demande qui ont une frontière ou une portée ({MaxNotions} au plus, les plus risquées d'abord), ou dis en une phrase dans sansNotions qu'il n'y en a aucune.");
            }

            return erreurs;
        }

        if (backlog.Notions.Count > MaxNotions)
        {
            erreurs.Add($"Trop de notions ({backlog.Notions.Count}) : {MaxNotions} au plus. Garde les plus risquées.");
        }

        var textes = demande.TextesDuBesoin.Select(SansBlancsMultiples).ToList();
        var identifiants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var notion in backlog.Notions)
        {
            if (!IdNotion().IsMatch(notion.Id))
            {
                erreurs.Add($"Identifiant de notion invalide « {notion.Id} » (attendu : N1, N2, N3).");
            }
            else if (!identifiants.Add(notion.Id))
            {
                erreurs.Add($"Identifiant de notion en double : {notion.Id}.");
            }

            if (string.IsNullOrWhiteSpace(notion.Nom))
            {
                erreurs.Add($"La notion {notion.Id} n'a pas de nom.");
            }

            if (string.IsNullOrWhiteSpace(notion.Phrase))
            {
                erreurs.Add($"La notion {notion.Id} ne cite aucune phrase de la demande.");
            }
            else if (!textes.Exists(t => t.Contains(SansBlancsMultiples(notion.Phrase), StringComparison.OrdinalIgnoreCase)))
            {
                erreurs.Add($"La notion {notion.Id} cite « {notion.Phrase.Trim()} », introuvable dans la demande : recopie telle quelle la phrase de la demande d'où elle vient (seules la casse et les blancs peuvent différer).");
            }
        }

        return erreurs;
    }

    internal static List<string> ErreursDeSubstance(Backlog backlog, Demande demande)
    {
        var erreurs = new List<string>();
        var choix = backlog.Stack.Choix.Trim();
        if (choix.Length > 0 && (choix.Length < LongueurMinStack || !MotDeStack().IsMatch(choix)))
        {
            erreurs.Add($"La stack « {choix} » ne décrit rien : nomme le langage, le cadre web et le type de stockage.");
        }

        if (backlog.Resume.Trim().Length < LongueurMinTexte)
        {
            erreurs.Add("Le résumé est trop court : décris en 2 à 4 phrases ce qui sera livré.");
        }

        foreach (var critere in backlog.Stories.SelectMany(s => s.Criteres))
        {
            var texte = critere.Texte.Trim();
            if (texte.Length > 0 && texte.Length < LongueurMinTexte)
            {
                erreurs.Add($"Le critère {critere.Id} « {texte} » est trop court : écris-le au format Étant donné… Quand… Alors…, observable depuis le navigateur ou par HTTP.");
            }
        }

        var besoin = Mots(string.Join(' ', demande.TextesDuBesoin));
        besoin.ExceptWith(MotsOutils);
        if (besoin.Count >= MotsSignificatifsMin)
        {
            var textes = backlog.Stories.SelectMany(s => s.Criteres.Select(c => c.Texte).Prepend(s.Description).Prepend(s.Titre)).Prepend(backlog.Resume).Prepend(backlog.Titre);
            besoin.IntersectWith(Mots(string.Join(' ', textes)));
            if (besoin.Count < MotsCommunsMin)
            {
                erreurs.Add("Le backlog ne reprend pas les mots du besoin : son titre, son résumé, ses stories et ses critères doivent parler de ce que la demande et les réponses du demandeur décrivent, pas d'un texte d'essai.");
            }
        }

        if (ExigenceNonCouverte(demande, ExigencePersistance(), backlog.Stories.SelectMany(s => s.Criteres).Select(c => c.Texte), CriterePersistance()) is { } exigence)
        {
            erreurs.Add($"Le besoin exige la persistance (« {exigence} ») mais aucun critère ne la vérifie : ajoute un critère observable, par exemple « les données créées sont toujours présentes après un rechargement de la page et via un nouvel appel HTTP ».");
        }

        if (demande.EstProjetExistant && ExigenceNonCouverte(demande, ExigenceNonRegression(), backlog.Stories.SelectMany(s => s.Criteres.Select(c => c.Texte).Prepend(s.Titre)), CritereNonRegression()) is { } nonRegression)
        {
            erreurs.Add($"Le besoin exige que l'existant continue de fonctionner (« {nonRegression} ») mais aucun critère ne le vérifie : ajoute un critère de non-régression observable, par exemple « les routes, codes de réponse, boutons et filtres existants fonctionnent toujours, et les données déjà enregistrées restent lisibles ».");
        }

        return erreurs;
    }

    private static string? ExigenceNonCouverte(Demande demande, Regex exigence, IEnumerable<string> textes, Regex critere) =>
        Phrase().Matches(demande.Texte).Select(m => m.Value.Trim()).FirstOrDefault(p => exigence.IsMatch(SansAccents(p))) is { } phrase
        && !textes.Any(t => critere.IsMatch(SansAccents(t)))
            ? phrase
            : null;

    private static HashSet<string> Mots(string texte) =>
        MotSignificatif().Matches(texte).Select(m => m.Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

    internal static string SansAccents(string texte) =>
        string.Concat(texte.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

    private AIFunction OutilListerTests(IBacASable bac) => AIFunctionFactory.Create(
        async (CancellationToken ct) =>
        {
            var liste = await oracle.ListerTestsAsync(bac, ct);
            return liste.Reussi
                ? $"{liste.Titres.Count} tests chargés :\n" + string.Join("\n", liste.Titres.Select(t => $"- {t}"))
                : $"Erreur au chargement des tests :\n{Couper(liste.Sortie, 6000)}";
        },
        "lister_tests",
        "Charge les tests de /acceptance/tests sans les exécuter et renvoie leurs titres, ou les erreurs de chargement.");

    private AIFunction OutilSoumettreTests(IBacASable bac, Demande demande, Backlog backlog, IReadOnlyList<Demande> precedentes, IReadOnlySet<string> pdfDeposes, SignalFin fin, Action<string, IReadOnlyList<string>, List<TestFrontiere>, ResultatOracle> recevoir) => AIFunctionFactory.Create(
        async (
            [Description("Résumé des tests écrits.")] string resume,
            [Description("Pour chaque test de frontière ([F1], [F2], [F3]) : son identifiant, la phrase de la demande ou du backlog d'où vient la notion qu'il éprouve, recopiée telle quelle, et l'identifiant de cette notion dans le backlog (N1, N2, N3) quand elle y figure. Vide si tu n'as écrit aucun test de frontière.")] List<TestFrontiere>? frontieres = null,
            CancellationToken ct = default) =>
        {
            if (OutilsAgent.EstUnMarqueurDeLUsine(resume))
            {
                return RefusResumeMarqueur;
            }

            if (NonRegression.Restaurer(depot, demande, precedentes))
            {
                return RefusNonRegressionModifiee;
            }

            if (demande.Tests is { } refuses && string.Equals(Empreinte.Calculer(depot.RepertoireAcceptation(demande.Id)), refuses.Empreinte, StringComparison.Ordinal))
            {
                return $"{PrefixeRefusTests}ce sont les tests refusés par le validateur, inchangés. Modifie-les selon ses commentaires, puis soumets à nouveau.";
            }

            var pdfFabriques = PdfFabriques(depot.RepertoireAcceptation(demande.Id), pdfDeposes);
            if (pdfFabriques.Count > 0)
            {
                return PrefixeRefusTests + ConsignePdfFabriques(pdfFabriques);
            }

            var (motif, titres) = await ControlerTestsAsync(bac, demande, backlog, ct);
            if (motif is not null)
            {
                return PrefixeRefusTests + motif;
            }

            var citations = CitationsFrontieres(titres, frontieres ?? []);
            var refus = new[] { RefusCitations(demande, backlog, citations), RefusNotionsNonCouvertes(backlog, citations, frontieres ?? []), RefusExemplesManquants(backlog, citations), RefusVerdictsInverses(demande, backlog, citations), RefusNotionsHorsContrat(demande, backlog), await RefusAttendusNonCalculesAsync(bac, demande, citations.Keys, ct) }.OfType<string>().ToList();
            if (refus.Count > 0)
            {
                return PrefixeRefusTests + string.Join("\n", refus);
            }

            var (applicationVide, execution) = await ExecuterContreApplicationVideAsync(bac, demande, ct);
            if (applicationVide is not null)
            {
                return PrefixeRefusTests + applicationVide;
            }

            if (await RefusVoteDesNotionsAsync(demande, backlog, citations, ct) is { } vote)
            {
                return PrefixeRefusTests + vote;
            }

            SignalerControleImpossible(demande, execution);
            recevoir(resume, titres, [.. citations.Select(c => new TestFrontiere { Test = c.Key, Phrase = SansBlancsMultiples(c.Value!.Phrase), Notion = NotionDuBacklog(backlog, c.Value.Notion) ?? "", Exemple = c.Value.Exemple?.Trim() ?? "", Inclus = c.Value.Inclus })], execution);
            fin.Terminer("soumettre_tests");
            return "Tests enregistrés.";
        },
        "soumettre_tests",
        $"Soumet les tests d'acceptation. La soumission est définitive : des tests acceptés sont verrouillés et le Dev travaille sur eux ; ne soumets que des tests réels et complets. Voici tous ses contrôles, il n'y en a pas d'autres : {ControlesCommunsDesTests()} ; ce ne sont pas les tests refusés par le validateur, inchangés ; chaque test de frontière ([F1]…) cite dans frontieres une phrase de la demande ou du backlog, recopiée telle quelle ; chaque notion du backlog (N1…) a son test de frontière, qui la nomme dans frontieres ; le test de frontière d'une notion du backlog donne dans frontieres un exemple discriminant et ton verdict pour cet exemple (inclus) ; un verdict inverse de celui d'une soumission précédente sur le même exemple suppose que le test de frontière ou une ligne de CONTRAT.md qui nomme la notion a changé depuis cette soumission ; {Acceptation}/CONTRAT.md nomme chaque notion par son identifiant (pour une réévaluation, dans une ligne nouvelle que tu as écrite), et sa règle y est attendue mais n'est pas contrôlée ; un test de frontière qui tire un attendu d'un fichier d'exemple à texte de référence suppose une commande executer_commande réussie qui nomme ce texte de référence ; contre une application vide (page blanche, réponses 200 sans contenu), chaque test hors non-régression échoue, sans être sauté ; enfin, l'exemple de chaque notion est jugé par {AvisParNotion.ToString(CultureInfo.InvariantCulture)} avis indépendants qui ne lisent que la demande, la phrase citée et l'exemple : une majorité contraire à ton verdict refuse la soumission. Termine ton travail.");

    private string ControlesCommunsDesTests() =>
        $"le résumé est réel, pas un marqueur de l'usine ; {Acceptation}/tests/{NonRegression.Dossier} est inchangé ; aucun PDF fabriqué par toi dans les tests (utilise {Echantillons}) ; les tests se chargent ; chaque critère du backlog est couvert par un test dont le titre porte son identifiant entre crochets ([S1-C1]) ; {config.Limites.MaxTests.ToString(CultureInfo.InvariantCulture)} tests au plus, tests de frontière compris, non-régression exclue ; {Acceptation} ne contient que CONTRAT.md, non vide, et le dossier tests/, sans lien symbolique ; aucun .skip( ni .fixme( (test, describe…) ; pas de mode serial ; aucune erreur du contrôle de types TypeScript";

    private async Task<(string? Motif, ResultatOracle Execution)> ExecuterContreApplicationVideAsync(IBacASable bac, Demande demande, CancellationToken ct, IReadOnlySet<string>? figes = null)
    {
        await bac.RedemarrerAsync(ct);
        var execution = await oracle.VerifierAsync(bac, depot.RepertoireAcceptation(demande.Id), new OptionsVerification(false, false, null, ApplicationVide: true), ct);
        if (execution is { Total: 0, Echecs.Count: > 0 } && execution.Echecs.TrueForAll(e => e.Titre == LecteurRapportPlaywright.TitreErreurGlobale))
        {
            return ($"ils ne se chargent pas contre une application vide (page blanche, réponses 200 sans contenu), alors que lister_tests les charge : un fichier de test n'appelle l'application que dans ses tests, jamais à son chargement.\n{Couper(string.Join("\n", execution.Echecs.Select(e => e.Erreur)), 6000)}", execution);
        }

        if (execution.Total == 0)
        {
            return (null, execution);
        }

        var verts = execution.TitresReussis.Distinct(StringComparer.Ordinal).ToList();
        var sautes = execution.TitresSautes.Distinct(StringComparer.Ordinal).ToList();
        if (figes is not null)
        {
            SignalerTestsFiges(demande, verts.Where(figes.Contains), "passent contre une application vide (page blanche, réponses 200 sans contenu)");
            SignalerTestsFiges(demande, sautes.Where(figes.Contains), "ont été sautés contre l'application vide");
            verts.RemoveAll(figes.Contains);
            sautes.RemoveAll(figes.Contains);
        }

        var refus = new List<string>();
        if (verts.Count > 0)
        {
            refus.Add($"ces tests passent contre une application vide (page blanche, réponses 200 sans contenu) : chacun doit affirmer quelque chose que seule l'application demandée produit.{string.Concat(verts.Select(t => $"\n- {t}"))}");
        }

        if (sautes.Count > 0)
        {
            var erreursGlobales = execution.Echecs.Where(e => e.Titre == LecteurRapportPlaywright.TitreErreurGlobale).Select(e => e.Erreur).ToList();
            refus.Add($"ces tests ont été sautés contre l'application vide : rends-les indépendants, un test sauté (après l'échec d'un beforeAll ou d'un test du même groupe) n'est jamais vérifié.{string.Concat(sautes.Select(t => $"\n- {t}"))}{(erreursGlobales.Count == 0 ? "" : $"\nErreurs de l'exécution hors des tests (un délai global dépassé interrompt les tests restants) :\n{Couper(string.Join("\n", erreursGlobales), 6000)}")}");
        }

        return (refus.Count == 0 ? null : string.Join("\n", refus), execution);
    }

    private void SignalerControleImpossible(Demande demande, ResultatOracle? execution)
    {
        if (execution is { Total: 0 })
        {
            var raison = execution.Journal is { Length: > 0 } journal ? journal : execution.Echecs.FirstOrDefault()?.Erreur ?? $"échec à l'étape « {execution.EtapeEchec} »";
            depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : contrôle contre l'application vide impossible : {Couper(raison.Trim().ReplaceLineEndings(" "), 300)} ; tests acceptés sans ce contrôle.");
        }
    }

    private void SignalerTestsFiges(Demande demande, IEnumerable<string> titres, string constat)
    {
        var liste = titres.ToList();
        if (liste.Count > 0)
        {
            SignalerUneFois(demande, $"Attention : des tests verrouillés, dans des fichiers de tests que la réécriture n'a pas modifiés, {constat} ; signalé sans refus :{string.Concat(liste.Select(t => $"\n- {t}"))}");
        }
    }

    private string? RefusNotionsHorsContrat(Demande demande, Backlog backlog)
    {
        if (backlog.Notions.Count == 0)
        {
            return null;
        }

        var chemin = Path.Combine(depot.RepertoireAcceptation(demande.Id), "CONTRAT.md");
        var lignesDOrigine = LignesDuContratDOrigine(demande);
        var nommees = File.Exists(chemin) ? File.ReadAllLines(chemin).Where(l => !lignesDOrigine.Contains(l.Trim())).SelectMany(l => NotionNommee().Matches(l).Select(m => m.Value)).ToHashSet(StringComparer.Ordinal) : [];
        var absentes = backlog.Notions.Where(n => !nommees.Contains(n.Id)).Select(n => $"- {Consignes.DecrireNotion(n)}").ToList();
        return absentes.Count == 0
            ? null
            : $"{Acceptation}/CONTRAT.md doit nommer chaque notion du backlog par son identifiant, avec sa règle. Notions absentes {(lignesDOrigine.Count > 0 ? "des lignes que tu as écrites (les lignes du contrat d'origine ne comptent pas : écris pour chacune une ligne nouvelle, même si sa règle n'a pas changé)" : "du contrat")} :\n{string.Join("\n", absentes)}";
    }

    private HashSet<string> LignesDuContratDOrigine(Demande demande)
    {
        var contratDOrigine = demande.NouveauxTests && demande.ReevaluationDe is { } source ? Path.Combine(depot.RepertoireAcceptation(source), "CONTRAT.md") : null;
        return File.Exists(contratDOrigine) ? File.ReadAllLines(contratDOrigine).Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal) : [];
    }

    private void MarquerReglesDOrigineRemplacees(Demande demande, Backlog backlog)
    {
        var chemin = Path.Combine(depot.RepertoireAcceptation(demande.Id), "CONTRAT.md");
        var lignesDOrigine = LignesDuContratDOrigine(demande);
        if (lignesDOrigine.Count == 0 || !File.Exists(chemin))
        {
            return;
        }

        var notions = backlog.Notions.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var lignes = File.ReadAllText(chemin).Split('\n');
        var marquees = 0;
        for (var i = 0; i < lignes.Length; i++)
        {
            var ligne = lignes[i].Trim();
            if (lignesDOrigine.Contains(ligne) && !ligne.Contains(MarqueRegleRemplacee, StringComparison.Ordinal) && NotionNommee().Matches(ligne).Any(m => notions.Contains(m.Value)))
            {
                lignes[i] = DebutDeLigneDuContrat().Replace(lignes[i], debut => $"{debut.Value}{MarqueRegleRemplacee} ", 1);
                marquees++;
            }
        }

        if (marquees > 0)
        {
            File.WriteAllText(chemin, string.Join('\n', lignes));
            depot.Journaliser(demande.Id, $"{marquees.ToString(CultureInfo.InvariantCulture)} ligne(s) du contrat d'origine qui nomment une notion du backlog, remplacées par les lignes nouvelles du QA amont, marquées « {MarqueRegleRemplacee} » dans {Acceptation}/CONTRAT.md.");
        }
    }

    private static Dictionary<string, TestFrontiere?> CitationsFrontieres(IEnumerable<string> titres, List<TestFrontiere> frontieres) =>
        titres.Select(IdentifiantFrontiere).OfType<string>().Distinct(StringComparer.Ordinal).ToDictionary(
            identifiant => identifiant,
            identifiant => frontieres.Find(f => f is not null && string.Equals(f.Test?.Trim().Trim('[', ']').Trim(), identifiant, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(f.Phrase)),
            StringComparer.Ordinal);

    private static string? NotionDuBacklog(Backlog backlog, string? notion) =>
        backlog.Notions.Find(n => string.Equals(n.Id, notion?.Trim().Trim('[', ']').Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    private static string? RefusNotionsNonCouvertes(Backlog backlog, Dictionary<string, TestFrontiere?> citations, List<TestFrontiere> frontieres)
    {
        var couvertes = citations.Values.Select(f => NotionDuBacklog(backlog, f?.Notion)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var manquantes = backlog.Notions.Where(n => !couvertes.Contains(n.Id)).Select(n => $"- {Consignes.DecrireNotion(n)}").ToList();
        var sansTest = frontieres.Select(f => f?.Test?.Trim().Trim('[', ']').Trim()).OfType<string>().Where(t => t.Length > 0 && !citations.Keys.Contains(t, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Select(t => $"[{t}]").ToList();
        return manquantes.Count == 0
            ? null
            : $"chaque notion du backlog doit avoir son test de frontière, qui la nomme dans le champ notion de frontieres ; tranche d'après la phrase citée et écris sa règle dans CONTRAT.md. Notions sans test :\n{string.Join("\n", manquantes)}{(sansTest.Count == 0 ? "" : $"\nNommés dans frontieres sans test reconnu : {string.Join(", ", sansTest)}. L'identifiant d'un test de frontière doit commencer le titre du test ou d'un de ses niveaux, après d'éventuels identifiants de critère (« [F1] … » ou « [S1-C1] [F1] … »).")}";
    }

    private static string? RefusExemplesManquants(Backlog backlog, Dictionary<string, TestFrontiere?> citations)
    {
        var incomplets = citations
            .Select(c => (Test: c.Key, Frontiere: c.Value, Notion: backlog.Notions.Find(n => n.Id == NotionDuBacklog(backlog, c.Value?.Notion))))
            .Where(c => c.Notion is not null && (string.IsNullOrWhiteSpace(c.Frontiere!.Exemple) || c.Frontiere.Inclus is null))
            .Select(c => $"- [{c.Test}] : {Consignes.DecrireNotion(c.Notion!)}")
            .ToList();
        return incomplets.Count == 0
            ? null
            : $"le test de frontière d'une notion du backlog porte dans frontieres un exemple discriminant (un cas concret court où les lectures possibles de la notion divergent, par exemple une valeur à la limite ou une partie contre le tout) et, dans inclus, ton verdict pour cet exemple selon la règle de CONTRAT.md (true s'il fait partie de la notion, false sinon). Exemple ou verdict absent :\n{string.Join("\n", incomplets)}";
    }

    private async Task<string?> RefusVoteDesNotionsAsync(Demande demande, Backlog backlog, Dictionary<string, TestFrontiere?> citations, CancellationToken ct)
    {
        var refus = new List<string>();
        string? interruption = null;
        foreach (var notion in backlog.Notions)
        {
            if (citations.FirstOrDefault(c => NotionDuBacklog(backlog, c.Value?.Notion) == notion.Id) is not { Value: { Inclus: { } verdict } frontiere } citation)
            {
                continue;
            }

            var phrase = SansBlancsMultiples(frontiere.Phrase);
            var exemple = SansBlancsMultiples(frontiere.Exemple);
            var precedent = demande.VotesNotions.Find(v => v.NomNotion == notion.Nom && v.Phrase == phrase && v.Exemple == exemple);
            var avis = new List<bool>();
            var pannes = new List<string>();
            for (var numero = 1; precedent is null && numero <= AvisParNotion; numero++)
            {
                if (interruption is not null)
                {
                    pannes.Add(interruption);
                    continue;
                }

                var (reponse, panne, erreurDAppel) = await DemanderAvisAsync(demande, notion, frontiere, numero, ct);
                if (reponse is { } obtenu)
                {
                    avis.Add(obtenu);
                }
                else
                {
                    pannes.Add(panne ?? "");
                    if (erreurDAppel)
                    {
                        interruption = $"avis non demandé après l'erreur d'appel d'un avis précédent : {panne}";
                    }
                }
            }

            var obtenus = precedent is null ? avis.Count : AvisParNotion;
            var contraires = precedent is null ? avis.Count(a => a != verdict) : precedent.Inclus == verdict ? precedent.Contraires : AvisParNotion - precedent.Contraires;
            var decompte = $"avis contraires : {contraires.ToString(CultureInfo.InvariantCulture)} sur {obtenus.ToString(CultureInfo.InvariantCulture)}{(pannes.Count > 0 ? $" obtenu(s), {pannes.Count.ToString(CultureInfo.InvariantCulture)} en panne" : "")}";
            depot.Journaliser(demande.Id, $"Vote sur l'exemple de {Consignes.DecrireNotion(notion)} : « {exemple} », {Verdict(verdict)} selon le QA amont ; {decompte}{(precedent is null ? "" : " (vote repris d'une soumission précédente : phrase et exemple inchangés)")}.");
            var empreinte = EmpreinteRegleEtTest(demande, citation.Key, notion.Id);
            if (demande.VotesNotions.Find(v => v.NomNotion == notion.Nom && v.Phrase == phrase && v.Exemple == exemple && v.Inclus == verdict) is { } memeVerdict)
            {
                memeVerdict.EmpreinteRegleEtTest = empreinte;
                depot.Enregistrer(demande);
            }
            else if (pannes.Count == 0)
            {
                demande.VotesNotions.Add(new VoteNotion { NomNotion = notion.Nom, Phrase = phrase, Exemple = exemple, Inclus = verdict, Contraires = contraires, EmpreinteRegleEtTest = empreinte });
                depot.Enregistrer(demande);
            }

            if (obtenus == 0)
            {
                depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : vote sur l'exemple de la notion {notion.Id} « {notion.Nom} » impossible, aucun avis obtenu ({Couper(pannes[0].ReplaceLineEndings(" "), 300)}) ; la notion passe sans ce contrôle.");
            }
            else if (contraires * 2 > AvisParNotion)
            {
                refus.Add($"- [{citation.Key}] {Consignes.DecrireNotion(notion)} : pour l'exemple « {exemple} », ton verdict est « {Verdict(verdict)} » ; {decompte}.");
            }
        }

        return refus.Count == 0
            ? null
            : $"des avis indépendants, qui n'ont lu que la demande, la phrase citée et l'exemple, contredisent en majorité ton verdict sur l'exemple discriminant d'une notion. Revois la règle de CONTRAT.md et le test de frontière d'après la phrase citée ; si tu penses que l'exemple était mal choisi, propose un autre exemple, le cas seul, sans ton verdict ni ta règle, que les avis jugeront tel quel :\n{string.Join("\n", refus)}";
    }

    private string? RefusVerdictsInverses(Demande demande, Backlog backlog, Dictionary<string, TestFrontiere?> citations)
    {
        var inverses = new List<string>();
        foreach (var notion in backlog.Notions)
        {
            if (citations.FirstOrDefault(c => NotionDuBacklog(backlog, c.Value?.Notion) == notion.Id) is { Value: { Inclus: { } verdict } frontiere } citation
                && demande.VotesNotions.Find(v => v.NomNotion == notion.Nom && v.Phrase == SansBlancsMultiples(frontiere.Phrase) && v.Exemple == SansBlancsMultiples(frontiere.Exemple) && v.Inclus != verdict) is { EmpreinteRegleEtTest: { Length: > 0 } empreinte }
                && empreinte == EmpreinteRegleEtTest(demande, citation.Key, notion.Id))
            {
                inverses.Add($"- [{citation.Key}] {Consignes.DecrireNotion(notion)} : pour l'exemple « {SansBlancsMultiples(frontiere.Exemple)} », ton verdict est maintenant « {Verdict(verdict)} ».");
            }
        }

        return inverses.Count == 0
            ? null
            : $"tu as inversé ton verdict sans changer le test ni la règle : pour le même exemple, une soumission précédente donnait le verdict contraire, et depuis, ni le test de frontière ni aucune ligne de CONTRAT.md qui nomme la notion n'a changé. Mets la règle de CONTRAT.md et le test de frontière d'accord avec ton verdict :\n{string.Join("\n", inverses)}";
    }

    private string EmpreinteRegleEtTest(Demande demande, string frontiere, string notion)
    {
        var repertoire = depot.RepertoireAcceptation(demande.Id);
        var contrat = Path.Combine(repertoire, "CONTRAT.md");
        var regles = File.Exists(contrat) ? File.ReadAllLines(contrat).Where(l => NotionNommee().Matches(l).Any(m => m.Value == notion)) : [];
        var texte = string.Join('\n', BlocsDesFrontieres(repertoire, [frontiere]).Select(b => b.Source).Order(StringComparer.Ordinal).Concat(regles));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texte))).ToLowerInvariant();
    }

    private static string Verdict(bool inclus) => inclus ? "inclus dans la notion" : "hors de la notion";

    private async Task<(bool? Avis, string? Panne, bool ErreurDAppel)> DemanderAvisAsync(Demande demande, Notion notion, TestFrontiere frontiere, int numero, CancellationToken ct)
    {
        if (PlafondAtteint(demande))
        {
            return (null, "plafond de coût atteint", false);
        }

        bool? avis = null;
        var fin = new SignalFin();
        var libelle = RolesAgent.Libelle(RoleAgent.QaAmont);
        var definition = new DefinitionAgent
        {
            Role = RoleAgent.QaAmont,
            Modeles = [config.ModelePour(RoleAgent.QaAmont)],
            PromptSysteme = Consignes.PromptAvisNotion,
            Message = Consignes.MessageAvisNotion(demande.Texte, notion.Nom, SansBlancsMultiples(frontiere.Phrase), SansBlancsMultiples(frontiere.Exemple)),
            Outils = [OutilDonnerAvis(fin, reponse => avis = reponse)],
            OutilsTerminaux = [OutilAvis],
            Fin = fin,
            Libelle = $"{libelle} (avis {numero.ToString(CultureInfo.InvariantCulture)}/{AvisParNotion.ToString(CultureInfo.InvariantCulture)} sur {notion.Id})",
            MaxIterations = MaxToursAvis,
        };
        var contexte = new ContexteAgent
        {
            BudgetRestant = config.Limites.PlafondCoutDemande - demande.CoutTotal,
            AnnulationDemandee = () => depot.AnnulationDemandee(demande.Id),
            Journal = ligne => depot.Journaliser(demande.Id, ligne),
            Consommer = consommation =>
            {
                AjouterConsommation(demande, libelle, consommation, avis: true);
                depot.Enregistrer(demande);
            },
        };
        try
        {
            var resultat = await executeur.ExecuterAsync(definition, contexte, ct);
            return avis is null ? (null, Raison(resultat), false) : (avis, null, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.Message, true);
        }
    }

    private static AIFunction OutilDonnerAvis(SignalFin fin, Action<bool> recevoir) => AIFunctionFactory.Create(
        ([Description("true si l'exemple fait partie de la notion au sens de la phrase citée, false sinon.")] bool fait_partie) =>
        {
            recevoir(fait_partie);
            fin.Terminer(OutilAvis);
            return "Avis enregistré.";
        },
        OutilAvis,
        "Donne ton avis : l'exemple fait-il partie de la notion au sens de la phrase citée ? Termine ton travail.");

    private async Task<string?> RefusAttendusNonCalculesAsync(IBacASable bac, Demande demande, IReadOnlyCollection<string> identifiants, CancellationToken ct)
    {
        var blocs = BlocsDesFrontieres(depot.RepertoireAcceptation(demande.Id), identifiants);
        if (blocs.Count == 0)
        {
            return null;
        }

        var fichiers = (await bac.ExecuterAsync($"ls -1 {Echantillons}", TimeSpan.FromSeconds(20), ct: ct)).Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var exemples = fichiers
            .Where(f => f.EndsWith(SuffixeTexteDeReference, StringComparison.Ordinal))
            .SelectMany(reference => fichiers.Where(f => f.StartsWith($"{reference[..^SuffixeTexteDeReference.Length]}.", StringComparison.Ordinal)).Select(exemple => (Exemple: exemple, Reference: reference)))
            .ToList();
        var exigees = blocs.SelectMany(b => exemples.Where(e => ContientEssai(b.Source, e.Exemple)).Select(e => (b.Identifiant, e.Exemple, e.Reference))).Distinct().ToList();
        if (exigees.Count == 0)
        {
            return null;
        }

        var conversations = depot.RepertoireConversations(demande.Id);
        var trace = TraceDesTours(Directory.Exists(conversations) ? Directory.EnumerateFiles(conversations, $"*-{RoleAgent.QaAmont}{SuffixeSorties}") : [], tours => tours.SelectMany(LignesDeCommandeReussie));
        var nonCalcules = exigees.Where(e => !ContientEssai(trace, e.Reference.ToLowerInvariant())).Select(e => $"- [{e.Identifiant}] utilise {Echantillons}/{e.Exemple}, mais aucune de tes commandes executer_commande terminées avec le code de sortie 0 ne nomme son texte de référence {Echantillons}/{e.Reference}.").ToList();
        return nonCalcules.Count == 0
            ? null
            : $"calcule l'attendu sur le texte de référence : un nombre attendu (occurrences, lignes, pages…) tiré d'un fichier d'exemple se calcule par une commande executer_commande qui lit son texte de référence et applique la règle de la notion, jamais à la main ; le lire avec lire_fichier ou rechercher ne suffit pas. Corrige ensuite l'attendu du test si besoin.\n{string.Join("\n", nonCalcules)}";
    }

    private static List<(string Identifiant, string Source)> BlocsDesFrontieres(string repertoireAcceptation, IReadOnlyCollection<string> identifiants)
    {
        var dossier = Path.Combine(repertoireAcceptation, "tests");
        var blocs = new List<(string Identifiant, string Source)>();
        if (identifiants.Count == 0 || !Directory.Exists(dossier))
        {
            return blocs;
        }

        foreach (var fichier in Directory.EnumerateFiles(dossier, "*.ts", SearchOption.AllDirectories))
        {
            string source;
            try
            {
                source = File.ReadAllText(fichier);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var appels = AppelDeTest().Matches(source);
            for (var i = 0; i < appels.Count; i++)
            {
                if (appels[i].Groups["frontiere"] is { Success: true } frontiere && identifiants.Contains(frontiere.Value))
                {
                    var groupe = appels[i].Groups["groupe"].Success;
                    var fin = appels.Skip(i + 1).FirstOrDefault(a => !groupe || a.Groups["groupe"].Success)?.Index ?? source.Length;
                    blocs.Add((frontiere.Value, source[..appels[0].Index] + source[appels[i].Index..fin]));
                }
            }
        }

        return blocs;
    }

    private static string? RefusCitations(Demande demande, Backlog backlog, Dictionary<string, TestFrontiere?> citations)
    {
        var textes = new[] { demande.Texte, backlog.Titre, backlog.Resume, backlog.Stack.Choix, backlog.Stack.Justification }
            .Concat(backlog.Stories.SelectMany(s => s.Criteres.Select(c => c.Texte).Concat([s.Titre, s.Description])))
            .Concat(backlog.HorsPerimetre)
            .Concat(backlog.Hypotheses)
            .Concat(backlog.Notions.Select(n => n.Phrase))
            .Select(SansBlancsMultiples)
            .ToList();
        var refus = citations
            .Where(c => c.Value is null || !textes.Exists(t => t.Contains(SansBlancsMultiples(c.Value.Phrase), StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Value is null ? $"- [{c.Key}] : aucune citation." : $"- [{c.Key}] : « {c.Value.Phrase.Trim()} » est introuvable dans la demande et le backlog.")
            .ToList();
        return refus.Count == 0
            ? null
            : $"chaque test de frontière cite dans frontieres la phrase de la demande ou du backlog d'où vient la notion qu'il éprouve, recopiée telle quelle (seules la casse et les blancs peuvent différer) :\n{string.Join("\n", refus)}";
    }

    private static string? FrontieresSansCitation(IReadOnlyList<string> titres, Backlog backlog) =>
        titres.Select(IdentifiantFrontiere).OfType<string>().Distinct(StringComparer.Ordinal).ToList() is { Count: > 0 } identifiants
            ? $"test(s) de frontière sans citation vérifiée : {string.Join(", ", identifiants.Select(i => $"[{i}]"))}. Soumets-les avec soumettre_tests en citant dans frontieres, pour chacun, la phrase de la demande ou du backlog d'où vient sa notion."
            : backlog.Notions.Count > 0
                ? $"notion(s) du backlog sans test de frontière vérifié : {string.Join(", ", backlog.Notions.Select(n => n.Id))}. Écris pour chacune son test de frontière, puis soumets-les avec soumettre_tests en nommant dans frontieres la notion de chacun."
                : null;

    internal static string SansBlancsMultiples(string texte) => Blancs().Replace(texte, " ").Trim();

    public static string? IdentifiantFrontiere(string titre) => IdFrontiereTitre().Match(titre) is { Success: true } identifiant ? identifiant.Groups[1].Value : null;

    public static string AvecCitation(TestsAcceptation tests, string titre) =>
        tests.Frontieres.Find(f => f.Test == IdentifiantFrontiere(titre)) is { } citation ? $"{titre} (d'après « {citation.Phrase} »)" : titre;

    public static IReadOnlyList<string> VotesRetenus(Demande demande)
    {
        if (demande.Tests is not { } tests || demande.Backlog is not { } backlog)
        {
            return [];
        }

        return [.. tests.Frontieres
            .Select(f => (Frontiere: f, Notion: backlog.Notions.Find(n => n.Id == f.Notion)))
            .Where(c => c.Notion is not null && c.Frontiere.Inclus is not null)
            .Select(c =>
            {
                var inclus = c.Frontiere.Inclus!.Value;
                var phrase = SansBlancsMultiples(c.Frontiere.Phrase);
                var exemple = SansBlancsMultiples(c.Frontiere.Exemple);
                var vote = demande.VotesNotions.Find(v => v.NomNotion == c.Notion!.Nom && v.Phrase == phrase && v.Exemple == exemple);
                var decompte = vote is null
                    ? "décompte non enregistré (vote impossible ou avis en panne)"
                    : $"avis contraires : {(vote.Inclus == inclus ? vote.Contraires : AvisParNotion - vote.Contraires).ToString(CultureInfo.InvariantCulture)} sur {AvisParNotion.ToString(CultureInfo.InvariantCulture)}";
                return $"{c.Notion!.Id} « {c.Notion.Nom} » : exemple « {exemple} », {Verdict(inclus)} (verdict retenu) ; {decompte}.";
            })];
    }

    public static string VerificationDuLivrableDOrigine(ResultatOracle verification)
    {
        var echecs = verification.Echecs.Select(e => e.Titre).Distinct(StringComparer.Ordinal).ToList();
        var etape = verification.EtapeEchec is { } etapeEchec and not "tests" ? $" (échec à l'étape « {etapeEchec} »)" : "";
        return $"Vérification du livrable d'origine avec les nouveaux tests : {verification.Reussis}/{verification.Total} tests réussis{etape}{(echecs.Count > 0 ? $" ; en échec : {string.Join(" ; ", echecs)}" : "")}.";
    }

    private AIFunction OutilLancerTests(Demande demande, IBacASable bac, string repertoireAcceptation, string empreinte, SuiviTests suivi, Action<string> journaliser, bool sondes) => AIFunctionFactory.Create(
        async (CancellationToken ct) =>
        {
            var conteneurNeuf = suivi.AJour;
            if (conteneurNeuf)
            {
                await bac.SupprimerAsync(avecVolume: false, ct);
                await bac.PreparerAsync(ModeMontage.Construction, ct);
                await DeposerScriptsQaAsync(demande, bac, ct);
            }

            var resultat = await oracle.VerifierAsync(bac, repertoireAcceptation, new OptionsVerification(true, true, empreinte, Essai: true, journaliser, sondes, [.. demande.TestsInstables], demande.ModeAuto, ExigePersistance(demande)), ct);
            await ClasserInstablesAsync(demande, bac, resultat, essai: true, cumuler: conteneurNeuf && suivi.DernierEssaiConteneurNeuf, ct);
            suivi.AJour = true;
            suivi.DernierEssaiConteneurNeuf = conteneurNeuf;
            suivi.DernierResultat = resultat;
            var retour = Consignes.FormaterRetourDev(resultat, demande);
            return (demande.NaturePasse == NaturePasse.BloquantsQa || demande.BloquantsQaDansLaPasse) && BloquantsARejouer(demande) ? retour + await RejouerScriptsPourLeDevAsync(demande, bac, resultat, ct) : retour;
        },
        "lancer_tests_acceptation",
        "Construit l'application (usine-build.sh), la démarre (usine-start.sh) et exécute les tests d'acceptation, puis, pendant la correction de bloquants du QA, rejoue leurs scripts de reproduction. Renvoie le résultat détaillé.");

    private static AIFunction OutilTerminerConstruction(IBacASable bac, SignalFin fin, Action<string> recevoir, SuiviTests suivi) => AIFunctionFactory.Create(
        async ([Description("Résumé honnête de ce qui a été fait et de l'état des tests.")] string resume, CancellationToken ct) =>
        {
            if (OutilsAgent.EstUnMarqueurDeLUsine(resume))
            {
                return RefusResumeMarqueur;
            }

            if (!suivi.AJour)
            {
                return "Impossible de terminer : lance d'abord lancer_tests_acceptation. Il doit avoir été exécuté dans cette session de travail, après ta dernière écriture ou commande.";
            }

            if (suivi.DernierResultat is { Succes: false } dernier && !suivi.RefusApresRougeDonne)
            {
                suivi.RefusApresRougeDonne = true;
                return $"Refusé : ton dernier lancer_tests_acceptation donnait {dernier.Reussis}/{dernier.Total} (étape « {dernier.EtapeEchec} ») et tu n'as rien changé depuis. Corrige puis relance les tests ; si tu t'arrêtes quand même, rappelle terminer_construction en expliquant dans le résumé ce qui bloque.";
            }

            var verification = await bac.ExecuterAsync("test -f usine-build.sh && test -f usine-start.sh", TimeSpan.FromSeconds(20), ct: ct);
            if (!verification.Reussi)
            {
                return "Impossible de terminer : usine-build.sh et usine-start.sh doivent exister à la racine de /workspace.";
            }

            recevoir(resume);
            fin.Terminer("terminer_construction");
            return "Construction terminée. L'oracle va vérifier.";
        },
        "terminer_construction",
        "Termine la construction. À appeler quand les tests d'acceptation passent, ou si tu es bloqué.");

    private static AIFunction OutilContesterTests(Demande demande, SignalFin fin, SuiviTests suivi) => AIFunctionFactory.Create(
        ([Description("Titres exacts des tests contestés, tels que lancer_tests_acceptation les affiche.")] List<string> titres,
         [Description("Pourquoi ces tests sont faux : ce qu'ils exigent, et ce que disent CONTRAT.md, le backlog ou la demande.")] string motif,
         [Description("Preuve : la ligne du test et celle de CONTRAT.md, du backlog ou de la demande qu'elle contredit, ou le constat de l'oracle.")] string preuve) =>
        {
            if (demande.ContestationTests is { } deja)
            {
                return deja.ParLUsine
                    ? "Refusé : l'usine a déjà soumis des tests au QA amont dans cette demande, et une seule contestation est permise."
                    : "Refusé : tu as déjà contesté des tests dans cette demande, et une seule contestation est permise.";
            }

            if (!suivi.AJour || suivi.DernierResultat is not { Succes: false, EtapeEchec: "tests" } dernier || demande.Tests is not { } tests)
            {
                return "Refusé : lance d'abord lancer_tests_acceptation, après ta dernière écriture ou commande. Seuls les tests rouges de ce dernier essai, avec une application construite et démarrée, peuvent être contestés.";
            }

            var rouges = dernier.Echecs.Select(e => e.Titre).Distinct(StringComparer.Ordinal).ToList();
            var contestes = titres.Select(t => t.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (rouges.Count is 0 or > MaxTestsContestes || rouges.Exists(t => !tests.Titres.Contains(t, StringComparer.Ordinal)) || !contestes.ToHashSet(StringComparer.Ordinal).SetEquals(rouges))
            {
                return $"Refusé : seuls les derniers tests rouges peuvent être contestés, {MaxTestsContestes} au plus, tous ensemble et avec leurs titres exacts. Ton dernier lancer_tests_acceptation en donne {rouges.Count} : {string.Join(" ; ", rouges)}.";
            }

            if (contestes.Exists(NonRegression.EstTitre))
            {
                return "Refusé : un test de non-régression (« [NR ») vient d'une livraison précédente et ne se conteste pas.";
            }

            if (string.IsNullOrWhiteSpace(motif) || string.IsNullOrWhiteSpace(preuve))
            {
                return "Refusé : donne ton motif et ta preuve.";
            }

            demande.ContestationTests = new ContestationTests
            {
                Titres = contestes,
                Motif = motif.Trim(),
                Preuve = preuve.Trim(),
                Dependants = [.. dernier.Dependants.Intersect(contestes, StringComparer.Ordinal)],
                Echec = Consignes.FormaterResultat(dernier, budget: Consignes.BudgetRetourDev),
            };
            fin.Terminer("contester_tests");
            return "Contestation transmise au QA amont : il réécrit ces tests ou maintient son refus, puis l'oracle vérifie ton code.";
        },
        "contester_tests",
        $"Conteste les derniers tests d'acceptation rouges, {MaxTestsContestes} au plus, que tu estimes faux, avec ton motif et ta preuve. Une seule fois par demande. Termine ta session de travail.");

    private AIFunction OutilSoumettreTestsReecrits(IBacASable bac, Demande demande, Backlog backlog, string sauvegarde, SignalFin fin, Action<string, IReadOnlyList<string>> recevoir) => AIFunctionFactory.Create(
        async ([Description("Ce que tu as changé dans les tests contestés, et pourquoi.")] string resume, CancellationToken ct) =>
        {
            if (OutilsAgent.EstUnMarqueurDeLUsine(resume))
            {
                return RefusResumeMarqueur;
            }

            var tests = demande.Tests ?? throw new InvalidOperationException("Tests d'acceptation absents.");
            var contestation = demande.ContestationTests ?? throw new InvalidOperationException("Contestation des tests absente.");
            var repertoire = depot.RepertoireAcceptation(demande.Id);
            if (RestaurerNonRegression(sauvegarde, repertoire))
            {
                return RefusNonRegressionModifiee;
            }

            if (string.Equals(Empreinte.Calculer(repertoire), tests.Empreinte, StringComparison.Ordinal))
            {
                return $"{PrefixeRefusTests}ils sont inchangés. Réécris les tests contestés, ou appelle maintenir_tests avec ta justification.";
            }

            var pdfFabriques = PdfFabriques(repertoire, PdfPresents(sauvegarde));
            if (pdfFabriques.Count > 0)
            {
                return PrefixeRefusTests + ConsignePdfFabriques(pdfFabriques);
            }

            var modifies = FichiersDeTestsModifies(sauvegarde, repertoire);
            var liste = await oracle.ListerTestsAsync(bac, ct);
            var (motif, titres) = await ControlerTestsAsync(bac, demande, backlog, ct, tests.Titres, [.. Empreinte.ListerFichiers(sauvegarde).Except(modifies, StringComparer.Ordinal)], liste);
            if (motif is not null)
            {
                return PrefixeRefusTests + motif;
            }

            var retires = TitresAjoutes(tests.Titres, titres);
            var ajoutes = TitresAjoutes(titres, tests.Titres);
            if (retires.Find(t => !contestation.Titres.Contains(t, StringComparer.Ordinal)) is { } autre)
            {
                return $"{PrefixeRefusTests}seuls les tests contestés peuvent changer, et le test « {autre} » a disparu ou changé de titre. Remets-le tel qu'il était.";
            }

            if (ajoutes.Count != retires.Count || !IdentifiantsDeCritere(ajoutes).SetEquals(IdentifiantsDeCritere(retires)))
            {
                return $"{PrefixeRefusTests}un test réécrit garde l'identifiant de critère ou de frontière du test contesté qu'il remplace, et aucun test n'est ajouté ni retiré. Titres disparus : {string.Join(" ; ", retires)}. Titres nouveaux : {string.Join(" ; ", ajoutes)}.";
            }

            var fichiers = liste.Titres.Zip(liste.FichiersDesTitres ?? []).ToLookup(t => t.First, t => t.Second, StringComparer.Ordinal);
            var reecrits = titres.Where(t => contestation.Titres.Contains(t, StringComparer.Ordinal) || ajoutes.Contains(t, StringComparer.Ordinal)).SelectMany(t => fichiers[t]).ToHashSet(StringComparer.Ordinal);
            if (modifies.Where(f => !reecrits.Contains(f) && File.Exists(Path.Combine(sauvegarde, f))).Order(StringComparer.Ordinal).ToList() is { Count: > 0 } horsContestation)
            {
                return $"{PrefixeRefusTests}un fichier de tests verrouillé sans test contesté ne peut pas changer. Remets dans leur état verrouillé : {CheminsAcceptation(horsContestation)}.";
            }

            var (applicationVide, execution) = await ExecuterContreApplicationVideAsync(bac, demande, ct, titres.Where(t => fichiers[t].Any() && !fichiers[t].Any(modifies.Contains)).ToHashSet(StringComparer.Ordinal));
            if (applicationVide is not null)
            {
                return PrefixeRefusTests + applicationVide;
            }

            SignalerControleImpossible(demande, execution);
            recevoir(resume, titres);
            fin.Terminer("soumettre_tests");
            return "Tests réécrits enregistrés et verrouillés. L'oracle va vérifier le code du Dev avec eux.";
        },
        "soumettre_tests",
        $"Soumet les tests contestés réécrits. Voici tous ses contrôles, il n'y en a pas d'autres : {ControlesCommunsDesTests()} ; les tests ont changé depuis leur verrouillage ; un fichier de tests verrouillé sans test contesté n'a pas changé ; seuls les titres des tests contestés ont changé, chacun gardant l'identifiant de critère ou de frontière du test qu'il remplace, sans test ajouté ni retiré ; enfin, contre une application vide (page blanche, réponses 200 sans contenu), chaque test hors non-régression échoue, sans être sauté. Les fichiers de tests que tu n'as pas modifiés depuis la contestation échappent aux contrôles .skip(, .fixme(, mode serial et application vide : un écart y est seulement signalé ; le maximum de tests ne compte que les tests nouveaux. Termine ton travail.");

    private static AIFunction OutilMaintenirTests(SignalFin fin, Action<string> recevoir) => AIFunctionFactory.Create(
        ([Description("Pourquoi les tests contestés sont justes : ce qu'une application conforme au contrat doit faire pour les passer.")] string justification) =>
        {
            if (string.IsNullOrWhiteSpace(justification))
            {
                return "Refusé : donne ta justification.";
            }

            recevoir(justification.Trim());
            fin.Terminer("maintenir_tests");
            return "Décision enregistrée : les tests contestés sont maintenus tels qu'ils ont été verrouillés.";
        },
        "maintenir_tests",
        "Maintient les tests contestés tels qu'ils ont été verrouillés, avec ta justification. Termine ton travail.");

    private static HashSet<string> IdentifiantsDeCritere(IEnumerable<string> titres) =>
        titres.SelectMany(t => IdentifiantCritereTitre().Matches(t).Select(m => (string?)m.Groups[1].Value).Append(IdentifiantFrontiere(t))).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private AIFunction OutilSoumettreEvaluation(Demande demande, IBacASable bac, SignalFin fin, string sorties, Action<Evaluation> recevoir, int bloquantsDejaReleves = 0, Action<string>? refusee = null) => AIFunctionFactory.Create(
        async ([Description("L'évaluation.")] EvaluationSoumise evaluation, CancellationToken ct) =>
        {
            var refus = new List<string>();
            if (evaluation.Controles.Points().Where(p => StatutControle(p.Controle.Statut) is null || string.IsNullOrWhiteSpace(p.Controle.Explication)).Select(p => p.Point).ToList() is { Count: > 0 } manquants)
            {
                refus.Add($"Liste de contrôle : donne pour chacun de ces points un statut, « fait » avec ce que tu as observé en une phrase, « non fait » ou « sans objet » avec la raison : {string.Join(" ; ", manquants)}.");
            }

            refus.AddRange(RefusEssaisEtEntrees(evaluation));
            refus.AddRange(EssaisSansTrace(evaluation, sorties));
            for (var i = 0; i < evaluation.Bloquants.Count; i++)
            {
                var soumis = evaluation.Bloquants[i];
                var libelle = $"Bloquant n°{(i + 1).ToString(CultureInfo.InvariantCulture)}";
                if (string.IsNullOrWhiteSpace(soumis.Attendu) || string.IsNullOrWhiteSpace(soumis.Constat))
                {
                    refus.Add($"{libelle} : donne le comportement attendu et ton constat.");
                }
                else if (string.IsNullOrWhiteSpace(soumis.Script))
                {
                    refus.Add($"{libelle} : aucun script de reproduction. Écris-en un sous /tmp, qui échoue tant que le comportement attendu n'est pas respecté, ou retire ce bloquant.");
                }
                else if (!ExtensionsScriptQa.Contains(Path.GetExtension(soumis.Script.Trim()), StringComparer.Ordinal))
                {
                    refus.Add($"{libelle} : {soumis.Script.Trim()} n'est ni un script Node (.js, .cjs) ni un script bash (.sh).");
                }
            }

            if (refus.Count > 0)
            {
                return RefusEvaluation(refus, refusee);
            }

            var repertoireDemande = depot.Repertoire(demande.Id);
            var numeroEvaluation = 1;
            while (Directory.Exists(Path.Combine(repertoireDemande, DossierQa, $"evaluation-{numeroEvaluation.ToString(CultureInfo.InvariantCulture)}")))
            {
                numeroEvaluation++;
            }

            var dossier = $"{DossierQa}/evaluation-{numeroEvaluation.ToString(CultureInfo.InvariantCulture)}";
            var bloquants = new List<Bloquant>();
            var acceptee = false;
            try
            {
                for (var i = 0; i < evaluation.Bloquants.Count; i++)
                {
                    var soumis = evaluation.Bloquants[i];
                    var script = soumis.Script.Trim();
                    var numero = (i + 1).ToString(CultureInfo.InvariantCulture);
                    var lecture = await bac.ExecuterAsync($"base64 -w0 -- {OutilsAgent.Quoter(script)}", TimeSpan.FromMinutes(1), "/tmp", ct: ct);
                    var contenu = lecture.Reussi ? LireBase64(lecture.Sortie) : [];
                    if (contenu.Length == 0)
                    {
                        refus.Add($"Bloquant n°{numero} : le script {script} est introuvable, vide ou illisible.");
                        continue;
                    }

                    var texte = Encoding.UTF8.GetString(contenu);
                    if (PauseFixe(texte, Path.GetExtension(script)) is { } pause)
                    {
                        refus.Add($"Bloquant n°{numero} : ton script {script} contient une pause fixe (« {pause} ») : il doit attendre l'état attendu avec un délai (attente sur un localisateur ou sur une condition, ou boucle bornée qui teste la condition et n'attend qu'entre deux essais, la pause écrite dans le corps même de cette boucle et non dans une fonction définie ailleurs), jamais pendant une durée fixe. Corrige-le, ou retire ce bloquant.");
                        continue;
                    }

                    if (AdresseEnDur().Match(texte) is { Success: true } adresse)
                    {
                        refus.Add($"Bloquant n°{numero} : ton script {script} contient une adresse d'application en dur (« {adresse.Value} ») : il prend l'adresse de l'application dans USINE_URL, sans valeur de repli. Corrige-le, ou retire ce bloquant.");
                        continue;
                    }

                    var copie = $"{dossier}/bloquant-{(bloquantsDejaReleves + i + 1).ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(script)}";
                    Directory.CreateDirectory(Path.Combine(repertoireDemande, dossier));
                    await File.WriteAllBytesAsync(Path.Combine(repertoireDemande, copie), contenu, ct);
                    bloquants.Add(new Bloquant
                    {
                        Attendu = soumis.Attendu.Trim(),
                        Constat = soumis.Constat.Trim(),
                        Script = script,
                        Copie = copie,
                    });
                }

                if (refus.Count > 0)
                {
                    return RefusEvaluation(refus, refusee);
                }

                if (bloquants.Count > 0)
                {
                    await oracle.ArreterApplicationAsync(bac, ct);
                    var redemarrage = await oracle.DemarrerApplicationAsync(bac, ct);
                    if (redemarrage.Code != 0)
                    {
                        return RefusEvaluation([$"l'usine redémarre l'application avant de contrôler tes scripts, et elle n'a pas redémarré : {Couper(redemarrage.SortieComplete, 1500)}\nSoumets de nouveau ton évaluation."], refusee, corrigible: false);
                    }
                }

                for (var i = 0; i < bloquants.Count; i++)
                {
                    var bloquant = bloquants[i];
                    var numero = (i + 1).ToString(CultureInfo.InvariantCulture);
                    var epreuve = await EprouverScriptQaAsync(demande, bac, bloquant.Copie!, Path.ChangeExtension(bloquant.Copie!, ".soumission.txt"), IssueScript.Reproduit, ct);
                    var sortie = epreuve.Execution.SortieComplete.Trim() is { Length: > 0 } texte ? $" Sa sortie :\n{Couper(texte, 1500)}" : "";
                    switch (epreuve)
                    {
                        case { Issue: IssueScript.Passe, AuPremierEssai: true }:
                            refus.Add($"Bloquant n°{numero} : ton script {bloquant.Script} réussit déjà (code 0) alors que l'application tourne : il ne reproduit pas le défaut. Corrige-le pour qu'il se termine avec le code 1 tant que le comportement attendu n'est pas respecté, ou retire ce bloquant.{sortie}");
                            break;
                        case { Issue: IssueScript.Passe }:
                            refus.Add($"Bloquant n°{numero} : ton script {bloquant.Script} a échoué au premier essai (code 1), puis réussi au second (code 0) : il ne reproduit pas le défaut de façon fiable. Vérifie un état précis et attends-le avec un délai, ou retire ce bloquant.{sortie}");
                            break;
                        case { Issue: IssueScript.Erreur }:
                            refus.Add($"Bloquant n°{numero} : ton script {bloquant.Script} s'est terminé en erreur ({MotifErreurScript(epreuve.Execution)}{(epreuve.AuPremierEssai ? "" : ", au second essai")}) : ce n'est ni une reproduction (code 1) ni un succès (code 0). Corrige ton script : code 1 seulement quand le comportement attendu n'est pas respecté, et aucune autre erreur.{sortie}");
                            break;
                    }
                }

                if (refus.Count > 0)
                {
                    return RefusEvaluation(refus, refusee);
                }

                acceptee = true;
            }
            finally
            {
                if (!acceptee)
                {
                    BacASableDocker.SupprimerRepertoire(Path.Combine(repertoireDemande, dossier));
                }
            }

            foreach (var (_, controle) in evaluation.Controles.Points())
            {
                controle.Statut = StatutControle(controle.Statut)!;
                controle.Explication = controle.Explication.Trim();
            }

            foreach (var essai in evaluation.Controles.Frontieres.Essais)
            {
                (essai.Valeur, essai.Attendu, essai.Obtenu, essai.Suite) = (essai.Valeur.Trim(), essai.Attendu.Trim(), essai.Obtenu.Trim(), essai.Suite.Trim());
            }

            var doubleSoumission = evaluation.Controles.DoubleSoumission;
            (doubleSoumission.PremiereEntree, doubleSoumission.SecondeEntree) = (doubleSoumission.PremiereEntree.Trim(), doubleSoumission.SecondeEntree.Trim());

            recevoir(new Evaluation { Bloquants = bloquants, Remarques = evaluation.Remarques, Synthese = evaluation.Synthese, Controles = evaluation.Controles, NonTeste = NonTeste(evaluation) });
            fin.Terminer("soumettre_evaluation");
            return "Évaluation enregistrée.";
        },
        OptionsStrictes("soumettre_evaluation", "Soumet ton évaluation (bloquants avec leur script de reproduction, remarques, synthèse, statut de chaque point de la liste de contrôle, ce que tu n'as pas testé). L'usine redémarre l'application, puis exécute deux fois chaque script de reproduction : il doit se terminer les deux fois avec le code 1. Termine ton travail."));

    private static string? StatutControle(string statut) =>
        statut.Trim().Replace('_', ' ').Replace('-', ' ').ToLowerInvariant() is var normalise && StatutsControle.Contains(normalise, StringComparer.Ordinal) ? normalise : null;

    private static List<string> RefusEssaisEtEntrees(EvaluationSoumise evaluation)
    {
        var refus = new List<string>();
        var frontieres = evaluation.Controles.Frontieres;
        if (StatutControle(frontieres.Statut) == ControleFait && frontieres.Essais.Count == 0)
        {
            refus.Add("Frontières : « fait » exige au moins un essai dans essais, avec la valeur essayée, l'attendu d'après la demande, l'obtenu et la suite donnée.");
        }

        var incomplets = new List<string>();
        var sansSuite = new List<string>();
        var suitesInvalides = new List<string>();
        for (var i = 0; i < frontieres.Essais.Count; i++)
        {
            var essai = frontieres.Essais[i];
            if (string.IsNullOrWhiteSpace(essai.Valeur) || string.IsNullOrWhiteSpace(essai.Attendu) || string.IsNullOrWhiteSpace(essai.Obtenu))
            {
                incomplets.Add($"essai n°{(i + 1).ToString(CultureInfo.InvariantCulture)}");
            }
            else if (string.IsNullOrWhiteSpace(essai.Suite))
            {
                if (!Concordent(essai.Attendu, essai.Obtenu))
                {
                    sansSuite.Add($"« {essai.Valeur.Trim()} » (attendu : « {essai.Attendu.Trim()} » ; obtenu : « {essai.Obtenu.Trim()} »)");
                }
            }
            else if (!SuiteValide(essai.Suite, evaluation))
            {
                suitesInvalides.Add($"« {essai.Valeur.Trim()} » (suite : « {essai.Suite.Trim()} »)");
            }
        }

        if (incomplets.Count > 0)
        {
            refus.Add($"Frontières : donne la valeur essayée, l'attendu et l'obtenu de chaque essai : {string.Join(" ; ", incomplets)}.");
        }

        if (sansSuite.Count > 0)
        {
            refus.Add($"Frontières : un écart entre l'attendu d'après la demande et l'obtenu doit devenir un bloquant ou une remarque ; si l'obtenu concorde en fait avec l'attendu, écris l'attendu dans les mêmes termes que l'obtenu ; sinon, donne la suite de ces essais, « bloquant n°k » ou « remarque n°k » : {string.Join(" ; ", sansSuite)}.");
        }

        if (suitesInvalides.Count > 0)
        {
            refus.Add($"Frontières : la suite d'un essai est « bloquant n°k » ou « remarque n°k », k étant le numéro d'un bloquant ou d'une remarque de cette évaluation : {string.Join(" ; ", suitesInvalides)}.");
        }

        var doubleSoumission = evaluation.Controles.DoubleSoumission;
        if (StatutControle(doubleSoumission.Statut) == ControleFait
            && (string.IsNullOrWhiteSpace(doubleSoumission.PremiereEntree) || string.IsNullOrWhiteSpace(doubleSoumission.SecondeEntree) || string.Equals(doubleSoumission.PremiereEntree.Trim(), doubleSoumission.SecondeEntree.Trim(), StringComparison.Ordinal)))
        {
            refus.Add("Double soumission : un « fait » nomme les deux entrées valides différentes envoyées l'une juste après l'autre, dans premiereEntree et secondeEntree ; une seule entrée, ou la même deux fois, ne montre pas que seule la dernière choisie est affichée et sert aux actions suivantes.");
        }

        return refus;
    }

    private static List<string> EssaisSansTrace(EvaluationSoumise evaluation, string sorties)
    {
        var doubleSoumission = evaluation.Controles.DoubleSoumission;
        var declares = evaluation.Controles.Frontieres.Essais
            .SelectMany((essai, i) => new[] { (Quoi: "valeur", Texte: essai.Valeur, Ou: $"de l'essai n°{(i + 1).ToString(CultureInfo.InvariantCulture)}"), (Quoi: "obtenu", Texte: essai.Obtenu, Ou: $"de l'essai n°{(i + 1).ToString(CultureInfo.InvariantCulture)}") })
            .Append((Quoi: "première entrée", Texte: doubleSoumission.PremiereEntree, Ou: "de la double soumission"))
            .Append((Quoi: "seconde entrée", Texte: doubleSoumission.SecondeEntree, Ou: "de la double soumission"))
            .Where(d => NormaliserValeur(d.Texte).Length > 0)
            .ToList();
        if (declares.Count == 0)
        {
            return [];
        }

        var trace = TraceDesCommandes(sorties);
        var absents = declares.Where(d => !ContientEssai(trace, NormaliserValeur(d.Texte).ToLowerInvariant())).Select(d => $"{d.Quoi} « {d.Texte.Trim()} » {d.Ou}").ToList();
        return absents.Count == 0
            ? []
            : [$"Essais sans trace : un essai doit avoir été réellement exécuté contre l'application ; donne la commande qui l'a fait (curl vers USINE_URL ou localhost, script Playwright, script de reproduction lancé comme node /tmp/essai.js ou bash /tmp/essai.sh), et recopie valeur, obtenu et entrées tels qu'ils figurent dans cette commande, dans le script qu'elle lance ou dans sa sortie ; une commande qui ne fait qu'imprimer des valeurs (echo, printf, console.log ou print) ne compte pas. Ni tes commandes vers l'application de cette session ni leurs sorties ne contiennent, aux blancs et à la casse près : {string.Join(" ; ", absents)}."];
    }

    private static string TraceDesCommandes(string sorties) =>
        TraceDesTours([sorties], ToursVersApplication);

    private static IEnumerable<string> ToursVersApplication(List<List<string>> tours)
    {
        var scripts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var retenus = new List<List<string>>();
        foreach (var tour in tours)
        {
            var sortie = IndexCodeDeSortie(tour);
            if (EnTeteEcritureFichier().IsMatch(tour[0]) && tour.FindLast(ligne => ligne.StartsWith(OutilsAgent.FichierEcrit, StringComparison.Ordinal)) is { } ecrit && ecrit.LastIndexOf(" (", StringComparison.Ordinal) is var fin and > 0)
            {
                scripts[ecrit[OutilsAgent.FichierEcrit.Length..fin]] = tour;
            }
            else if (EnTeteCommande().IsMatch(tour[0]) && sortie >= 0 && string.Join('\n', tour.Take(sortie)) is var commande && CommandeVersApplication().IsMatch(commande) && (!CommandeDAffichage().IsMatch(commande) || OutilVersApplication().IsMatch(commande)))
            {
                retenus.Add(tour);
                retenus.AddRange(scripts.Where(script => commande.Contains(script.Key, StringComparison.Ordinal) || (Path.GetFileName(script.Key) is { Length: > 0 } nom && commande.Contains(nom, StringComparison.Ordinal))).Select(script => script.Value));
            }
        }

        return retenus.Distinct().SelectMany(tour => tour);
    }

    private static IEnumerable<string> LignesDeCommandeReussie(List<string> tour)
    {
        var sortie = IndexCodeDeSortie(tour);
        return EnTeteCommande().IsMatch(tour[0]) && sortie >= 0 && string.Equals(tour[sortie].TrimEnd(), $"{OutilsAgent.EnteteCodeDeSortie}0", StringComparison.Ordinal)
            ? tour.Take(sortie)
            : [];
    }

    private static int IndexCodeDeSortie(List<string> tour) => tour.FindIndex(ligne => ligne.StartsWith(OutilsAgent.EnteteCodeDeSortie, StringComparison.Ordinal));

    private static string TraceDesTours(IEnumerable<string> fichiers, Func<List<List<string>>, IEnumerable<string>> retenues)
    {
        var conservees = new StringBuilder();
        foreach (var sorties in fichiers)
        {
            string texte;
            try
            {
                texte = File.Exists(sorties) ? File.ReadAllText(sorties) : "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                texte = "";
            }

            var tours = new List<List<string>> { new() };
            foreach (var ligne in texte.Split('\n'))
            {
                if (ligne.StartsWith("## Tour ", StringComparison.Ordinal))
                {
                    tours.Add([]);
                }

                tours[^1].Add(ligne);
            }

            foreach (var ligne in retenues([.. tours.Where(tour => tour.Count > 0)]))
            {
                conservees.Append(ligne).Append('\n');
            }
        }

        var decodees = EchappementUnicode().Replace(conservees.ToString(), m => ((char)int.Parse(m.Groups["code"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        return string.Join(' ', decodees.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    private static bool ContientEssai(string trace, string cherche)
    {
        for (var position = trace.IndexOf(cherche, StringComparison.Ordinal); position >= 0; position = trace.IndexOf(cherche, position + 1, StringComparison.Ordinal))
        {
            var fin = position + cherche.Length;
            if ((position == 0 || !char.IsLetterOrDigit(cherche[0]) || !char.IsLetterOrDigit(trace[position - 1]))
                && (fin == trace.Length || !char.IsLetterOrDigit(cherche[^1]) || !char.IsLetterOrDigit(trace[fin])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SuiteValide(string suite, EvaluationSoumise evaluation) =>
        SuiteEssai().Match(NormaliserValeur(suite)) is { Success: true } correspondance
        && int.TryParse(correspondance.Groups["numero"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
        && numero >= 1
        && numero <= (correspondance.Groups["type"].Value.Equals("bloquant", StringComparison.OrdinalIgnoreCase) ? evaluation.Bloquants.Count : evaluation.Remarques.Count);

    private static bool Concordent(string a, string b) => string.Equals(NormaliserValeur(a), NormaliserValeur(b), StringComparison.OrdinalIgnoreCase);

    private static string NormaliserValeur(string texte) =>
        string.Join(' ', texte.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim('«', '»', '"', ' ', '.');

    private static string NonTeste(EvaluationSoumise evaluation)
    {
        var nonFaits = evaluation.Controles.Points().Where(p => string.Equals(p.Controle.Statut, ControleNonFait, StringComparison.Ordinal)).Select(p => $"{p.Point} ({p.Controle.Explication})").ToList();
        var nonTeste = evaluation.NonTeste.Trim();
        if (nonFaits.Count == 0)
        {
            return nonTeste;
        }

        var liste = string.Join(" ; ", nonFaits);
        var autres = nonTeste.TrimEnd('.');
        return autres.Length == 0 || autres.Equals("rien", StringComparison.OrdinalIgnoreCase) ? liste : $"{autres} ; {liste}";
    }

    private static string RefusEvaluation(List<string> refus, Action<string>? refusee, bool corrigible = true)
    {
        refusee?.Invoke(string.Join(" ; ", refus));
        return $"Évaluation refusée, corrige puis soumets à nouveau :\n{string.Join("\n", refus.Select(r => $"- {r}"))}{(corrigible ? $"\n{ExecuteurAgent.ConsigneApresRefusEvaluation}" : "")}";
    }

    private static byte[] LireBase64(string texte)
    {
        try
        {
            return Convert.FromBase64String(texte.Trim());
        }
        catch (FormatException)
        {
            return [];
        }
    }

    public static List<string> CriteresNonCouverts(Backlog backlog, IEnumerable<string> titres)
    {
        var ecrits = titres.Where(t => !NonRegression.EstTitre(t)).ToList();
        return backlog.Stories
            .SelectMany(s => s.Criteres)
            .Select(c => c.Id)
            .Where(id => !ecrits.Any(t => t.Contains($"[{id}]", StringComparison.Ordinal)))
            .ToList();
    }

    public static List<string> TestsHorsBacklog(Backlog backlog, IEnumerable<string> titres)
    {
        var identifiants = backlog.Stories.SelectMany(s => s.Criteres).Select(c => c.Id).ToList();
        return titres
            .Where(t => !NonRegression.EstTitre(t) && IdentifiantFrontiere(t) is null && !identifiants.Any(id => t.Contains($"[{id}]", StringComparison.Ordinal)))
            .ToList();
    }

    private static async Task<string?> EmpreinteCodeAsync(IBacASable bac, CancellationToken ct)
    {
        var resultat = await bac.ExecuterAsync(CommandeEmpreinteCode, TimeSpan.FromMinutes(2), ct: ct);
        var empreinte = resultat.Sortie.Trim();
        return resultat.Reussi && EmpreinteGit().IsMatch(empreinte) ? empreinte : null;
    }

    private static async Task ClasserInstablesAsync(Demande demande, IBacASable bac, ResultatOracle resultat, bool essai, bool cumuler, CancellationToken ct)
    {
        if (resultat.Total == 0 || !resultat.EmpreinteValide || demande.Tests is not { } tests)
        {
            return;
        }

        var reussis = new List<string>(tests.Titres);
        foreach (var echec in resultat.Echecs)
        {
            reussis.Remove(echec.Titre);
        }

        if (reussis.Count == resultat.Reussis && await EmpreinteCodeAsync(bac, ct) is { } empreinte)
        {
            if (essai)
            {
                demande.ResultatsParVersion.RemoveAll(v => v.Essai && !string.Equals(v.EmpreinteCode, empreinte, StringComparison.Ordinal));
            }

            var version = demande.ResultatsParVersion.Find(v => v.Essai == essai && string.Equals(v.EmpreinteCode, empreinte, StringComparison.Ordinal));
            if (version is null)
            {
                version = new ResultatsVersion { EmpreinteCode = empreinte, Essai = essai };
                demande.ResultatsParVersion.Add(version);
            }
            else if (!cumuler)
            {
                version.Reussis = [];
                version.Echoues = [];
            }

            version.Reussis = [.. version.Reussis.Union(reussis, StringComparer.Ordinal)];
            version.Echoues = [.. version.Echoues.Union(resultat.Echecs.Select(e => e.Titre), StringComparer.Ordinal)];
            demande.TestsInstables = [.. demande.TestsInstables.Union(version.Reussis.Intersect(version.Echoues, StringComparer.Ordinal), StringComparer.Ordinal)];
        }

        var excuses = resultat.Echecs.Where(e => demande.TestsInstables.Contains(e.Titre, StringComparer.Ordinal)).ToList();
        if (excuses.Count == 0)
        {
            return;
        }

        resultat.Echecs = [.. resultat.Echecs.Except(excuses)];
        resultat.Instables = [.. resultat.Instables, .. excuses.Select(e => e.Titre)];
        resultat.Reussis += excuses.Count;
        resultat.Echoues -= excuses.Count;
        if (resultat.Echecs.Count == 0 && resultat.EtapeEchec is "tests")
        {
            resultat.Succes = true;
            resultat.EtapeEchec = null;
        }
    }

    private static async Task<List<string>> DependancesNonImporteesAsync(IBacASable bac, CancellationToken ct)
    {
        var resultat = await bac.ExecuterAsync(CommandeDependances, TimeSpan.FromMinutes(2), entree: Ressources.Lire("controles/dependances-non-importees.js"), ct: ct);
        return resultat.Reussi ? [.. resultat.Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : [];
    }

    private static async Task<List<string>> RestesDeDebogageAsync(IBacASable bac, CancellationToken ct)
    {
        var resultat = await bac.ExecuterAsync(CommandeFichiersSuivis, TimeSpan.FromMinutes(2), ct: ct);
        return resultat.Reussi ? RestesDeDebogage(resultat.Sortie) : [];
    }

    internal static List<string> RestesDeDebogage(string fichiersSuivis) =>
        [.. fichiersSuivis.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(f => ResteDeDebogage().IsMatch(f))];

    private static async Task PreparerGitAsync(IBacASable bac, CancellationToken ct)
    {
        var motifs = string.Join(' ', MotifsHorsInstantane.Concat(MotifsDonnees).Select(OutilsAgent.Quoter));
        await bac.ExecuterAsync(
            "if [ ! -d .git ]; then git init -q; fi; mkdir -p .git/info; touch .git/info/exclude; "
            + $"for p in {motifs}; do grep -qxF \"$p\" .git/info/exclude || echo \"$p\" >> .git/info/exclude; done",
            TimeSpan.FromMinutes(1),
            ct: ct);
    }

    private async Task<ResultatCommande> CommiterAsync(Demande demande, IBacASable bac, string message, string reference, string referenceComplete, string designation, CancellationToken ct)
    {
        var commit = await bac.ExecuterAsync(
            $"git add -A && (git diff --cached --quiet || git -c core.hooksPath=/dev/null commit -q -m {OutilsAgent.Quoter(message)}) && git update-ref {reference} HEAD",
            TimeSpan.FromMinutes(2),
            ct: ct);
        if (!commit.Reussi)
        {
            return commit;
        }

        await bac.ExecuterAsync($"find . -path ./.git -prune -o -type d -empty -print0 > {ListeDossiersVides}", TimeSpan.FromMinutes(2), ct: ct);
        var exclusions = string.Join(' ', MotifsHorsInstantane.Select(m => $"-x {OutilsAgent.Quoter(m)}"));
        var complet = await bac.ExecuterAsync(
            $"git update-ref -d {referenceComplete} "
            + $"&& rm -f {IndexComplet} && GIT_INDEX_FILE={IndexComplet} git read-tree HEAD "
            + $"&& git ls-files -z -o {exclusions} | GIT_INDEX_FILE={IndexComplet} xargs -0 -r git add -f -- "
            + $"&& git update-ref {referenceComplete} \"$(GIT_INDEX_FILE={IndexComplet} git write-tree)\"",
            TimeSpan.FromMinutes(2),
            ct: ct);
        if (!complet.Reussi)
        {
            depot.AjouterEvenement(demande, ChefDeProjet, $"Attention : l'instantané complet de {designation} a échoué ({Couper(complet.SortieComplete, 300)}) ; un retour à cet état ne restaurera pas les fichiers ignorés par le projet.");
        }

        return commit;
    }

    private static string SansResultat(string role, ResultatAgent resultat) =>
        $"Le rôle {role} s'est arrêté sans rendre son résultat ({Raison(resultat)}).";

    private static string Raison(ResultatAgent resultat) => resultat switch
    {
        { BudgetEpuise: true } => "plafond de coût atteint",
        { MotifArret: { Length: > 0 } motif } => Couper(motif, 1000),
        { DernierTexte: { Length: > 0 } texte } => $"dernier message : « {Couper(texte, 300)} »",
        _ => $"{resultat.Iterations} tours sans conclusion",
    };

    [GeneratedRegex("^[0-9a-f]{40,64}$")]
    private static partial Regex EmpreinteGit();

    [GeneratedRegex(@"(?:^|/)(?:tmp|debug)-[^/]*$|\.(?:orig|bak|rej|swp)$|~$|-debug\.[^/]*$", RegexOptions.IgnoreCase)]
    private static partial Regex ResteDeDebogage();

    [GeneratedRegex(@"[^\W\d_]{3,}")]
    private static partial Regex MotDeStack();

    [GeneratedRegex(@"[^\W\d_]{4,}")]
    internal static partial Regex MotSignificatif();

    [GeneratedRegex(@"[^.!?\n]+[.!?]?")]
    internal static partial Regex Phrase();

    [GeneratedRegex(@"(?<!\b(?:(?:sans|pas\s+(?:besoin\s+)?de|aucune?)\s+(?:[a-z]+\s+)?|non[\s-]+))(?:persist|durable)|apres\s+(?:un\s+|le\s+)?redemarrage|conserv.*redemarr|redemarr.*conserv", RegexOptions.IgnoreCase)]
    private static partial Regex ExigencePersistance();

    [GeneratedRegex(@"persist|conserv|durable|recharg|redemarr|relanc|toujours\s+present", RegexOptions.IgnoreCase)]
    private static partial Regex CriterePersistance();

    [GeneratedRegex(@"regression|sans\s+(?:rien\s+)?casser|(?:exist|actuel).*(?:continu\w*\s+(?:a|de)\s+fonctionner|fonctionn\w*\s+encore)", RegexOptions.IgnoreCase)]
    private static partial Regex ExigenceNonRegression();

    [GeneratedRegex(@"existant|regression|toujours|encore|comme\s+avant|continu", RegexOptions.IgnoreCase)]
    private static partial Regex CritereNonRegression();

    [GeneratedRegex(@"^S\d+$")]
    private static partial Regex IdStory();

    [GeneratedRegex(@"^S\d+-C\d+$")]
    private static partial Regex IdCritere();

    [GeneratedRegex(@"^N[1-3]$")]
    private static partial Regex IdNotion();

    [GeneratedRegex(@"\bN[1-3]\b")]
    private static partial Regex NotionNommee();

    [GeneratedRegex(@"^[ \t]*(?:(?:[-*+>|]|\d+[.)]|#{1,6})[ \t]*)?")]
    private static partial Regex DebutDeLigneDuContrat();

    [GeneratedRegex(@"\[(S\d+-C\d+)\]")]
    private static partial Regex IdentifiantCritereTitre();

    [GeneratedRegex(@"(?:^|› )(?:\[S\d+-C\d+\] *)*\[(F\d+)\]")]
    private static partial Regex IdFrontiereTitre();

    [GeneratedRegex(@"\s+")]
    internal static partial Regex Blancs();

    [GeneratedRegex(@"^\S.*\(\d+,\d+\): error TS\d+: ")]
    private static partial Regex DiagnosticTypeScript();

    [GeneratedRegex(@"^\S.*\(\d+,\d+\): error TS(?:2304|2305|2307|2339|2349|2551|2552|2724): ")]
    private static partial Regex DiagnosticTypeScriptBloquant();

    [GeneratedRegex(@"\b(?<pause>waitForTimeout)\s*\(|\bawait\s+(?:[\w$]+\.)*(?<pause>setTimeout)\s*\(|\bnew\s+Promise\b[^;]*?\b(?<pause>setTimeout)\s*\(|\bpromisify\s*\(\s*(?:[\w$]+\.)*(?<pause>setTimeout)\b|(?<![\w.$])(?<pause>sleep)\s*\(")]
    private static partial Regex PauseFixeNode();

    [GeneratedRegex(@"'[^']*'|""(?:\\.|[^""\\])*""|(?<![^\s;|&()])#[^\n]*|\b(?<mot>do|done)\b|(?<![\w.$-])(?<mot>sleep\s+[0-9.]+[smhd]?\b)")]
    private static partial Regex MotBoucleOuPause();

    [GeneratedRegex(@"\b(?:for(?:\s+await)?\s*\([^\n]*\)|while\s*\([^;\n]*\)|do)\s*$", RegexOptions.Singleline)]
    private static partial Regex EnTeteDeBoucle();

    [GeneratedRegex(@"^(?<type>bloquant|remarque)\s+n\s*[°º]\s*(?<numero>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuiteEssai();

    [GeneratedRegex(@"\b(?:localhost|127\.0\.0\.1|0\.0\.0\.0):\d+", RegexOptions.IgnoreCase)]
    private static partial Regex AdresseEnDur();

    [GeneratedRegex(@"\\u(?<code>[0-9A-Fa-f]{4})")]
    private static partial Regex EchappementUnicode();

    [GeneratedRegex(@"^## Tour \d+ : executer_commande\(")]
    private static partial Regex EnTeteCommande();

    [GeneratedRegex(@"^## Tour \d+ : ecrire_fichier\(")]
    private static partial Regex EnTeteEcritureFichier();

    [GeneratedRegex(@"USINE_URL|\blocalhost\b|\b127\.0\.0\.1\b|playwright|" + LancementDeScript)]
    private static partial Regex CommandeVersApplication();

    [GeneratedRegex(@"^## Tour \d+ : executer_commande\((?:delai_secondes=[^,]*, )?commande=\s*(?:echo|printf)\b")]
    private static partial Regex CommandeDAffichage();

    [GeneratedRegex(@"\bcurl\b|playwright|\b(?:node|python3?)\s+-(?:e|c|-eval)\b|\b(?:bash|sh)\s+-c\b|" + LancementDeScript)]
    private static partial Regex OutilVersApplication();

    [GeneratedRegex(@"(?<![\w.$])test(?<groupe>\.describe(?:\.(?:serial|parallel|only|skip|fixme))?)?\s*\(\s*(?:['""`]\[(?<frontiere>F\d+)\])?")]
    private static partial Regex AppelDeTest();

    private enum IssueScript
    {
        Passe,
        Reproduit,
        Erreur,
    }

    private sealed record EpreuveScript(IssueScript Issue, ResultatCommande Execution, bool AuPremierEssai);
}

public sealed class PlafondAtteintException(string message) : InvalidOperationException(message);

public sealed class BacIndisponibleException(string message) : InvalidOperationException(message);

public sealed class SuiviTests
{
    public bool AJour { get; set; }

    public bool DernierEssaiConteneurNeuf { get; set; }

    public ResultatOracle? DernierResultat { get; set; }

    public bool RefusApresRougeDonne { get; set; }

    public static bool Modifie(AIFunction outil) => outil.Name is "ecrire_fichier" or "remplacer_dans_fichier" or "executer_commande";

    public static bool AModifie(string outil, string resultat) =>
        (outil == "executer_commande" && !resultat.StartsWith(OutilsAgent.PrefixeRefusReecriture, StringComparison.Ordinal) && !resultat.StartsWith(OutilsAgent.PrefixeRefusSuppression, StringComparison.Ordinal) && !resultat.StartsWith(OutilsAgent.PrefixeRefusDossierInterne, StringComparison.Ordinal))
        || resultat.StartsWith("Erreur d'écriture", StringComparison.Ordinal)
        || !(resultat.StartsWith("Refusé", StringComparison.Ordinal) || resultat.StartsWith("Erreur", StringComparison.Ordinal));
}

public sealed class OutilQuiPerimeLesTests(AIFunction outil, SuiviTests suivi) : DelegatingAIFunction(outil)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var resultat = await base.InvokeCoreAsync(arguments, cancellationToken);
        if (SuiviTests.AModifie(Name, resultat?.ToString() ?? ""))
        {
            suivi.AJour = false;
        }

        return resultat;
    }
}
