using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Usine.Core.Agents;
using Usine.Core.Etat;
using Usine.Core.Orchestration;
using Usine.Core.Services;

namespace Usine.Core.Discussion;

public sealed class RevuesEnAttente
{
    private readonly Lock verrou = new();

    private readonly List<string> identifiants = [];

    private CancellationTokenSource signal = new();

    public CancellationToken Signal
    {
        get
        {
            lock (verrou)
            {
                return signal.Token;
            }
        }
    }

    public void Ajouter(string id)
    {
        CancellationTokenSource aSignaler;
        lock (verrou)
        {
            if (!identifiants.Contains(id, StringComparer.Ordinal))
            {
                identifiants.Add(id);
            }

            aSignaler = signal;
        }

        aSignaler.Cancel();
    }

    public string? Prendre()
    {
        lock (verrou)
        {
            if (identifiants.Count == 0)
            {
                if (signal.IsCancellationRequested)
                {
                    signal = new CancellationTokenSource();
                }

                return null;
            }

            var id = identifiants[0];
            identifiants.RemoveAt(0);
            return id;
        }
    }
}

public sealed partial class RevueGuidee(DepotDemandes depot, RevuesEnAttente revues, IReadOnlyList<OutilDiscussion> outils, IConsoleDiscussion console, InformationsUsine? informations = null)
{
    public const string CommandePasser = "/passer";

    public const string CommandePasserAnglais = "/skip";

    public const string CommandeRevue = "/revue";

    public const string CommandeRevueAnglais = "/review";

    public const string CommandeAnnuler = "/annuler";

    public const string CommandeAnnulerAnglais = "/cancel";

    public const string AnnulerHorsRevue = "Aucune revue en cours : /annuler annule la demande dont la revue est en cours. Pour annuler une autre demande, demande-le à l'assistant.";

    public const string QuestionAccord = "OK ? (Entrée = oui, sinon écris ce qui ne va pas)";

    public const string QuestionReponse = "Ta réponse ? (Entrée = pas de préférence)";

    public const string SansPreference = "Pas de préférence, choisis au mieux.";

    public const string AucuneRevue = "Aucune revue en cours.";

    public const string AucuneAttente = "Aucune demande n'attend de revue.";

    public const string RevueEnCours = "Une revue est en cours : réponds à la question, ou tape /passer pour la mettre de côté.";

    private const string OutilValider = "valider_etape";

    private const string OutilRepondre = "repondre_questions";

    private const string OutilAnnuler = "annuler_demande";

    public CancellationToken Signal => revues.Signal;

    public static bool AttendLeDemandeur(Demande demande) =>
        demande.Statut == StatutDemande.AttenteReponses
        || (demande.Statut == StatutDemande.AttenteValidation && ServiceDemandes.EtapeAValider(demande.Etape) is not null);

    public static bool EstPasser(string ligne) => ligne.Trim() is CommandePasser or CommandePasserAnglais;

    public static bool EstRevue(string ligne) => ligne.Trim() is CommandeRevue or CommandeRevueAnglais;

    public static bool EstAnnuler(string ligne) => ligne.Trim() is CommandeAnnuler or CommandeAnnulerAnglais;

    public static string CommandeInconnue(string ligne) =>
        $"Commande inconnue : {ligne.Trim()}. Commandes : {CommandeAnnuler} (ou {CommandeAnnulerAnglais}), {CommandePasser} (ou {CommandePasserAnglais}), {CommandeRevue} (ou {CommandeRevueAnglais}), {BoucleDiscussion.CommandeQuitter} (ou {BoucleDiscussion.CommandeQuitterAnglais}).";

    public static bool EstCommande(string ligne) => MotDeCommande().IsMatch(ligne.Trim());

    public static bool EstCommandeInconnue(string ligne) =>
        EstCommande(ligne) && !EstAnnuler(ligne) && !EstPasser(ligne) && !EstRevue(ligne) && !BoucleDiscussion.EstQuitter(ligne);

    public static string FormaterReponses(IEnumerable<(string Reference, string Texte)> reponses) =>
        string.Join("\n", reponses.Select(r => $"{r.Reference}. {r.Texte}"));

    public static string FormaterRemarques(IEnumerable<(string Reference, string Texte)> remarques) =>
        string.Join("\n", remarques.Select(r => $"{r.Reference} : {r.Texte}"));

    public bool TraiterCommande(string ligne)
    {
        if (EstRevue(ligne))
        {
            Relancer();
            return true;
        }

        if (EstPasser(ligne))
        {
            console.Ecrire(AucuneRevue);
            return true;
        }

        if (EstAnnuler(ligne))
        {
            console.Ecrire(AnnulerHorsRevue);
            return true;
        }

        if (EstCommandeInconnue(ligne))
        {
            console.Ecrire(CommandeInconnue(ligne));
            return true;
        }

        return false;
    }

    public async Task<bool> ConduireAsync(CancellationToken ct)
    {
        while (revues.Prendre() is { } id)
        {
            if (!await ConduireAsync(id, ct))
            {
                return false;
            }
        }

        return true;
    }

    private void Relancer()
    {
        var enAttente = FilEnDirect.Identifiants(depot).Where(id => FilEnDirect.Charger(depot, id) is { } demande && AttendLeDemandeur(demande)).ToList();
        if (enAttente.Count == 0)
        {
            console.Ecrire(AucuneAttente);
            return;
        }

        foreach (var id in enAttente)
        {
            revues.Ajouter(id);
        }
    }

    private async Task<bool> ConduireAsync(string id, CancellationToken ct)
    {
        if (FilEnDirect.Charger(depot, id) is not { } demande || !AttendLeDemandeur(demande))
        {
            return true;
        }

        var cle = (demande.Statut, demande.Etape);
        var questions = demande.Statut == StatutDemande.AttenteReponses;
        var (entete, pas) = Preparer(demande);
        foreach (var ligne in entete)
        {
            console.Ecrire(ligne);
        }

        var reponses = new List<(string Reference, string Texte)>();
        foreach (var etape in pas)
        {
            foreach (var ligne in etape.Lignes)
            {
                console.Ecrire(ligne);
            }

            var reponse = await LireReponseAsync(questions ? QuestionReponse : QuestionAccord, ct);
            if (reponse is null || BoucleDiscussion.EstQuitter(reponse))
            {
                return false;
            }

            if (EstPasser(reponse))
            {
                console.Ecrire(MiseDeCote(id));
                return true;
            }

            if (EstAnnuler(reponse))
            {
                return await AppelerAsync(id, cle, OutilAnnuler, new() { ["id"] = id }, ct);
            }

            if (Sortie(id, cle) is { } arret)
            {
                console.Ecrire(arret);
                return true;
            }

            if (questions)
            {
                reponses.Add((etape.Reference, reponse.Length == 0 ? SansPreference : reponse));
            }
            else if (!EstAccord(reponse))
            {
                reponses.Add((etape.Reference, reponse));
            }
        }

        if (pas.Count == 0)
        {
            return true;
        }

        if (questions)
        {
            console.Ecrire("Récapitulatif de tes réponses :");
            foreach (var (reference, texte) in reponses)
            {
                console.Ecrire($"  {reference}. {texte}");
            }

            return await AppelerAsync(id, cle, OutilRepondre, new() { ["id"] = id, ["reponses"] = FormaterReponses(reponses) }, ct);
        }

        if (reponses.Count == 0)
        {
            console.Ecrire("Récapitulatif : tout est validé.");
            return await AppelerAsync(id, cle, OutilValider, new() { ["id"] = id, ["etape"] = ServiceDemandes.EtapeAValider(demande.Etape), ["approuver"] = true }, ct);
        }

        console.Ecrire($"Récapitulatif : {reponses.Count} remarque{(reponses.Count > 1 ? "s" : "")} :");
        foreach (var (reference, texte) in reponses)
        {
            console.Ecrire($"  {reference} : {texte}");
        }

        return await AppelerAsync(id, cle, OutilValider, new()
        {
            ["id"] = id,
            ["etape"] = ServiceDemandes.EtapeAValider(demande.Etape),
            ["approuver"] = false,
            ["commentaire"] = FormaterRemarques(reponses),
        }, ct);
    }

    private static string MiseDeCote(string id) =>
        $"Revue de la demande {FilEnDirect.Suffixe(id)} mise de côté : elle reste en attente. Tape {CommandeRevue} pour la reprendre.";

    private async Task<string?> LireReponseAsync(string question, CancellationToken ct)
    {
        while (true)
        {
            console.Ecrire(question);
            var reponse = (await console.LireAsync(ct))?.Trim();
            if (reponse is null || !(EstRevue(reponse) || EstCommandeInconnue(reponse)))
            {
                return reponse;
            }

            console.Ecrire(EstRevue(reponse) ? RevueEnCours : CommandeInconnue(reponse));
        }
    }

    private async Task<string?> LireConfirmationAsync(string question, CancellationToken ct)
    {
        while (true)
        {
            var reponse = await console.LireConfirmationAsync(question, ct);
            if (reponse is null || !(EstRevue(reponse) || EstCommandeInconnue(reponse)))
            {
                return reponse;
            }

            console.Ecrire(EstRevue(reponse) ? RevueEnCours : CommandeInconnue(reponse));
        }
    }

    private async Task<bool> AppelerAsync(string id, (StatutDemande, EtapeDemande) cle, string nom, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        if (outils.FirstOrDefault(o => o.Fonction.Name == nom) is not { } outil)
        {
            console.Ecrire($"Outil {nom} indisponible : rien n'est envoyé, la demande {FilEnDirect.Suffixe(id)} reste en attente.");
            return true;
        }

        var appel = new FunctionCallContent(Guid.NewGuid().ToString("N"), nom, arguments);
        var resume = $"{nom}({ExecuteurAgent.ResumerArguments(appel)})";
        if (outil.Destructif)
        {
            var reponse = await LireConfirmationAsync($"La revue va appeler {nom}({ExecuteurAgent.ArgumentsComplets(appel)}). Autoriser ?", ct);
            if (!BoucleDiscussion.EstOui(reponse) && !(nom == OutilAnnuler && reponse is not null && EstAnnuler(reponse)))
            {
                console.Ecrire($"→ {resume} : {BoucleDiscussion.RefusUtilisateur}");
                if (reponse is not null && BoucleDiscussion.EstQuitter(reponse))
                {
                    return false;
                }

                if (reponse is not null && EstAnnuler(reponse))
                {
                    return await AppelerAsync(id, cle, OutilAnnuler, new() { ["id"] = id }, ct);
                }

                console.Ecrire(reponse is not null && EstPasser(reponse)
                    ? MiseDeCote(id)
                    : $"Rien n'est envoyé : la demande {FilEnDirect.Suffixe(id)} reste en attente. Tape {CommandeRevue} pour reprendre sa revue.");
                return true;
            }
        }

        if (Sortie(id, cle) is { } arret)
        {
            console.Ecrire(arret);
            return true;
        }

        string resultat;
        try
        {
            resultat = Convert.ToString(await outil.Fonction.InvokeAsync(new AIFunctionArguments(arguments), ct), CultureInfo.InvariantCulture) ?? "";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            resultat = $"Erreur : {ex.Message}";
        }

        console.Ecrire($"→ {resume} : {ExecuteurAgent.PremiereLigne(resultat)}");
        informations?.Ajouter($"Revue guidée de la demande {id} terminée par l'appel {nom}({ExecuteurAgent.ArgumentsComplets(appel)}), résultat :\n{resultat}");
        return true;
    }

    private string? Sortie(string id, (StatutDemande Statut, EtapeDemande Etape) cle)
    {
        var actuelle = FilEnDirect.Charger(depot, id);
        if (actuelle is not null && (actuelle.Statut, actuelle.Etape) == cle)
        {
            return null;
        }

        var objet = cle.Etape switch
        {
            _ when cle.Statut == StatutDemande.AttenteReponses => "tes réponses",
            EtapeDemande.ValidationBesoin => "la validation du besoin",
            EtapeDemande.ValidationBacklog => "la validation du backlog",
            EtapeDemande.ValidationTests => "la validation des tests",
            _ => "la validation de la livraison",
        };
        var etat = actuelle is null ? "état illisible" : $"{actuelle.Statut}, étape {actuelle.Etape}";
        return $"Revue arrêtée : la demande {FilEnDirect.Suffixe(id)} n'attend plus {objet} ({etat}). Rien n'est envoyé.";
    }

    private static bool EstAccord(string reponse) =>
        reponse.Length == 0
        || reponse.Equals("ok", StringComparison.OrdinalIgnoreCase)
        || reponse.Equals("oui", StringComparison.OrdinalIgnoreCase)
        || reponse.Equals("o", StringComparison.OrdinalIgnoreCase)
        || reponse.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || reponse.Equals("y", StringComparison.OrdinalIgnoreCase);

    private static (IReadOnlyList<string> Entete, IReadOnlyList<Pas> Pas) Preparer(Demande demande)
    {
        var nom = $"Demande {FilEnDirect.Suffixe(demande.Id)} ({demande.Titre})";
        if (demande.Statut == StatutDemande.AttenteReponses)
        {
            var nombre = demande.Questions.Count;
            return (
                [nombre == 1 ? $"{nom} : le PO a 1 question, je te la pose." : $"{nom} : le PO a {nombre} questions, je te les pose une par une."],
                demande.Questions.Select((question, i) => new Pas($"{i + 1}", [$"Question {i + 1}/{nombre} : {question}"])).ToList());
        }

        return demande.Etape switch
        {
            EtapeDemande.ValidationBesoin => demande.Besoin is { } besoin
                ? ([], [new Pas("Besoin", [$"{nom} : besoin compris par le PO, à valider avant qu'il écrive les user stories.", $"{besoin.Titre} : {besoin.Resume}"])])
                : ([$"{nom} : besoin introuvable dans l'état de la demande, pas de revue possible."], []),
            EtapeDemande.ValidationBacklog => demande.Backlog is { } backlog
                ? ([], PasBacklog(nom, backlog, demande.CadrageParDemandeur, avecResume: !demande.BesoinValide))
                : ([$"{nom} : backlog introuvable dans l'état de la demande, pas de revue possible."], []),
            EtapeDemande.ValidationTests => PasTests(nom, demande),
            _ => ([],
            [
                new Pas("Livraison",
                [
                    $"{nom} : livraison à valider.",
                    $"Dernière vérification : {FilEnDirect.DerniereVerification(demande)}.",
                    .. LignesAvis(FormateurDemande.Avis(demande)),
                    .. demande.Livrable is { } livrable ? [$"Livrable : {livrable.RepertoireCode}", .. string.IsNullOrWhiteSpace(livrable.Rapport) ? Array.Empty<string>() : [$"Rapport : {livrable.Rapport}"]] : Array.Empty<string>(),
                ]),
            ]),
        };
    }

    private static IReadOnlyList<string> LignesAvis(AvisQa? avis)
    {
        if (avis is null)
        {
            return [];
        }

        return
        [
            $"{avis.Titre} :",
            .. avis.Mentions.Select(mention => $"  {mention}"),
            .. string.IsNullOrWhiteSpace(avis.Synthese) ? Array.Empty<string>() : [$"  {avis.Synthese}"],
            .. string.IsNullOrWhiteSpace(avis.NonTeste) ? Array.Empty<string>() : [$"  Non testé : {avis.NonTeste}"],
            .. avis.Points.Select(point => $"  - {point}"),
        ];
    }

    private static List<Pas> PasBacklog(string nom, Backlog backlog, bool storiesSeulement, bool avecResume)
    {
        var nombre = backlog.Stories.Count;
        var pas = new List<Pas>
        {
            new("Backlog",
            [
                $"{nom} : backlog à valider.",
                !avecResume || string.IsNullOrWhiteSpace(backlog.Resume) ? backlog.Titre : $"{backlog.Titre} : {backlog.Resume}",
                .. string.IsNullOrWhiteSpace(backlog.Stack.Choix) ? Array.Empty<string>() : [$"Technique : {backlog.Stack.Choix}"],
                nombre == 1 ? "1 story, je te la présente." : $"{nombre} stories, je te les présente une par une.",
            ]),
        };
        pas.AddRange(backlog.Stories.Select((story, i) => new Pas(story.Id,
        [
            $"Story {i + 1}/{nombre} — {story.Id} : {story.Titre}",
            .. string.IsNullOrWhiteSpace(story.Description) ? Array.Empty<string>() : [$"  {story.Description}"],
            .. story.Criteres.Select(critere => $"  - {critere.Id} : {SansCalcul().Replace(critere.Texte, "")}"),
        ])));
        if (storiesSeulement)
        {
            return pas;
        }

        if (backlog.HorsPerimetre.Count > 0)
        {
            pas.Add(new Pas("Hors périmètre", ["Hors périmètre :", .. backlog.HorsPerimetre.Select(element => $"  - {element}")]));
        }

        if (backlog.Hypotheses.Count > 0)
        {
            pas.Add(new Pas("Hypothèses", ["Hypothèses :", .. backlog.Hypotheses.Select(element => $"  - {element}")]));
        }

        return pas;
    }

    private static (IReadOnlyList<string> Entete, IReadOnlyList<Pas> Pas) PasTests(string nom, Demande demande)
    {
        var titres = demande.Tests?.Titres ?? [];
        var stories = demande.Backlog?.Stories ?? [];
        var groupes = new List<(string Story, List<string> Titres)>();
        foreach (var story in stories)
        {
            Groupe(groupes, story.Id);
        }

        var frontieres = new List<string>();
        var sansStory = new List<string>();
        foreach (var titre in titres)
        {
            if (Orchestrateur.IdentifiantFrontiere(titre) is not null)
            {
                frontieres.Add(Orchestrateur.AvecCitation(demande.Tests!, titre));
                if (PrefixeStory().Match(titre) is { Success: true } prefixeFrontiere)
                {
                    Groupe(groupes, prefixeFrontiere.Groups["story"].Value).Add(titre);
                }
            }
            else if (PrefixeStory().Match(titre) is { Success: true } prefixe)
            {
                Groupe(groupes, prefixe.Groups["story"].Value).Add(titre);
            }
            else
            {
                sansStory.Add(titre);
            }
        }

        var pas = groupes.Select((groupe, i) =>
        {
            var titreStory = stories.FirstOrDefault(s => s.Id == groupe.Story)?.Titre;
            return new Pas(groupe.Story,
            [
                $"Story {i + 1}/{groupes.Count} — {groupe.Story}{(string.IsNullOrWhiteSpace(titreStory) ? "" : $" : {titreStory}")}",
                .. groupe.Titres.Count == 0 ? ["  Aucun test pour cette story."] : groupe.Titres.Select(titre => $"  - {titre}"),
            ]);
        }).ToList();
        if (frontieres.Count > 0)
        {
            pas.Add(new Pas("Tests de frontière", ["Tests de frontière :", .. frontieres.Select(titre => $"  - {titre}")]));
        }

        if (sansStory.Count > 0)
        {
            pas.Add(new Pas("Tests sans story", ["Tests sans story :", .. sansStory.Select(titre => $"  - {titre}")]));
        }

        if (pas.Count == 0)
        {
            pas.Add(new Pas("Tests", ["Aucun test d'acceptation."]));
        }

        List<string> entete = [$"{nom} : tests à valider ({titres.Count} au total), story par story."];
        if (demande.Tests?.ExecutionInitiale is { } initiale)
        {
            entete.Add(NonRegression.AvantToutCode(initiale, titres));
        }

        return (entete, pas);
    }

    private static List<string> Groupe(List<(string Story, List<string> Titres)> groupes, string story)
    {
        foreach (var groupe in groupes)
        {
            if (groupe.Story == story)
            {
                return groupe.Titres;
            }
        }

        groupes.Add((story, []));
        return groupes[^1].Titres;
    }

    [GeneratedRegex(@"\[(?<story>S\d+)-")]
    private static partial Regex PrefixeStory();

    [GeneratedRegex(@"^/[\p{L}-]*$")]
    private static partial Regex MotDeCommande();

    [GeneratedRegex(@"\s*\(\s*calculs?\s*:(?>[^()]+|\((?<p>)|\)(?<-p>))*(?(p)(?!))\)", RegexOptions.IgnoreCase)]
    internal static partial Regex SansCalcul();

    private sealed record Pas(string Reference, IReadOnlyList<string> Lignes);
}
