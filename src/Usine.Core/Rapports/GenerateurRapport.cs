using System.Globalization;
using System.Text;
using Usine.Core.Configuration;
using Usine.Core.Etat;
using Usine.Core.Oracle;
using Usine.Core.Orchestration;

namespace Usine.Core.Rapports;

public static class GenerateurRapport
{
    private const int LongueurDebut = 160;

    public static string Generer(Demande demande, ConfigUsine config)
    {
        var texte = new StringBuilder();
        var succes = demande.Verifications.LastOrDefault()?.Succes == true;
        texte.AppendLine(CultureInfo.InvariantCulture, $"# Rapport de l'usine : {demande.Titre}").AppendLine();
        texte.AppendLine(CultureInfo.InvariantCulture, $"- **Demande** : {demande.Id}");
        var resultat = (succes, Consignes.EchecAvantLesTests(demande.Verifications.LastOrDefault())) switch
        {
            (true, _) => "tests d'acceptation verts",
            (false, { } horsService) => $"échec, {horsService}",
            _ => "échec partiel, tests d'acceptation non tous verts",
        };
        texte.AppendLine(CultureInfo.InvariantCulture, $"- **Résultat** : {resultat}");
        texte.AppendLine(CultureInfo.InvariantCulture, $"- **Type** : {(demande.EstProjetExistant ? $"évolution du dépôt {demande.DepotSource}" : "application nouvelle")}");
        if (demande.Projet is not null || demande.PublicationProjet is not null)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- **Projet** : {demande.Projet ?? "aucun"}{(demande.PublicationProjet is { } publication ? $" — {publication.Message}" : "")}");
        }
        texte.AppendLine(CultureInfo.InvariantCulture, $"- **Coût total estimé** : {Consignes.FormaterCout(demande.CoutTotal, demande.CoutConnu)}");
        texte.AppendLine(CultureInfo.InvariantCulture, $"- **Durée** : {Duree(demande.MisAJourLe - demande.CreeLe)}");
        texte.AppendLine(CultureInfo.InvariantCulture, $"- **Décision du demandeur** : {Decision(demande)}");
        texte.AppendLine();

        texte.AppendLine("## Demande d'origine").AppendLine().AppendLine(demande.Texte).AppendLine();
        foreach (var echange in demande.Echanges)
        {
            texte.AppendLine("### Questions du PO").AppendLine();
            foreach (var question in echange.Questions)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {question}");
            }

            texte.AppendLine().AppendLine("**Réponses** :").AppendLine().AppendLine(echange.Reponses).AppendLine();
        }

        if (demande.Backlog is not null)
        {
            texte.AppendLine(Consignes.FormaterBacklog(demande.Backlog));
        }

        if (demande.Tests is not null)
        {
            texte.AppendLine("## Tests d'acceptation").AppendLine();
            var origine = string.Equals(demande.Tests.Resume, Orchestrateur.ResumeAdoption, StringComparison.Ordinal)
                ? "adoptés tels quels depuis le dossier d'acceptation avant le code"
                : demande.NouveauxTests ? "réécrits par le QA amont pour cette réévaluation" : "écrits avant le code";
            var ajoutes = demande.TestsAjoutesAuxRefus.Values.SelectMany(t => t).ToHashSet(StringComparer.Ordinal);
            var ecrits = demande.Tests.Titres.Count(t => !NonRegression.EstTitre(t) && !ajoutes.Contains(t));
            var repris = demande.Tests.Titres.Count(NonRegression.EstTitre);
            var ajoutesAuxRefus = demande.Tests.Titres.Count - ecrits - repris;
            texte.AppendLine(CultureInfo.InvariantCulture, $"{ecrits} tests, {origine} et verrouillés (empreinte `{Empreinte.Abreger(demande.Tests.Empreinte)}`){(repris > 0 ? $", avec {repris} tests de non-régression repris des demandes précédentes" : "")}{(ajoutesAuxRefus > 0 ? $", puis {ajoutesAuxRefus} tests ajoutés par le demandeur à ses refus de livraison" : "")}.").AppendLine();
            if (demande.Tests.ExecutionInitiale is { } initiale)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"Exécution avant tout code : {(NonRegression.ControleAvantCodeImpossible(initiale) ? NonRegression.AvantCodeImpossible : $"{initiale.Echoues} en échec sur {initiale.Total}{NonRegression.SansReprisAvantCode(initiale, demande.Tests.Titres)}")}.").AppendLine();
            }

            var frontieres = demande.Tests.Titres.Where(t => Orchestrateur.IdentifiantFrontiere(t) is not null).ToList();
            if (frontieres.Count > 0)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"Tests de frontière ({frontieres.Count}) :").AppendLine();
                foreach (var titre in frontieres)
                {
                    texte.AppendLine(CultureInfo.InvariantCulture, $"- {Orchestrateur.AvecCitation(demande.Tests, titre)}");
                }

                texte.AppendLine();
            }

            var votes = Orchestrateur.VotesRetenus(demande);
            if (votes.Count > 0)
            {
                texte.AppendLine("Vote des avis indépendants sur l'exemple discriminant de chaque notion :").AppendLine();
                foreach (var vote in votes)
                {
                    texte.AppendLine(CultureInfo.InvariantCulture, $"- {vote}");
                }

                texte.AppendLine();
            }
        }

        if (demande.ContestationTests is { } contestation)
        {
            texte.AppendLine(contestation.ParLUsine ? "## Tests au repère ambigu soumis d'office par l'usine au QA amont" : "## Tests contestés par le Dev").AppendLine();
            foreach (var titre in contestation.Titres)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {titre}{(contestation.Dependants.Contains(titre, StringComparer.Ordinal) ? " (réussit seul sur une instance neuve : dépendant de l'état laissé par les autres tests, selon l'oracle)" : "")}");
            }

            var decision = contestation.Reecrits switch
            {
                true => "tests réécrits, puis verrouillés de nouveau",
                false => "tests maintenus",
                null => "en attente",
            };
            texte.AppendLine();
            texte.AppendLine(CultureInfo.InvariantCulture, $"- **Motif** : {contestation.Motif}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"- **Preuve** : {contestation.Preuve}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"- **Décision du QA amont** : {decision}{(contestation.Decision.Length > 0 ? $". {contestation.Decision}" : "")}");
            texte.AppendLine();
        }

        texte.AppendLine("## Vérifications de l'oracle").AppendLine();
        texte.AppendLine("| # | Résultat | Étape en échec | Tests réussis | Durée |");
        texte.AppendLine("|---|---|---|---|---|");
        for (var i = 0; i < demande.Verifications.Count; i++)
        {
            var v = demande.Verifications[i];
            texte.AppendLine(CultureInfo.InvariantCulture, $"| {i + 1} | {(v.Succes ? "✅ vert" : "❌ rouge")} | {v.EtapeEchec ?? "—"} | {v.Reussis}/{v.Total}{Consignes.DontInstables(v)} | {v.DureeSecondes.ToString("0", CultureInfo.InvariantCulture)} s |");
        }

        texte.AppendLine();
        if (demande.NouveauxTests && demande.Verifications.Count > 0)
        {
            texte.AppendLine(Orchestrateur.VerificationDuLivrableDOrigine(demande.Verifications[0])).AppendLine();
        }

        if (demande.Verifications.LastOrDefault() is { Succes: false } derniere)
        {
            texte.AppendLine("### Derniers échecs").AppendLine().AppendLine("```").AppendLine(Consignes.FormaterResultat(derniere)).AppendLine("```").AppendLine();
        }

        if (demande.Verifications.LastOrDefault() is { Succes: true, Signaux.Count: > 0 } derniereVerte)
        {
            texte.AppendLine("## Écarts relevés par la sonde, non corrigés").AppendLine();
            texte.AppendLine("Non bloquants : le verdict de livraison ne dépend que des tests d'acceptation.").AppendLine();
            foreach (var signal in derniereVerte.Signaux)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {signal.Message}");
            }

            texte.AppendLine();
        }

        if (demande.CommentairesLivraison.Count > 0)
        {
            texte.AppendLine("## Refus de livraison").AppendLine();
            texte.AppendLine(CultureInfo.InvariantCulture, $"{demande.CommentairesLivraison.Count} refus du demandeur :").AppendLine();
            for (var i = 0; i < demande.CommentairesLivraison.Count; i++)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"{i + 1}. {Debut(demande.CommentairesLivraison[i])}");
                foreach (var titre in demande.TestsAjoutesAuxRefus.GetValueOrDefault(i + 1) ?? [])
                {
                    texte.AppendLine(CultureInfo.InvariantCulture, $"   - Test ajouté : {titre}");
                }
            }

            texte.AppendLine();
        }

        if (demande.Evaluation is { } evaluation)
        {
            texte.AppendLine(demande.EvaluationAnterieureAuDernierVert ? "## Évaluation du QA (version précédente, non réévaluée depuis)" : "## Évaluation du QA").AppendLine();
            if (demande.EvaluationAnterieureAuDernierVert)
            {
                texte.AppendLine(Consignes.EvaluationAnterieure).AppendLine();
            }

            texte.AppendLine(evaluation.Synthese).AppendLine();
            if (!string.IsNullOrWhiteSpace(evaluation.NonTeste))
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"**Non testé** : {evaluation.NonTeste}").AppendLine();
            }

            Liste(texte, "Bloquants", [.. evaluation.Bloquants.Select(b => Consignes.EtatBloquant(b, ""))]);
            Liste(texte, "Revérification", [.. Consignes.LignesReverification(evaluation)]);
            Liste(texte, evaluation.TransmiseAuDev ? "Remarques (transmises au Dev comme points secondaires, non revérifiées)" : "Remarques", evaluation.Remarques);
        }

        texte.AppendLine("## Coûts par rôle").AppendLine();
        texte.AppendLine("| Rôle | Modèle | Tours | Tokens entrée | Dont servis par le cache | Tokens sortie | Coût estimé |");
        texte.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var cout in demande.Couts)
        {
            texte.AppendLine(CultureInfo.InvariantCulture,
                $"| {cout.Role} | {cout.Modele} | {Tours(cout.Appels, cout.AppelsAvis)} | {cout.TokensEntree:N0} | {Cache(cout.TokensCache, cout.TokensEntree)} | {cout.TokensSortie:N0} | {Cout(cout.Cout, cout.CoutConnu)} |");
        }

        if (demande.Couts.Count > 0)
        {
            var cache = demande.Couts.All(c => c.TokensCache is not null) ? demande.Couts.Sum(c => c.TokensCache) : null;
            var entree = demande.Couts.Sum(c => c.TokensEntree);
            texte.AppendLine(CultureInfo.InvariantCulture,
                $"| **Total** | | {Tours(demande.Couts.Sum(c => c.Appels), demande.Couts.Sum(c => c.AppelsAvis))} | {entree:N0} | {Cache(cache, entree)} | {demande.Couts.Sum(c => c.TokensSortie):N0} | {Cout(demande.CoutTotal, demande.CoutConnu)} |");
        }

        texte.AppendLine();
        texte.AppendLine("## Chronologie").AppendLine();
        for (var i = 0; i < demande.Evenements.Count; i++)
        {
            var evenement = demande.Evenements[i];
            var fois = 1;
            while (i + 1 < demande.Evenements.Count
                && string.Equals(demande.Evenements[i + 1].Acteur, evenement.Acteur, StringComparison.Ordinal)
                && string.Equals(demande.Evenements[i + 1].Message, evenement.Message, StringComparison.Ordinal))
            {
                i++;
                fois++;
            }

            var repetition = fois > 1 ? string.Create(CultureInfo.InvariantCulture, $" ({fois} fois, jusqu'au `{demande.Evenements[i].Date.ToLocalTime():yyyy-MM-dd HH:mm:ss}`)") : "";
            texte.AppendLine(CultureInfo.InvariantCulture, $"- `{evenement.Date.ToLocalTime():yyyy-MM-dd HH:mm:ss}` **{evenement.Acteur}** : {evenement.Message}{repetition}");
        }

        texte.AppendLine();
        texte.AppendLine("## Lancer l'application").AppendLine();
        texte.AppendLine("Le code est dans le dossier `code/` à côté de ce rapport. Dans un environnement Linux (ou le conteneur de l'usine) :").AppendLine();
        texte.AppendLine("```bash");
        texte.AppendLine("./usine-build.sh");
        texte.AppendLine(CultureInfo.InvariantCulture, $"PORT={config.BacASable.Port} ./usine-start.sh");
        texte.AppendLine("```");
        texte.AppendLine();
        var volume = $"usine-app-{demande.Id}";
        texte.AppendLine(CultureInfo.InvariantCulture, $"Avec Docker, depuis le dossier `code/` (dans bash, ou dans PowerShell sous Windows). Le code est recopié à chaque lancement dans le volume Docker `{volume}`, où les données de l'application restent d'un lancement à l'autre ; `docker volume rm {volume}` les efface, et l'usine ne supprime jamais ce volume :").AppendLine();
        texte.AppendLine("```bash");
        texte.AppendLine(CultureInfo.InvariantCulture, $"docker run --rm -it -p {config.BacASable.Port}:{config.BacASable.Port} -v \"${{PWD}}:/src:ro\" -v {volume}:/workspace {config.BacASable.Image} bash -c \"cp -rf /src/. /workspace/ && cd /workspace && ./usine-build.sh && PORT={config.BacASable.Port} ./usine-start.sh\"");
        texte.AppendLine("```");
        return texte.ToString();
    }

    private static void Liste(StringBuilder texte, string titre, IReadOnlyCollection<string> elements)
    {
        if (elements.Count == 0)
        {
            return;
        }

        texte.AppendLine(CultureInfo.InvariantCulture, $"**{titre}** :").AppendLine();
        foreach (var element in elements)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- {element}");
        }

        texte.AppendLine();
    }

    private static string Decision(Demande demande) => demande switch
    {
        { ModeAuto: true, CadrageParDemandeur: true, BesoinValide: true } => "aucune sur la livraison : besoin et backlog validés par le demandeur, tests et livraison automatiques",
        { ModeAuto: true, CadrageParDemandeur: true } => "aucune sur la livraison : backlog validé par le demandeur, tests et livraison automatiques",
        { ModeAuto: true } => "aucune, demande en mode automatique",
        { Statut: StatutDemande.Terminee } => "livraison approuvée",
        { Statut: StatutDemande.Annulee } => "aucune, demande annulée avant la décision sur cette livraison",
        _ => "pas encore prise quand ce rapport a été écrit, à la livraison",
    };

    private static string Debut(string commentaire)
    {
        var lignes = commentaire.Trim().Split('\n', 2);
        var ligne = lignes[0].Trim();
        var debut = ligne.Length > LongueurDebut ? ligne[..LongueurDebut].TrimEnd() : ligne;
        return debut.Length < ligne.Length || lignes.Length > 1 ? $"{debut}…" : debut;
    }

    private static string Jetons(long? nombre) => nombre?.ToString("N0", CultureInfo.InvariantCulture) ?? "inconnu";

    private static string Cache(long? cache, long entree) =>
        cache is { } servis && entree > 0 ? $"{Jetons(servis)} ({(servis * 100.0 / entree).ToString("0", CultureInfo.InvariantCulture)} %)" : Jetons(cache);

    private static string Tours(int appels, int avis) =>
        avis > 0 ? $"{(appels - avis).ToString(CultureInfo.InvariantCulture)} (+ {avis.ToString(CultureInfo.InvariantCulture)} avis du vote)" : appels.ToString(CultureInfo.InvariantCulture);

    private static string Cout(decimal cout, bool connu) => connu ? cout.ToString("0.000", CultureInfo.InvariantCulture) + " $" : "inconnu";

    private static string Duree(TimeSpan duree) =>
        duree.TotalHours >= 1
            ? $"{(int)duree.TotalHours} h {duree.Minutes:00} min"
            : $"{duree.Minutes} min {duree.Seconds:00} s";
}
