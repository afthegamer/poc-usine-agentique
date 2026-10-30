using System.Globalization;
using System.Text;
using Usine.Core.Etat;
using Usine.Core.Oracle;
using Usine.Core.Orchestration;
using static Usine.Core.Texte;

namespace Usine.Core.Services;

public sealed record AvisQa(string Titre, IReadOnlyList<string> Mentions, string Synthese, string NonTeste, IReadOnlyList<string> Points);

public static class FormateurDemande
{
    private const int MaxCaracteresContenus = 20_000;

    private const int MaxTitresEnEchec = 15;

    private const int MaxCaracteresTitre = 300;

    public static string ActionAttendue(Demande demande) => (demande.Statut, demande.Etape) switch
    {
        (StatutDemande.AttenteReponses, _) =>
            $"Réponds aux questions du PO (outil MCP repondre_questions, ou en ligne de commande : usine repondre {demande.Id} \"…\").",
        (StatutDemande.AttenteValidation, EtapeDemande.ValidationBesoin) =>
            $"Relis le besoin tel que le PO l'a compris, puis approuve-le ou refuse-le avec un commentaire (outil MCP valider_etape, étape « besoin », ou : usine valider {demande.Id} besoin [--refuser \"…\"]). Le PO n'écrit les user stories qu'après son approbation.",
        (StatutDemande.AttenteValidation, EtapeDemande.ValidationBacklog) =>
            $"Relis le backlog, puis approuve-le ou refuse-le avec un commentaire (outil MCP valider_etape, étape « backlog », ou : usine valider {demande.Id} backlog [--refuser \"…\"]).",
        (StatutDemande.AttenteValidation, EtapeDemande.ValidationTests) =>
            $"Relis les tests d'acceptation et le contrat CONTRAT.md (fichiers listés ci-dessous, verrouillés ensemble à l'approbation ; le Dev et le QA suivent ce contrat ; leur contenu est donné par l'outil MCP voir_avancement en mode détaillé, ou : usine statut {demande.Id} --detail), puis approuve-les ou refuse-les (outil MCP valider_etape, étape « tests », ou : usine valider {demande.Id} tests [--refuser \"…\"]).",
        (StatutDemande.AttenteValidation, EtapeDemande.ValidationLivraison) =>
            $"{EchecLivraison(demande)}{VersionRefusee(demande)}Consulte le livrable et le rapport, puis approuve ou refuse la livraison (outil MCP valider_etape, étape « livraison », ou : usine valider {demande.Id} livraison [--refuser \"…\"]).",
        (StatutDemande.EnCours, _) => "Rien : l'usine travaille. Reviens voir l'avancement dans quelques minutes.",
        (StatutDemande.Terminee, _) => "Rien : la demande est terminée. Le livrable est disponible.",
        (StatutDemande.Echouee, _) =>
            $"La demande a échoué : lis l'erreur et le journal indiqués ci-dessous, et rends-en compte au demandeur. S'il le demande, une fois la cause corrigée, reprends-la à l'étape de l'échec (outil MCP reprendre_demande, ou en ligne de commande : usine reprendre {demande.Id}) ; pour l'abandonner et supprimer son espace de travail, annule-la (outil MCP annuler_demande, ou : usine annuler {demande.Id}).",
        (StatutDemande.Annulee, _) => "Rien : la demande est annulée.",
        _ => "—",
    };

    public static string Resume(Demande demande, DepotDemandes depot, bool detaille)
    {
        var texte = new StringBuilder();
        texte.AppendLine(CultureInfo.InvariantCulture, $"# Demande {demande.Id} — {demande.Titre}").AppendLine();
        texte.AppendLine(CultureInfo.InvariantCulture, $"- Statut : **{demande.Statut}** (étape : {demande.Etape})");
        texte.AppendLine(CultureInfo.InvariantCulture, $"- Action attendue : {ActionAttendue(demande)}");
        texte.AppendLine(CultureInfo.InvariantCulture, $"- Coût estimé : {Consignes.FormaterCout(demande.CoutTotal, demande.CoutConnu)}{(demande.Couts.Count == 0 ? " (aucun appel de modèle enregistré)" : "")}");
        if (demande.Projet is { } projet)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- Projet : {projet} ({Path.Combine(depot.RepertoireProjets, projet)})");
        }
        else if (demande.EstProjetExistant)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- Projet existant : {demande.DepotSource}");
        }

        if (demande.PublicationProjet is { } publication)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- {publication.Message}");
        }

        if (demande.CadrageParDemandeur)
        {
            texte.AppendLine("- Mode auto après le backlog : questions du PO, besoin compris et backlog validés par toi, tests et livraison automatiques ; le projet n'est publié que si la livraison réussit.");
        }
        else if (demande.ModeAuto)
        {
            texte.AppendLine("- Mode auto : validations automatiques, aucune question.");
        }

        if (!string.IsNullOrWhiteSpace(demande.Erreur))
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- Erreur : {demande.Erreur}");
        }

        if (demande.Statut == StatutDemande.Echouee)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- Journal : {depot.CheminJournal(demande.Id)}");
        }

        if (demande.Questions.Count > 0)
        {
            texte.AppendLine().AppendLine("## Questions du PO").AppendLine();
            for (var i = 0; i < demande.Questions.Count; i++)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"{i + 1}. {demande.Questions[i]}");
            }
        }

        if (demande.Besoin is { } besoin && (demande.Etape == EtapeDemande.ValidationBesoin || (detaille && demande.Backlog is null)))
        {
            texte.AppendLine();
            if (!demande.BesoinValide && demande.Etape < EtapeDemande.ValidationBesoin && demande.CommentairesBesoin.Count > 0)
            {
                texte.AppendLine("## Besoin précédent, refusé par le demandeur (pas encore remplacé par le PO)").AppendLine();
            }

            texte.AppendLine(CultureInfo.InvariantCulture, $"## Besoin{(demande.BesoinValide ? " validé" : "")} : {besoin.Titre}").AppendLine();
            texte.AppendLine(besoin.Resume);
        }

        if (demande.Backlog is not null && (detaille || demande.Etape == EtapeDemande.ValidationBacklog))
        {
            texte.AppendLine();
            if (demande.Etape < EtapeDemande.ValidationBacklog && demande.CommentairesBacklog.Count > 0)
            {
                texte.AppendLine("## Backlog précédent, refusé par le demandeur (pas encore remplacé par le PO)").AppendLine();
            }

            texte.AppendLine(Consignes.FormaterBacklog(demande.Backlog));
        }

        if (demande.Tests is not null && (detaille || demande.Etape == EtapeDemande.ValidationTests))
        {
            var repertoire = depot.RepertoireAcceptation(demande.Id);
            var aValider = demande.Etape == EtapeDemande.ValidationTests;
            var fichiers = aValider ? Empreinte.ListerFichiers(repertoire) : demande.Tests.Fichiers;
            texte.AppendLine(CultureInfo.InvariantCulture, $"## Tests d'acceptation ({demande.Tests.Titres.Count})").AppendLine();
            texte.AppendLine(CultureInfo.InvariantCulture, $"Dossier : {repertoire}").AppendLine();
            texte.AppendLine("Fichiers :");
            foreach (var fichier in fichiers)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {fichier}");
            }

            texte.AppendLine().AppendLine("Tests :");
            foreach (var titre in demande.Tests.Titres)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {titre}");
            }

            if (demande.Tests.ExecutionInitiale is { } initiale)
            {
                texte.AppendLine().AppendLine(NonRegression.AvantToutCode(initiale, demande.Tests.Titres));
            }

            texte.AppendLine();
            if (detaille && aValider)
            {
                AjouterContenus(texte, repertoire, fichiers);
            }
        }

        if (detaille && demande.Verifications.Count > 1)
        {
            texte.AppendLine("## Vérifications précédentes").AppendLine();
            for (var i = 0; i < demande.Verifications.Count - 1; i++)
            {
                var precedente = demande.Verifications[i];
                texte.AppendLine(CultureInfo.InvariantCulture, $"- n°{i + 1} : {(precedente.Succes ? $"réussie ({precedente.Reussis}/{precedente.Total} tests{Consignes.DontInstables(precedente)})" : $"échec à l'étape « {precedente.EtapeEchec} » ({precedente.Reussis}/{precedente.Total} tests réussis{Consignes.DontInstables(precedente)})")}");
                foreach (var echec in precedente.Echecs.Take(MaxTitresEnEchec))
                {
                    texte.AppendLine(CultureInfo.InvariantCulture, $"  - en échec : {Couper(echec.Titre, MaxCaracteresTitre)}");
                }

                if (precedente.Echecs.Count > MaxTitresEnEchec)
                {
                    texte.AppendLine(CultureInfo.InvariantCulture, $"  - … et {precedente.Echecs.Count - MaxTitresEnEchec} autres échecs.");
                }
            }

            texte.AppendLine();
        }

        if (demande.Verifications.LastOrDefault() is { } verification)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"## Dernière vérification (n°{demande.Verifications.Count})").AppendLine();
            texte.AppendLine(Consignes.FormaterResultat(verification, maxEchecs: detaille ? 15 : 5));
        }

        if (Avis(demande) is { } avis)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"## {avis.Titre}").AppendLine();
            foreach (var mention in avis.Mentions)
            {
                texte.AppendLine(mention).AppendLine();
            }

            texte.AppendLine(avis.Synthese);
            if (!string.IsNullOrWhiteSpace(avis.NonTeste))
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"Non testé : {avis.NonTeste}");
            }

            foreach (var point in avis.Points)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {point}");
            }

            if (Approuvee(demande))
            {
                texte.AppendLine().AppendLine(string.IsNullOrWhiteSpace(demande.CommentaireApprobationLivraison)
                    ? "Le demandeur a approuvé la livraison."
                    : $"Le demandeur a approuvé la livraison, avec ce commentaire : {demande.CommentaireApprobationLivraison}");
            }

            texte.AppendLine();
        }

        if (demande.Livrable is { } livrable)
        {
            texte.AppendLine(LivrableDepasse(demande)
                ? "## Livrable précédent (remplacé à la prochaine livraison)"
                : LivrableAbandonne(demande) ? "## Livrable précédent (livraison refusée ou reprise, demande annulée depuis)" : "## Livrable").AppendLine();
            texte.AppendLine(CultureInfo.InvariantCulture, $"- Code : {livrable.RepertoireCode}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"- Rapport : {livrable.Rapport}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"- Tests d'acceptation : {(livrable.Succes ? "tous verts" : "non tous verts")}");
            texte.AppendLine();
        }

        var evenements = detaille ? demande.Evenements : demande.Evenements.TakeLast(8).ToList();
        if (evenements.Count > 0)
        {
            texte.AppendLine("## Derniers événements").AppendLine();
            foreach (var evenement in evenements)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {LigneEvenement(evenement)}");
            }
        }

        return texte.ToString().TrimEnd();
    }

    public static AvisQa? Avis(Demande demande)
    {
        if (demande.Evaluation is not { } evaluation)
        {
            return null;
        }

        var approuvee = Approuvee(demande);
        var anterieure = demande.EvaluationAnterieureAuDernierVert;
        var bloquant = approuvee ? "Bloquant signalé" : anterieure ? "Bloquant sur la version précédente" : "Bloquant";
        return new AvisQa(
            approuvee ? "Évaluation du QA soumise au demandeur" : anterieure ? "Évaluation du QA (version précédente, non réévaluée depuis)" : "Évaluation du QA",
            [.. anterieure ? [Consignes.EvaluationAnterieure] : Array.Empty<string>(), .. demande.EvaluationSansAvis ? [Consignes.SansAvisQa] : Array.Empty<string>()],
            evaluation.Synthese,
            evaluation.NonTeste,
            [.. evaluation.Bloquants.Select(b => Consignes.EtatBloquant(b, $"{bloquant} : ")), .. Consignes.LignesReverification(evaluation), .. evaluation.Remarques.Select(r => $"Remarque : {r}")]);
    }

    public static string LigneEvenement(Evenement evenement) =>
        string.Create(CultureInfo.InvariantCulture, $"{evenement.Date.ToLocalTime():yyyy-MM-dd HH:mm:ss} [{evenement.Acteur}] {evenement.Message}");

    public static string? AvertissementEchecApresLivraison(Demande demande, DepotDemandes depot)
    {
        if (demande.Statut == StatutDemande.Echouee)
        {
            var partiel = depot.RepertoireTravailPartiel(demande.Id);
            return $"Attention : la demande {demande.Id} a échoué après sa dernière livraison, à l'étape {demande.Etape} ; le rapport ci-dessous date de cette livraison et ne décrit pas le travail fait depuis.{(Directory.Exists(partiel) ? $" Travail partiel conservé au moment de l'échec : {partiel}" : "")}\nErreur : {demande.Erreur}";
        }

        if (LivrableDepasse(demande))
        {
            return $"Attention : la demande {demande.Id} est de nouveau en cours, à l'étape {demande.Etape}, après sa dernière livraison (refusée ou reprise depuis) ; le rapport ci-dessous date de cette livraison et ne décrit pas le travail fait depuis, et le code sera remplacé à la prochaine livraison.";
        }

        if (LivrableAbandonne(demande))
        {
            return $"Attention : la demande {demande.Id} a été annulée à l'étape {demande.Etape}, après sa dernière livraison (refusée ou reprise depuis) ; le rapport ci-dessous date de cette livraison et ne décrit ni le motif d'un refus ni le travail fait depuis (la suite est dans la chronologie : outil MCP voir_avancement en mode détaillé, ou : usine statut {demande.Id} --detail).";
        }

        return null;
    }

    public static string Ligne(Demande demande) =>
        $"{demande.Id}  {demande.Statut,-17} {demande.Etape,-19} {(demande.CoutConnu ? demande.CoutTotal.ToString("0.00", CultureInfo.InvariantCulture) : "?"),7} $  {demande.Titre}{(demande.Projet is { } projet ? $"  [projet {projet}]" : "")}";

    public static string ListeProjets(IReadOnlyList<ProjetUsine> projets)
    {
        if (projets.Count == 0)
        {
            return "Aucun projet : une demande lancée sans projet ni dépôt en crée un quand elle se termine avec succès.";
        }

        var texte = new StringBuilder();
        foreach (var projet in projets)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"{projet.Nom}  {projet.Chemin}  (dernière mise à jour : {projet.DerniereMiseAJour.ToLocalTime():yyyy-MM-dd HH:mm:ss})");
            if (projet.Demandes.Count == 0)
            {
                texte.AppendLine("  - aucune demande enregistrée pour ce projet");
            }

            foreach (var demande in projet.Demandes)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"  - {demande.Id}  {(demande.EstProjetExistant ? "évolution" : "création")}  {demande.Statut}  {demande.Titre}");
            }
        }

        return texte.ToString().TrimEnd();
    }

    private static bool Approuvee(Demande demande) => demande.Statut == StatutDemande.Terminee && !demande.ModeAuto;

    private static bool LivrableDepasse(Demande demande) =>
        demande.Livrable is not null && demande.Statut is StatutDemande.EnCours or StatutDemande.Echouee && demande.Etape <= EtapeDemande.Livraison;

    private static bool LivrableAbandonne(Demande demande) =>
        demande.Livrable is not null && demande.Statut == StatutDemande.Annulee && demande.Etape <= EtapeDemande.Livraison;

    private static void AjouterContenus(StringBuilder texte, string repertoire, IReadOnlyList<string> fichiers)
    {
        var coupe = string.Create(CultureInfo.InvariantCulture, $"[… contenu coupé : au plus {MaxCaracteresContenus} caractères des fichiers d'acceptation sont affichés ; la suite est à lire dans le dossier indiqué plus haut …]");
        var restant = MaxCaracteresContenus;
        foreach (var fichier in fichiers)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"### Contenu de {fichier}").AppendLine();
            if (restant == 0)
            {
                texte.AppendLine(coupe).AppendLine();
                continue;
            }

            try
            {
                using var lecteur = new StreamReader(Path.Combine(repertoire, fichier));
                var tampon = new char[restant + 1];
                var lus = lecteur.ReadBlock(tampon, 0, tampon.Length);
                var contenu = new string(tampon, 0, Math.Min(lus, restant));
                if (contenu.Contains('\0'))
                {
                    texte.AppendLine("[fichier binaire : contenu non affiché]").AppendLine();
                    continue;
                }

                restant -= contenu.Length;
                texte.AppendLine("````").AppendLine(contenu.TrimEnd()).AppendLine("````");
                if (lus > contenu.Length)
                {
                    texte.AppendLine(coupe);
                }

                texte.AppendLine();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"[contenu illisible : {ex.Message}]").AppendLine();
            }
        }
    }

    private static string EchecLivraison(Demande demande) => demande.Verifications.LastOrDefault() switch
    {
        { Succes: false } derniere when Consignes.EchecAvantLesTests(derniere) is { } horsService => $"Attention, livraison en échec : {horsService} à la dernière vérification. ",
        { Succes: false } derniere => $"Attention, livraison en échec : tests d'acceptation non tous verts ({derniere.Reussis}/{derniere.Total} à la dernière vérification). ",
        _ => "",
    };

    private static string VersionRefusee(Demande demande) => demande switch
    {
        { VersionRefuseeRestauree: true } => "Attention : la version livrée est celle qui avait été refusée (retour au dernier état vert, antérieur au refus) ; le commentaire du refus n'a pas pu être appliqué. ",
        { LivraisonIdentiqueRefusee: true } => "Attention : livraison identique à la version refusée : le code n'a pas changé depuis ce refus, et son commentaire n'a pas été appliqué. ",
        _ => "",
    };
}
