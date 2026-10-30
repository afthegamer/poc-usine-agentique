using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Usine.Core.BacASable;
using Usine.Core.Configuration;
using Usine.Core.Modeles;

namespace Usine.Core.Agents;

public sealed class SignalFin
{
    public bool Termine { get; private set; }

    public string? Outil { get; private set; }

    public void Terminer(string outil)
    {
        Termine = true;
        Outil = outil;
    }
}

public sealed class DefinitionAgent
{
    public required RoleAgent Role { get; init; }

    public required IReadOnlyList<ReferenceModele> Modeles { get; init; }

    public required string PromptSysteme { get; init; }

    public required string Message { get; init; }

    public required IReadOnlyList<AIFunction> Outils { get; init; }

    public required IReadOnlyCollection<string> OutilsTerminaux { get; init; }

    public required SignalFin Fin { get; init; }

    public string? Libelle { get; init; }

    public int? MaxIterations { get; init; }
}

public sealed class ContexteAgent
{
    public required decimal? BudgetRestant { get; init; }

    public required Func<bool> AnnulationDemandee { get; init; }

    public required Action<string> Journal { get; init; }

    public Action<string>? Signaler { get; init; }

    public Action<Consommation>? Consommer { get; init; }

    public int ToursDejaConsommes { get; init; }

    public string? CheminTranscription { get; init; }

    public string? CheminSorties { get; init; }
}

public sealed class HistoriqueVisible
{
    public int Tour { get; set; }

    public int PremierTourVisible { get; set; }

    public int ToursRetires { get; set; }

    public List<string> FichiersEcrits { get; } = [];

    internal (TextContent Mention, string TexteSansFichiers)? MentionDesFichiers { get; set; }

    public void NoterEcriture(string chemin)
    {
        FichiersEcrits.Remove(chemin);
        FichiersEcrits.Add(chemin);
    }
}

public sealed record Consommation(ReferenceModele Modele, long TokensEntree, long TokensSortie, decimal Cout, bool CoutConnu, long? TokensCache = null);

public sealed record ResultatAgent(
    bool Termine,
    int Iterations,
    long TokensEntree,
    long TokensSortie,
    decimal Cout,
    bool CoutConnu,
    bool BudgetEpuise,
    string? DernierTexte,
    string Modele,
    string? MotifArret,
    bool ContexteDepasse = false);

public interface IExecuteurAgent
{
    Task<ResultatAgent> ExecuterAsync(DefinitionAgent definition, ContexteAgent contexte, CancellationToken ct);
}

public sealed partial class ExecuteurAgent(IFabriqueClientsChat fabrique, ConfigUsine config) : IExecuteurAgent
{
    private const int MaxRelances = 3;

    private const int MaxRelancesAuTotal = 6;

    private const int MaxRefusIdentiques = 3;

    private const int MaxRefusAuTotal = 5;

    private const int RepetitionsAvantRappel = 3;

    private const int MaxRepetitions = 6;

    private const int PeriodeMaxCycle = 3;

    private const int MaxDelaisDepasses = 1;

    private const int MaxReponsesIllisibles = 1;

    private const int RefusOutilAvantRappel = 3;

    private const int DiviseurToursRestantsAvantRappelFinDeTours = 3;

    private const int DiviseurToursRestantsAvantRappelFinDeToursQa = 6;

    private const int ToursRestantsMinAvantRappelFinDeToursQa = 10;

    private const int NumerateurSeuilElagage = 3;

    private const int DenominateurSeuilElagage = 4;

    private const int ToursGardesIntacts = 4;

    private const int CaracteresParTokenParDefaut = 3;

    private const int MaxTokensParCaractere = 1;

    private const int TailleMaxSortieConservee = 20000;

    private const int TailleMaxRefusRappele = 1500;

    private const int MaxFichiersRappeles = 30;

    internal const string ConsigneApresRefusEvaluation = "Fais-le maintenant : corrige ou retire chaque élément refusé (un essai sans trace peut passer dans nonTeste), puis rappelle tout de suite soumettre_evaluation. Sans évaluation acceptée avant la fin de tes tours, tout ton travail est perdu.";

    internal const string SortieRetiree = "[sortie retirée pour faire de la place : relis le fichier ou relance la commande si tu en as besoin]";

    internal const string ContenuRetire = "[contenu retiré pour faire de la place : relis le fichier]";

    private const string PrefixeRefus = "Refusé";

    private const string PrefixeErreur = "Erreur";

    private const string ConsigneReprise =
        "\n\n---\nUn autre modèle a commencé cette tâche puis s'est interrompu. Ce qu'il a déjà écrit est conservé : "
        + "regarde d'abord l'état actuel des fichiers, puis poursuis sans tout refaire.";

    internal const string ConsigneReformulation =
        "Ta dernière réponse n'a pas pu être lue : l'appel d'outil était mal formé. Recommence en appelant un seul outil à la fois, avec des arguments JSON simples ; "
        + "si un contenu de fichier contient des balises comme <function> ou <parameter>, écris-le par executer_commande avec un heredoc ou en plusieurs morceaux.";

    private static readonly TimeSpan[] AttentesReessai =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(120),
    ];

    private static readonly string[] ErreursDefinitives =
    [
        "api key not valid", "api_key_invalid", "invalid api key", "permission", "unauthori", "forbidden",
        "not found", "no longer available", "does not exist", "invalid argument", "invalid_argument",
        "perday", "per day",
    ];

    private static readonly string[] ErreursDeContexte =
    [
        "no user query found in messages", "context length", "context_length_exceeded", "maximum context",
        "prompt is too long", "too many tokens", "input is too long", "exceeds the maximum number of tokens",
        "exceeds the available context size",
    ];

    private static readonly string[] ErreursDAppelDOutilIllisible =
    [
        "tool call parsing failed", "xml syntax error", "failed to parse tool call", "invalid tool call", "malformed tool call",
    ];

    private static readonly Dictionary<string, string[]> ArgumentsDeContenu = new(StringComparer.Ordinal)
    {
        ["ecrire_fichier"] = ["contenu"],
        ["remplacer_dans_fichier"] = ["ancien", "nouveau"],
    };

    private static readonly Dictionary<string, DateTimeOffset> DerniersAppels = new(StringComparer.OrdinalIgnoreCase);

    private static readonly SemaphoreSlim VerrouCadence = new(1, 1);

    private static readonly Dictionary<string, List<(DateTimeOffset Instant, long Tokens)>> EnvoisParModele = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan[] Attentes { get; init; } = AttentesReessai;

    public TimeSpan FenetreQuota { get; init; } = TimeSpan.FromMinutes(1);

    public async Task<ResultatAgent> ExecuterAsync(DefinitionAgent definition, ContexteAgent contexte, CancellationToken ct)
    {
        var candidats = definition.Modeles;
        var libelle = LibelleDe(definition);
        var maxTours = definition.MaxIterations ?? config.Limites.MaxIterationsAgent;
        var indexModele = 0;
        IChatClient CreerClient(string designation)
        {
            while (true)
            {
                try
                {
                    return fabrique.Creer(candidats[indexModele], definition.Role);
                }
                catch (Exception erreur) when (indexModele < candidats.Count - 1)
                {
                    indexModele++;
                    contexte.Journal($"{libelle} : {designation} {candidats[indexModele - 1]} n'a pas pu être créé ({Decrire(erreur)}) ; bascule sur le modèle de secours {candidats[indexModele]}.");
                    contexte.Signaler?.Invoke($"Bascule sur le modèle de secours {candidats[indexModele]}.");
                }
            }
        }

        var client = CreerClient("le modèle");

        var utilises = new List<string> { candidats[indexModele].ToString() };
        var transcription = new List<ChatMessage>();
        var messages = NouvelleConversation(definition, definition.Message);
        var options = new ChatOptions
        {
            ModelId = candidats[indexModele].Modele,
            Tools = [.. definition.Outils],
            ToolMode = ChatToolMode.Auto,
            MaxOutputTokens = config.MaxTokensSortiePour(candidats[indexModele]),
        };
        var outils = definition.Outils.ToDictionary(o => o.Name, StringComparer.Ordinal);
        long tokensEntree = 0;
        long tokensSortie = 0;
        decimal cout = 0;
        var coutConnu = true;
        var relances = 0;
        var reponsesSansOutil = 0;
        var iterations = contexte.ToursDejaConsommes;
        var budgetEpuise = false;
        string? dernierTexte = null;
        string? dernierRefus = null;
        var refusIdentiques = 0;
        var refusVus = new Dictionary<string, int>();
        var refusOutils = new Dictionary<(string Outil, string Refus), int>();
        var signatures = new List<string>();
        var nomsDesTours = new List<string>();
        var rappelDonne = false;
        var rappelFinDeToursDonne = false;
        var terminalAppele = false;
        string? dernierRefusTerminal = null;
        var sansConsommation = new HashSet<ReferenceModele>();
        var elagueAuDelaDeLaLimite = false;
        var elagueDeForceAuDelaDeLaLimite = false;
        var debordementElague = false;
        var reformulationDemandee = false;
        long tokensDernierAppel = 0;
        long caracteresDernierAppel = 0;
        var historique = new HistoriqueVisible();
        string? motifArret = null;
        var contexteDepasse = false;
        void Arreter(string motif)
        {
            motifArret = motif;
            contexte.Journal($"{libelle} : arrêt, {motifArret}.");
        }

        try
        {
            while (iterations < maxTours)
            {
                EcrireTranscriptionEnCours(contexte, definition, string.Join(" → ", utilises), [.. transcription, .. messages]);
                ct.ThrowIfCancellationRequested();
                if (contexte.AnnulationDemandee())
                {
                    throw new OperationCanceledException("Annulation demandée.");
                }

                if (config.MaxTokensContextePour(candidats[indexModele]) > 0 && caracteresDernierAppel > 0 && MotifContexte(candidats[indexModele], EstimerContexte(messages, tokensDernierAppel, caracteresDernierAppel), messages, historique, contexte, libelle, tokensDernierAppel, caracteresDernierAppel, ref elagueAuDelaDeLaLimite, ref elagueDeForceAuDelaDeLaLimite, estime: true) is { } motifContexteEstime)
                {
                    Arreter(motifContexteEstime);
                    contexteDepasse = true;
                    break;
                }

                ChatResponse reponse;
                TimeSpan dureeAppel;
                try
                {
                    (reponse, dureeAppel) = await AppelerAvecReessaisAsync(client, candidats[indexModele], indexModele < candidats.Count - 1, EstimerContexte(messages, tokensDernierAppel, caracteresDernierAppel), messages, options, contexte, libelle, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && !contexte.AnnulationDemandee() && EstDebordementContexte(ex))
                {
                    if (!debordementElague && ElaguerDeForce(messages, historique) is var (retires, contenus) && retires + contenus > 0)
                    {
                        debordementElague = true;
                        contexte.Journal($"{libelle} : contexte au-delà de la fenêtre du modèle ({Decrire(ex)}) ; élagage forcé du contexte, {DecrireElagage(retires, contenus)}, nouvel essai immédiat.");
                        contexte.Signaler?.Invoke($"Le contexte dépasse la fenêtre du modèle {candidats[indexModele]} ({Decrire(ex)}) : élagage forcé et nouvel essai immédiat.");
                        continue;
                    }

                    Arreter($"contexte au-delà de la fenêtre du modèle ({Decrire(ex)})");
                    contexteDepasse = true;
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && !contexte.AnnulationDemandee() && EstAppelDOutilIllisible(ex))
                {
                    if (!reformulationDemandee)
                    {
                        reformulationDemandee = true;
                        contexte.Journal($"{libelle} : réponse du modèle illisible pour le fournisseur ({Decrire(ex)}) ; message de reformulation ajouté, nouvel essai immédiat.");
                        contexte.Signaler?.Invoke($"Réponse du modèle {candidats[indexModele]} illisible pour le fournisseur ({Decrire(ex)}) : reformulation demandée et nouvel essai immédiat.");
                        messages.Add(new ChatMessage(ChatRole.User, ConsigneReformulation));
                        continue;
                    }

                    Arreter($"réponse du modèle illisible pour le fournisseur ({Decrire(ex)})");
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && !contexte.AnnulationDemandee() && indexModele < candidats.Count - 1)
                {
                    indexModele++;
                    if (ex is AppelAuDelaDuQuotaException)
                    {
                        contexte.Journal($"{libelle} : {ex.Message} ; bascule sur le modèle de secours {candidats[indexModele]}.");
                        contexte.Signaler?.Invoke($"Bascule sur le modèle de secours {candidats[indexModele]} : {ex.Message}.");
                    }
                    else
                    {
                        contexte.Journal($"{libelle} : le modèle {candidats[indexModele - 1]} ne répond pas ({Decrire(ex)}) ; bascule sur le modèle de secours {candidats[indexModele]}.");
                        contexte.Signaler?.Invoke($"Bascule sur le modèle de secours {candidats[indexModele]}.");
                    }

                    var secours = CreerClient("le modèle de secours");
                    client.Dispose();
                    client = secours;
                    options.ModelId = candidats[indexModele].Modele;
                    options.MaxOutputTokens = config.MaxTokensSortiePour(candidats[indexModele]);
                    utilises.Add(candidats[indexModele].ToString());
                    if (messages.Count > 2)
                    {
                        var aDesOutilsDeTravail = definition.Outils.Any(o => !definition.OutilsTerminaux.Contains(o.Name));
                        transcription.AddRange(messages);
                        transcription.Add(new ChatMessage(ChatRole.System, $"Bascule sur {candidats[indexModele]} : nouvelle conversation, le travail déjà écrit est conservé."));
                        messages = NouvelleConversation(definition, aDesOutilsDeTravail ? definition.Message + ConsigneReprise : definition.Message);
                        historique = new HistoriqueVisible();
                        tokensDernierAppel = 0;
                        caracteresDernierAppel = 0;
                        dernierRefus = null;
                        refusIdentiques = 0;
                        refusVus.Clear();
                        refusOutils.Clear();
                        signatures.Clear();
                        nomsDesTours.Clear();
                        rappelDonne = false;
                        debordementElague = false;
                        reformulationDemandee = false;
                        relances = 0;
                        reponsesSansOutil = 0;
                    }

                    continue;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && !contexte.AnnulationDemandee())
                {
                    contexte.Journal($"{libelle} : le modèle {candidats[indexModele]} ne répond pas ({Decrire(ex)}) ; aucun modèle de secours ne reste.");
                    throw;
                }

                iterations++;
                var tarif = config.TarifPour(candidats[indexModele]);
                var tokensAppelEntree = reponse.Usage?.InputTokenCount ?? 0;
                var tokensAppelSortie = SortieFacturee(reponse.Usage, candidats[indexModele]);
                var coutAppel = CoutExact(tarif, tokensAppelEntree, reponse.Usage?.CachedInputTokenCount, tokensAppelSortie);
                var coutAppelConnu = tarif is not null && reponse.Usage is not null;
                if (reponse.Usage is null && sansConsommation.Add(candidats[indexModele]))
                {
                    contexte.Journal($"{libelle} : le modèle {candidats[indexModele]} ne renvoie pas sa consommation (« usage » absent de sa réponse) ; ses tokens et son coût ne sont pas comptés{(contexte.BudgetRestant is null ? "" : ", et le plafond de coût ne s'applique pas à lui")}.");
                }

                tokensEntree += tokensAppelEntree;
                tokensSortie += tokensAppelSortie;
                coutConnu &= coutAppelConnu;
                cout += coutAppel;
                contexte.Consommer?.Invoke(new Consommation(candidats[indexModele], tokensAppelEntree, tokensAppelSortie, coutAppel, coutAppelConnu, reponse.Usage?.CachedInputTokenCount));
                debordementElague = false;
                reformulationDemandee = false;
                tokensDernierAppel = tokensAppelEntree;
                caracteresDernierAppel = Caracteres(messages);
                contexte.Journal($"{libelle} : appel {iterations.ToString(CultureInfo.InvariantCulture)} ({candidats[indexModele]}) : entrée {Jetons(reponse.Usage?.InputTokenCount)} (cache {Jetons(reponse.Usage?.CachedInputTokenCount)}), sortie {Jetons(reponse.Usage?.OutputTokenCount is null ? null : tokensAppelSortie)} (réflexion {Jetons(reponse.Usage?.ReasoningTokenCount)}), {dureeAppel.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s, {caracteresDernierAppel.ToString(CultureInfo.InvariantCulture)} caractères envoyés.");
                if (EstGemini(candidats[indexModele]))
                {
                    foreach (var message in reponse.Messages)
                    {
                        message.Contents = [.. message.Contents.Where(c => c is not TextReasoningContent)];
                    }
                }

                messages.AddRange(reponse.Messages.Where(m => m.Contents.Count > 0));

                var texte = string.Join("\n", reponse.Messages.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text)).Trim();
                if (texte.Length > 0)
                {
                    dernierTexte = texte;
                }

                var appels = reponse.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
                if (appels.Count == 0)
                {
                    if (BudgetAtteint(cout, contexte, libelle))
                    {
                        budgetEpuise = true;
                        break;
                    }

                    if (MotifContexte(candidats[indexModele], tokensAppelEntree, messages, historique, contexte, libelle, tokensDernierAppel, caracteresDernierAppel, ref elagueAuDelaDeLaLimite, ref elagueDeForceAuDelaDeLaLimite) is { } motifContexteSansOutil)
                    {
                        Arreter(motifContexteSansOutil);
                        contexteDepasse = true;
                        break;
                    }

                    if (++relances > MaxRelances)
                    {
                        Arreter($"aucun outil appelé après {MaxRelances} relances");
                        break;
                    }

                    if (++reponsesSansOutil > MaxRelancesAuTotal)
                    {
                        Arreter($"{reponsesSansOutil.ToString(CultureInfo.InvariantCulture)} réponses sans appel d'outil au total, malgré {MaxRelancesAuTotal.ToString(CultureInfo.InvariantCulture)} relances");
                        break;
                    }

                    var consigne = reponse.FinishReason == ChatFinishReason.Length
                        ? "Ta réponse a été coupée (trop longue). Découpe ton travail en plus petites étapes et continue avec les outils."
                        : reponse.FinishReason == ChatFinishReason.ToolCalls
                            ? "Ton appel d'outil était mal formé et n'a pas pu être lu. Réessaie avec des arguments plus courts (par exemple, écris un gros fichier en plusieurs fois)."
                            : $"Tu n'as appelé aucun outil. Continue le travail avec les outils et termine obligatoirement en appelant {string.Join(" ou ", definition.OutilsTerminaux)}.";
                    messages.Add(new ChatMessage(ChatRole.User, consigne));
                    continue;
                }

                relances = 0;
                var resultats = new List<AIContent>();
                var refusDuTour = new List<string?>();
                var refusOutilsDuTour = new List<(string Outil, string Refus)>();
                var appelsDuTour = new List<string>();
                historique.Tour = messages.Count(m => m.Role == ChatRole.Tool) + historique.ToursRetires;
                foreach (var appel in appels)
                {
                    if (definition.Fin.Termine)
                    {
                        FunctionResultContent ignore = new(appel.CallId, "Ignoré : ton travail est déjà terminé.");
                        resultats.Add(ignore);
                        continue;
                    }

                    var sortie = await InvoquerAsync(appel, outils, historique, ct);
                    ConserverSortie(contexte, definition, iterations, appel, sortie);
                    var terminal = definition.OutilsTerminaux.Contains(appel.Name);
                    terminalAppele |= terminal;
                    if (terminal)
                    {
                        dernierRefusTerminal = definition.Fin.Termine ? null : sortie.Trim();
                    }

                    contexte.Journal($"{libelle} → {appel.Name}({ResumerArguments(appel)}) : {(terminal ? sortie.Trim().ReplaceLineEndings() : PremiereLigne(sortie))}");
                    FunctionResultContent resultat = new(appel.CallId, sortie);
                    resultats.Add(resultat);
                    refusDuTour.Add(terminal ? $"{appel.Name} : « {sortie.Trim()} »" : null);
                    appelsDuTour.Add($"{appel.Name}|{JsonSerializer.Serialize(Ordonner(appel.Arguments), Json.Options)}|{PremiereLigne(sortie)}");
                    if (!terminal && config.Limites.MaxRefusOutil > 0)
                    {
                        var ligne = PremiereLigne(sortie);
                        if (ligne.StartsWith(PrefixeRefus, StringComparison.Ordinal))
                        {
                            refusOutilsDuTour.Add((appel.Name, ligne));
                        }
                        else if (!ligne.StartsWith(PrefixeErreur, StringComparison.Ordinal))
                        {
                            refusOutilsDuTour.RemoveAll(c => c.Outil == appel.Name);
                            foreach (var cle in refusOutils.Keys.Where(c => c.Outil == appel.Name).ToList())
                            {
                                refusOutils.Remove(cle);
                            }
                        }
                    }
                }

                messages.Add(new ChatMessage(ChatRole.Tool, resultats));
                if (definition.Fin.Termine)
                {
                    break;
                }

                var refus = refusDuTour.Distinct().ToList() is [{ } unique] ? unique : null;
                refusIdentiques = refus is null ? 0 : refus == dernierRefus ? refusIdentiques + 1 : 1;
                dernierRefus = refus;
                if (refus is not null && refusIdentiques >= MaxRefusIdentiques)
                {
                    Arreter($"{MaxRefusIdentiques} refus identiques de suite pour {refus.ReplaceLineEndings(" ")}");
                    break;
                }

                if (refusDuTour.OfType<string>().Distinct().ToList() is [var refusTerminal])
                {
                    refusVus[refusTerminal] = refusVus.GetValueOrDefault(refusTerminal) + 1;
                    if (refusVus[refusTerminal] >= MaxRefusAuTotal)
                    {
                        Arreter($"{MaxRefusAuTotal} refus identiques au total pour {refusTerminal.ReplaceLineEndings(" ")}");
                        break;
                    }
                }

                var rappelsRefus = new List<string>();
                foreach (var cle in refusOutilsDuTour.Distinct())
                {
                    var total = refusOutils.GetValueOrDefault(cle) + 1;
                    refusOutils[cle] = total;
                    if (total >= config.Limites.MaxRefusOutil)
                    {
                        Arreter(DecrireRefus(total, cle));
                        break;
                    }

                    if (total == RefusOutilAvantRappel)
                    {
                        contexte.Journal($"{libelle} : rappel, {DecrireRefus(total, cle)}.");
                        rappelsRefus.Add($"Tu as reçu au moins {RefusOutilAvantRappel.ToString(CultureInfo.InvariantCulture)} fois le même refus de l'outil {cle.Outil}, et ces appels n'ont rien modifié :\n{cle.Refus}\nNe renvoie pas cet appel tel quel : fais ce que demande ce refus, ou utilise un autre outil. Encore {(config.Limites.MaxRefusOutil - RefusOutilAvantRappel).ToString(CultureInfo.InvariantCulture)} tour(s) avec ce refus, et ton travail sera arrêté.");
                    }
                }

                if (motifArret is not null)
                {
                    break;
                }

                if (rappelsRefus.Count > 0)
                {
                    messages.Add(new ChatMessage(ChatRole.User, string.Join("\n", rappelsRefus)));
                }

                signatures.Add(string.Join("\u001f", appelsDuTour));
                nomsDesTours.Add(string.Join("+", appels.Select(a => a.Name)));
                var (periode, repetitions) = Cycle(signatures);
                if (repetitions < 2)
                {
                    rappelDonne = false;
                }

                if (repetitions >= MaxRepetitions)
                {
                    Arreter($"{DecrireCycle(MaxRepetitions, periode, nomsDesTours)}, sans changement de résultat");
                    break;
                }

                if (repetitions >= RepetitionsAvantRappel && !rappelDonne)
                {
                    rappelDonne = true;
                    if (rappelsRefus.Count == 0)
                    {
                        contexte.Journal($"{libelle} : rappel, {DecrireCycle(RepetitionsAvantRappel, periode, nomsDesTours)}.");
                        messages.Add(new ChatMessage(ChatRole.User, $"{DecrireRappel(periode, nomsDesTours)} avec les mêmes arguments, et le résultat n'a pas changé. N'insiste pas : avance, et termine obligatoirement en appelant {string.Join(" ou ", definition.OutilsTerminaux)}."));
                    }
                }

                var toursRestants = maxTours - iterations;
                var terminaux = string.Join(" ou ", definition.OutilsTerminaux);
                if (!rappelFinDeToursDonne && toursRestants > 0 && toursRestants <= ToursRestantsAuRappel(definition.Role, maxTours) && ConsigneFinDeTours(definition.Role, terminaux, dernierRefusTerminal) is { } consigneFinDeTours)
                {
                    rappelFinDeToursDonne = true;
                    contexte.Journal($"{libelle} : rappel, {iterations.ToString(CultureInfo.InvariantCulture)} tours sur {maxTours.ToString(CultureInfo.InvariantCulture)} sans {(terminalAppele ? $"que {terminaux} ait abouti" : terminaux)}.");
                    messages.Add(new ChatMessage(ChatRole.User, $"Il ne te reste que {toursRestants.ToString(CultureInfo.InvariantCulture)} tour(s) sur {maxTours.ToString(CultureInfo.InvariantCulture)}, {(terminalAppele ? $"et {terminaux} n'a pas encore abouti" : $"et tu n'as pas encore appelé {terminaux}")}. {consigneFinDeTours}"));
                }

                if (MotifContexte(candidats[indexModele], tokensAppelEntree, messages, historique, contexte, libelle, tokensDernierAppel, caracteresDernierAppel, ref elagueAuDelaDeLaLimite, ref elagueDeForceAuDelaDeLaLimite) is { } motifContexte)
                {
                    Arreter(motifContexte);
                    contexteDepasse = true;
                    break;
                }

                if (BudgetAtteint(cout, contexte, libelle))
                {
                    budgetEpuise = true;
                    break;
                }
            }

            if (motifArret is null && !budgetEpuise && !definition.Fin.Termine && iterations >= maxTours)
            {
                Arreter($"limite de {maxTours.ToString(CultureInfo.InvariantCulture)} tours atteinte");
            }
        }
        finally
        {
            client.Dispose();
            EcrireTranscriptionEnCours(contexte, definition, string.Join(" → ", utilises), [.. transcription, .. messages], provisoire: false);
        }

        return new ResultatAgent(
            definition.Fin.Termine,
            iterations - contexte.ToursDejaConsommes,
            tokensEntree,
            tokensSortie,
            Math.Round(cout, 4),
            coutConnu,
            budgetEpuise,
            dernierTexte,
            string.Join(" → ", utilises),
            motifArret,
            contexteDepasse);
    }

    public static bool EstErreurDefinitive(Exception ex) =>
        ErreursDefinitives.Any(motif => ex.Message.Contains(motif, StringComparison.OrdinalIgnoreCase))
        || StatutHttp(ex) is >= 400 and < 500 and not 408 and not 429;

    public static bool EstDebordementContexte(Exception ex) => ErreurContient(ex, ErreursDeContexte);

    public static bool EstAppelDOutilIllisible(Exception ex) => ErreurContient(ex, ErreursDAppelDOutilIllisible);

    private static bool ErreurContient(Exception ex, string[] motifs)
    {
        var texte = TexteDeLErreur(ex);
        return motifs.Any(motif => texte.Contains(motif, StringComparison.OrdinalIgnoreCase));
    }

    private static string DecrireElagage(int resultats, int contenus) =>
        $"{resultats.ToString(CultureInfo.InvariantCulture)} résultats d'outils{(contenus > 0 ? $" et {contenus.ToString(CultureInfo.InvariantCulture)} contenus d'appels d'écriture" : "")} retirés";

    public static bool EstQuotaParMinute(Exception ex) =>
        StatutHttp(ex) == 429 || TexteDeLErreur(ex).Contains("RESOURCE_EXHAUSTED", StringComparison.Ordinal);

    internal static TimeSpan? DelaiReessaiIndique(Exception ex) =>
        DelaiReessai().Match(TexteDeLErreur(ex)) is { Success: true } trouve
            ? TimeSpan.FromSeconds(Math.Ceiling(double.Parse(trouve.Groups["secondes"].Value, CultureInfo.InvariantCulture)) + 1)
            : null;

    internal static TimeSpan AttenteQuota(IReadOnlyList<(DateTimeOffset Instant, long Tokens)> envois, long estimation, long limite, DateTimeOffset maintenant, TimeSpan fenetre)
    {
        var recents = envois.Where(e => e.Instant + fenetre > maintenant).OrderBy(e => e.Instant).ToList();
        var total = recents.Sum(e => e.Tokens);
        var attente = TimeSpan.Zero;
        foreach (var envoi in recents)
        {
            if (total + estimation <= limite)
            {
                break;
            }

            total -= envoi.Tokens;
            attente = envoi.Instant + fenetre - maintenant;
        }

        return attente;
    }

    private static string TexteDeLErreur(Exception ex) => ex switch
    {
        System.ClientModel.ClientResultException erreur when erreur.GetRawResponse() is { } reponse => $"{ex.Message} {reponse.Content}",
        Anthropic.Exceptions.AnthropicApiException { ResponseBody: { } corps } => $"{ex.Message} {corps}",
        _ => ex.Message,
    };

    private static string Decrire(Exception ex) =>
        $"{ex.GetType().Name}{(StatutHttp(ex) is { } statut ? $" HTTP {statut}" : "")} : {PremiereLigne(ex.Message)}{Precision(ex)}";

    private static string Precision(Exception ex) => ex switch
    {
        ArgumentOutOfRangeException { ParamName: "value" } erreur => $" Valeur reçue : {(erreur.ActualValue is { } valeur ? $"« {PremiereLigne(valeur.ToString() ?? "")} »" : "null")}.",
        System.ClientModel.ClientResultException erreur when erreur.GetRawResponse() is { } reponse && PremiereLigne(reponse.Content.ToString().ReplaceLineEndings(" ")) is { Length: > 0 } corps => $" Réponse : {corps}",
        _ => "",
    };

    private static int? StatutHttp(Exception ex) => ex switch
    {
        Google.GenAI.ClientError erreur => erreur.StatusCode,
        Anthropic.Exceptions.AnthropicApiException erreur => (int)erreur.StatusCode,
        System.ClientModel.ClientResultException erreur => erreur.Status,
        _ => null,
    };

    private long SortieFacturee(UsageDetails? usage, ReferenceModele reference)
    {
        var sortie = usage?.OutputTokenCount ?? 0;
        return EstGemini(reference) ? sortie + (usage?.ReasoningTokenCount ?? 0) : sortie;
    }

    private bool EstGemini(ReferenceModele reference) =>
        config.Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur)
        && fournisseur.Type.Equals(TypesFournisseur.Gemini, StringComparison.OrdinalIgnoreCase);

    private string? MotifContexte(ReferenceModele modele, long tokensAppelEntree, List<ChatMessage> messages, HistoriqueVisible historique, ContexteAgent contexte, string libelle, long tokensDernierAppel, long caracteresDernierAppel, ref bool elagueAuDelaDeLaLimite, ref bool elagueDeForceAuDelaDeLaLimite, bool estime = false)
    {
        var limite = config.MaxTokensContextePour(modele);
        var dejaElagueAuDelaDeLaLimite = elagueAuDelaDeLaLimite;
        if (!estime)
        {
            elagueAuDelaDeLaLimite = false;
            elagueDeForceAuDelaDeLaLimite &= tokensAppelEntree > limite;
        }

        if (limite <= 0 || tokensAppelEntree <= (long)limite * NumerateurSeuilElagage / DenominateurSeuilElagage)
        {
            return null;
        }

        var depasse = tokensAppelEntree > limite;
        if (!(depasse && dejaElagueAuDelaDeLaLimite) && Elaguer(messages, historique) is var (retires, contenus) && retires + contenus > 0)
        {
            contexte.Journal($"{libelle} : élagage du contexte, {DecrireElagage(retires, contenus)}.");
            elagueAuDelaDeLaLimite = depasse;
            return null;
        }

        if (depasse && !elagueDeForceAuDelaDeLaLimite)
        {
            var (forces, contenusForces) = ElaguerDeForce(messages, historique, caracteres => EstimerContexte(caracteres, tokensDernierAppel, caracteresDernierAppel) <= (long)limite * NumerateurSeuilElagage / DenominateurSeuilElagage);
            if (forces + contenusForces > 0)
            {
                elagueDeForceAuDelaDeLaLimite = true;
                contexte.Journal($"{libelle} : élagage forcé du contexte (limite atteinte), {DecrireElagage(forces, contenusForces)}.");
                if (EstimerContexte(messages, tokensDernierAppel, caracteresDernierAppel) <= limite)
                {
                    return null;
                }
            }
        }

        return depasse
            ? $"contexte {(estime ? "estimé " : "")}de {tokensAppelEntree.ToString(CultureInfo.InvariantCulture)} tokens au-delà de la limite de {limite.ToString(CultureInfo.InvariantCulture)}"
            : null;
    }

    private static long EstimerContexte(List<ChatMessage> messages, long tokensDernierAppel, long caracteresDernierAppel) =>
        EstimerContexte(Caracteres(messages), tokensDernierAppel, caracteresDernierAppel);

    private static long EstimerContexte(long caracteres, long tokensDernierAppel, long caracteresDernierAppel)
    {
        if (tokensDernierAppel <= 0)
        {
            return caracteres / CaracteresParTokenParDefaut;
        }

        var ajoutes = caracteres - caracteresDernierAppel;
        var estimation = tokensDernierAppel > caracteresDernierAppel * MaxTokensParCaractere
            ? ajoutes * MaxTokensParCaractere
            : ajoutes * tokensDernierAppel / caracteresDernierAppel;
        return tokensDernierAppel + estimation;
    }

    private static long Caracteres(List<ChatMessage> messages)
    {
        long total = 0;
        foreach (var contenu in messages.SelectMany(m => m.Contents))
        {
            total += contenu switch
            {
                TextContent texte => texte.Text.Length,
                FunctionCallContent appel => appel.Name.Length + (appel.Arguments is null ? 0 : JsonSerializer.Serialize(appel.Arguments, Json.Options).Length),
                FunctionResultContent resultat => (resultat.Result?.ToString() ?? "").Length,
                _ => 0,
            };
        }

        return total;
    }

    private static (int Resultats, int Contenus) ElaguerDeForce(List<ChatMessage> messages, HistoriqueVisible historique, Func<long, bool>? assezElague = null)
    {
        var (retires, contenus) = Elaguer(messages, historique);
        var total = Caracteres(messages);
        if (retires + contenus > 0 && (assezElague is null || assezElague(total)))
        {
            return (retires, contenus);
        }

        var tours = messages.Where(m => m.Role == ChatRole.Tool).ToList();
        var cible = total * NumerateurSeuilElagage / DenominateurSeuilElagage;
        var assez = assezElague ?? (caracteres => caracteres <= cible);
        var candidats = tours.Take(tours.Count - 1).SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Where(r => r.Result is string sortie && sortie.Length > SortieRetiree.Length)
            .OrderByDescending(r => ((string)r.Result!).Length);
        var forces = 0;
        foreach (var resultat in candidats)
        {
            if (assez(total))
            {
                break;
            }

            total -= ((string)resultat.Result!).Length - SortieRetiree.Length;
            resultat.Result = SortieRetiree;
            forces++;
        }

        if (forces > 0)
        {
            historique.PremierTourVisible = Math.Max(historique.PremierTourVisible, tours.Count + historique.ToursRetires - 1);
        }

        var ecritures = messages.Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>().Where(a => ContenusLongs(a) > 0).Select(a => (Message: m, Appel: a)))
            .OrderByDescending(e => JsonSerializer.Serialize(e.Appel.Arguments, Json.Options).Length)
            .ToList();
        var contenusForces = 0;
        TextContent? mention = null;
        foreach (var (message, appel) in ecritures)
        {
            if (assez(total))
            {
                break;
            }

            var noms = ArgumentsDeContenu[appel.Name];
            var index = message.Contents.IndexOf(appel);
            message.Contents[index] = new FunctionCallContent(appel.CallId, appel.Name, appel.Arguments!.Where(a => !noms.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));
            mention = new TextContent(ResumerAppelRetire(appel, null));
            message.Contents.Insert(index, mention);
            contenusForces += ContenusLongs(appel);
            var tour = tours.FindIndex(t => t.Contents.OfType<FunctionResultContent>().Any(r => r.CallId == appel.CallId));
            historique.PremierTourVisible = Math.Max(historique.PremierTourVisible, (tour < 0 ? tours.Count : tour + 1) + historique.ToursRetires);
            total = Caracteres(messages);
        }

        RappelerFichiersEcrits(mention, historique);
        return (retires + forces, contenus + contenusForces);
    }

    private static (int Resultats, int Contenus) Elaguer(List<ChatMessage> messages, HistoriqueVisible historique)
    {
        var tours = messages.Where(m => m.Role == ChatRole.Tool).ToList();
        historique.PremierTourVisible = Math.Max(historique.PremierTourVisible, tours.Count + historique.ToursRetires - ToursGardesIntacts);
        var contenus = 0;
        TextContent? mention = null;
        var fin = tours.Count > ToursGardesIntacts ? messages.IndexOf(tours[tours.Count - ToursGardesIntacts - 1]) : 0;
        foreach (var message in messages.Take(fin).Where(m => m.Role == ChatRole.Assistant).ToList())
        {
            foreach (var appel in message.Contents.OfType<FunctionCallContent>().ToList())
            {
                var longs = ContenusLongs(appel);
                if (longs == 0)
                {
                    continue;
                }

                contenus += longs;
                mention = new TextContent(ResumerAppelRetire(appel, RetirerResultat(messages, appel.CallId, historique)));
                message.Contents[message.Contents.IndexOf(appel)] = mention;
            }
        }

        RappelerFichiersEcrits(mention, historique);

        foreach (var message in messages.Where(m => m.Role == ChatRole.Assistant && !m.Contents.Any(c => c is FunctionCallContent)).ToList())
        {
            var index = messages.IndexOf(message);
            if (index + 1 < messages.Count && messages[index + 1].Role == ChatRole.Assistant)
            {
                messages[index + 1].Contents = [.. message.Contents, .. messages[index + 1].Contents];
                messages.RemoveAt(index);
            }
        }

        var retires = 0;
        foreach (var resultat in tours.Take(tours.Count - ToursGardesIntacts).SelectMany(m => m.Contents).OfType<FunctionResultContent>())
        {
            if (resultat.Result is string sortie && sortie.Length > SortieRetiree.Length)
            {
                resultat.Result = SortieRetiree;
                retires++;
            }
        }

        return (retires, contenus);
    }

    private static int ContenusLongs(FunctionCallContent appel) =>
        appel.Arguments is { } arguments && ArgumentsDeContenu.TryGetValue(appel.Name, out var noms)
            ? noms.Count(nom => arguments.TryGetValue(nom, out var valeur) && valeur is string or JsonElement { ValueKind: JsonValueKind.String } && ContenuARetirer(valeur.ToString() ?? ""))
            : 0;

    private static bool ContenuARetirer(string contenu) =>
        contenu.Length > ContenuRetire.Length || OutilsAgent.EstUnMarqueurDeLUsine(contenu);

    private static string? RetirerResultat(List<ChatMessage> messages, string callId, HistoriqueVisible historique)
    {
        foreach (var message in messages.Where(m => m.Role == ChatRole.Tool))
        {
            if (message.Contents.OfType<FunctionResultContent>().FirstOrDefault(r => r.CallId == callId) is not { } resultat)
            {
                continue;
            }

            message.Contents.Remove(resultat);
            if (message.Contents.Count == 0)
            {
                messages.Remove(message);
                historique.ToursRetires++;
            }

            return resultat.Result?.ToString();
        }

        return null;
    }

    internal static void RappelerFichiersEcrits(TextContent? mention, HistoriqueVisible historique)
    {
        if (mention is null || historique.FichiersEcrits.Count == 0)
        {
            return;
        }

        if (historique.MentionDesFichiers is (var precedente, var texteSansFichiers))
        {
            precedente.Text = texteSansFichiers;
        }

        historique.MentionDesFichiers = (mention, mention.Text);
        var anciens = historique.FichiersEcrits.Count - MaxFichiersRappeles;
        var liste = string.Join(", ", historique.FichiersEcrits.TakeLast(MaxFichiersRappeles));
        mention.Text = $"{mention.Text[..^1]} ; fichiers que tu as écrits ou modifiés depuis le début de ta session, inutile de les chercher : {(anciens > 0 ? $"{anciens.ToString(CultureInfo.InvariantCulture)} plus anciens, puis " : "")}{liste})";
    }

    private static string ResumerAppelRetire(FunctionCallContent appel, string? resultat)
    {
        var chemin = appel.Arguments is { } arguments && arguments.TryGetValue("chemin", out var valeur) && valeur?.ToString() is { Length: > 0 } texte ? $" sur {texte}" : "";
        var ligne = resultat is { Length: > 0 } ? PremiereLigne(resultat) : "";
        var issue = ligne.Length == 0 ? "" : OutilsAgent.EstUnMarqueurDeLUsine(ligne) ? " : Refusé, les arguments reprenaient un marqueur de l'usine" : $" : {ligne}";
        return $"({appel.Name}{chemin}{issue} ; contenu de cet appel retiré de l'historique pour faire de la place : relis le fichier si tu en as besoin)";
    }

    private static bool BudgetAtteint(decimal cout, ContexteAgent contexte, string libelle)
    {
        if (contexte.BudgetRestant is not { } restant || cout < restant)
        {
            return false;
        }

        contexte.Journal($"{libelle} : plafond de coût atteint ({cout.ToString("0.00", CultureInfo.InvariantCulture)} $).");
        return true;
    }

    private async Task RespecterCadenceAsync(ReferenceModele reference, CancellationToken ct)
    {
        if (!config.Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur) || fournisseur.RequetesParMinute is not > 0)
        {
            return;
        }

        var intervalle = TimeSpan.FromSeconds(60.0 / fournisseur.RequetesParMinute.Value);
        await VerrouCadence.WaitAsync(ct);
        try
        {
            if (DerniersAppels.TryGetValue(reference.Fournisseur, out var dernier))
            {
                var attente = dernier + intervalle - DateTimeOffset.UtcNow;
                if (attente > TimeSpan.Zero)
                {
                    await Task.Delay(attente, ct);
                }
            }

            DerniersAppels[reference.Fournisseur] = DateTimeOffset.UtcNow;
        }
        finally
        {
            VerrouCadence.Release();
        }
    }

    private async Task RespecterQuotaAsync(ReferenceModele reference, long estimation, ContexteAgent contexte, string libelle, CancellationToken ct)
    {
        if (config.TokensEntreeParMinutePour(reference) is not { } limite)
        {
            return;
        }

        await VerrouCadence.WaitAsync(ct);
        try
        {
            var cle = reference.ToString();
            if (!EnvoisParModele.TryGetValue(cle, out var envois))
            {
                envois = [];
                EnvoisParModele[cle] = envois;
            }

            var attente = AttenteQuota(envois, estimation, limite, DateTimeOffset.UtcNow, FenetreQuota);
            if (attente > TimeSpan.Zero)
            {
                contexte.Journal($"{libelle} : quota de {limite.ToString(CultureInfo.InvariantCulture)} tokens d'entrée par minute du modèle {reference} : appel estimé à {estimation.ToString(CultureInfo.InvariantCulture)} tokens, attente de {Math.Ceiling(attente.TotalSeconds).ToString(CultureInfo.InvariantCulture)} s.");
                await AttendreAsync(attente, contexte, ct);
            }

            var maintenant = DateTimeOffset.UtcNow;
            envois.RemoveAll(e => e.Instant + FenetreQuota <= maintenant);
            envois.Add((maintenant, estimation));
        }
        finally
        {
            VerrouCadence.Release();
        }
    }

    private static decimal CoutExact(TarifModele? tarif, long tokensEntree, long? tokensCache, long tokensSortie)
    {
        if (tarif is null)
        {
            return 0m;
        }

        var servisParLeCache = Math.Clamp(tokensCache ?? 0, 0, tokensEntree);
        return tarif.EntreeCacheParMillion is { } prixCache
            ? ((tokensEntree - servisParLeCache) * tarif.EntreeParMillion + servisParLeCache * prixCache + tokensSortie * tarif.SortieParMillion) / 1_000_000m
            : (tokensEntree * tarif.EntreeParMillion + tokensSortie * tarif.SortieParMillion) / 1_000_000m;
    }

    private static string Jetons(long? nombre) => nombre?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private async Task<(ChatResponse Reponse, TimeSpan Duree)> AppelerAvecReessaisAsync(
        IChatClient client,
        ReferenceModele reference,
        bool secoursDisponible,
        long estimation,
        List<ChatMessage> messages,
        ChatOptions options,
        ContexteAgent contexte,
        string libelle,
        CancellationToken ct)
    {
        var limite = config.TokensEntreeParMinutePour(reference);
        if (secoursDisponible && limite is { } plafond && estimation > plafond)
        {
            throw new AppelAuDelaDuQuotaException($"appel estimé à {estimation.ToString(CultureInfo.InvariantCulture)} tokens d'entrée, au-delà du quota de {plafond.ToString(CultureInfo.InvariantCulture)} tokens d'entrée par minute du modèle {reference}");
        }

        var delaisDepasses = 0;
        var reponsesIllisibles = 0;
        for (var essai = 0; ; essai++)
        {
            await RespecterCadenceAsync(reference, ct);
            await RespecterQuotaAsync(reference, estimation, contexte, libelle, ct);
            var debut = Stopwatch.GetTimestamp();
            try
            {
                return (await client.GetResponseAsync(messages, options, ct), Stopwatch.GetElapsedTime(debut));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && !EstErreurDefinitive(ex) && !EstDebordementContexte(ex) && !EstAppelDOutilIllisible(ex) && essai < Attentes.Length)
            {
                var illisible = ex is ArgumentOutOfRangeException or JsonException;
                if ((ex is OperationCanceledException && ++delaisDepasses > MaxDelaisDepasses) || (illisible && ++reponsesIllisibles > MaxReponsesIllisibles))
                {
                    throw;
                }

                var quota = !illisible && EstQuotaParMinute(ex);
                var attente = quota ? DelaiReessaiIndique(ex) ?? (limite is not null && FenetreQuota > Attentes[essai] ? FenetreQuota : Attentes[essai]) : Attentes[essai];
                var secondes = attente.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                contexte.Journal(quota
                    ? $"{libelle} : quota par minute du modèle {reference} atteint ({Decrire(ex)}), nouvel essai dans {secondes} s."
                    : $"{libelle} : {(illisible ? "réponse reçue mais illisible, sa consommation n'est pas comptée" : "erreur du modèle")} ({Decrire(ex)}), nouvel essai dans {Attentes[essai].TotalSeconds} s.");
                if (essai == 0)
                {
                    contexte.Signaler?.Invoke(quota
                        ? $"Quota par minute du modèle {reference} atteint ({(StatutHttp(ex) == 429 ? "HTTP 429" : "RESOURCE_EXHAUSTED")}) : nouvel essai dans {secondes} s."
                        : illisible
                            ? $"Réponse du modèle {reference} reçue mais illisible ({Decrire(ex)}) : nouvel essai en cours."
                            : $"Le modèle {reference} ne répond pas ({Decrire(ex)}) : nouveaux essais en cours.");
                }

                await AttendreAsync(attente, contexte, ct);
            }
        }
    }

    private static async Task AttendreAsync(TimeSpan duree, ContexteAgent contexte, CancellationToken ct)
    {
        var fin = DateTimeOffset.UtcNow + duree;
        while (true)
        {
            if (contexte.AnnulationDemandee())
            {
                throw new OperationCanceledException("Annulation demandée.");
            }

            var reste = fin - DateTimeOffset.UtcNow;
            if (reste <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(reste < TimeSpan.FromSeconds(1) ? reste : TimeSpan.FromSeconds(1), ct);
        }
    }

    private static List<ChatMessage> NouvelleConversation(DefinitionAgent definition, string message) =>
    [
        new(ChatRole.System, definition.PromptSysteme),
        new(ChatRole.User, message),
    ];

    private static async Task<string> InvoquerAsync(FunctionCallContent appel, IReadOnlyDictionary<string, AIFunction> outils, HistoriqueVisible historique, CancellationToken ct)
    {
        if (appel.Exception is not null)
        {
            return $"Erreur : les arguments de l'appel sont invalides ({appel.Exception.Message}). Réessaie avec un contenu plus court ou découpé.";
        }

        if (!outils.TryGetValue(appel.Name, out var outil))
        {
            return $"Erreur : outil inconnu « {appel.Name} ». Outils disponibles : {string.Join(", ", outils.Keys)}.";
        }

        try
        {
            var arguments = new AIFunctionArguments(appel.Arguments ?? new Dictionary<string, object?>())
            {
                Context = new Dictionary<object, object?> { [typeof(HistoriqueVisible)] = historique },
            };
            var resultat = await outil.InvokeAsync(arguments, ct);
            return resultat switch
            {
                null => "",
                string texte => texte,
                JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.ToString(),
                _ => JsonSerializer.Serialize(resultat, Json.Options),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Erreur : {ex.Message}";
        }
    }

    private static string DecrireRefus(int fois, (string Outil, string Refus) cle) =>
        $"{fois.ToString(CultureInfo.InvariantCulture)} refus identiques de l'outil {cle.Outil}, sans réussite de cet outil entre-temps : « {cle.Refus} »";

    private static SortedDictionary<string, object?>? Ordonner(IDictionary<string, object?>? arguments) =>
        arguments is null ? null : new SortedDictionary<string, object?>(arguments, StringComparer.Ordinal);

    private static (int Periode, int Repetitions) Cycle(List<string> signatures)
    {
        var meilleur = (Periode: 1, Repetitions: 0);
        for (var periode = 1; periode <= PeriodeMaxCycle && periode <= signatures.Count; periode++)
        {
            var repetitions = 1;
            while (signatures.Count >= periode * (repetitions + 1) && BlocsEgaux(signatures, periode, repetitions))
            {
                repetitions++;
            }

            if (repetitions > meilleur.Repetitions)
            {
                meilleur = (periode, repetitions);
            }
        }

        return meilleur;
    }

    private static bool BlocsEgaux(List<string> signatures, int periode, int decalage)
    {
        var dernier = signatures.Count - 1;
        for (var i = 0; i < periode; i++)
        {
            if (signatures[dernier - i] != signatures[dernier - i - periode * decalage])
            {
                return false;
            }
        }

        return true;
    }

    private static string DecrireCycle(int fois, int periode, List<string> nomsDesTours) =>
        periode == 1
            ? $"{fois.ToString(CultureInfo.InvariantCulture)} appels identiques de suite à {nomsDesTours[^1]}"
            : $"{fois.ToString(CultureInfo.InvariantCulture)} répétitions de suite du même enchaînement de {periode.ToString(CultureInfo.InvariantCulture)} tours ({string.Join(", ", nomsDesTours.Skip(nomsDesTours.Count - periode))})";

    private static string DecrireRappel(int periode, List<string> nomsDesTours) =>
        periode == 1
            ? $"Tu viens d'appeler {RepetitionsAvantRappel.ToString(CultureInfo.InvariantCulture)} fois de suite {nomsDesTours[^1]}"
            : $"Tu viens de répéter {RepetitionsAvantRappel.ToString(CultureInfo.InvariantCulture)} fois de suite le même enchaînement de {periode.ToString(CultureInfo.InvariantCulture)} tours ({string.Join(", ", nomsDesTours.Skip(nomsDesTours.Count - periode))})";

    private static string LibelleDe(DefinitionAgent definition) => definition.Libelle ?? RolesAgent.Libelle(definition.Role);

    private static int ToursRestantsAuRappel(RoleAgent role, int maxTours) => role == RoleAgent.Qa
        ? Math.Max(maxTours / DiviseurToursRestantsAvantRappelFinDeToursQa, Math.Min(ToursRestantsMinAvantRappelFinDeToursQa, maxTours / DiviseurToursRestantsAvantRappelFinDeTours))
        : maxTours / DiviseurToursRestantsAvantRappelFinDeTours;

    private static string? ConsigneFinDeTours(RoleAgent role, string terminaux, string? dernierRefus) => role switch
    {
        RoleAgent.QaAmont => $"Arrête les essais annexes : soumets maintenant les tests que tu as déjà écrits en appelant {terminaux}. S'il leur manque quelque chose, sa réponse te dira quoi corriger.",
        RoleAgent.Dev => $"Arrête les vérifications annexes et concentre-toi sur les tests d'acceptation encore rouges : lance lancer_tests_acceptation, puis appelle {terminaux} avec un résumé honnête de ce qui passe et de ce qui ne passe pas.",
        RoleAgent.Qa when dernierRefus is not null => $"Ta dernière soumission a été refusée :\n{RefusRappele(dernierRefus)}\nResoumets maintenant en appelant {terminaux}, en corrigeant ou en retirant ce qui a été refusé, même les essais que tu n'as pas pu prouver : passe-les dans nonTeste. Sans évaluation soumise avant la fin de tes tours, tout ton travail est perdu.",
        RoleAgent.Qa => $"Arrête de chercher à tout vérifier : appelle maintenant {terminaux} avec ce que tu as déjà constaté, et dis dans nonTeste ce que tu n'as pas pu tester. Sans évaluation soumise avant la fin de tes tours, tout ton travail est perdu.",
        _ => null,
    };

    private static string RefusRappele(string refus)
    {
        var cite = refus.EndsWith(ConsigneApresRefusEvaluation, StringComparison.Ordinal) ? refus[..^ConsigneApresRefusEvaluation.Length].TrimEnd() : refus;
        return cite.Length > TailleMaxRefusRappele ? cite[..TailleMaxRefusRappele] + "…" : cite;
    }

    internal static string ResumerArguments(FunctionCallContent appel) => FormaterArguments(appel, complet: false);

         internal static string ArgumentsComplets(FunctionCallContent appel) => FormaterArguments(appel, complet: true);

         private static string FormaterArguments(FunctionCallContent appel, bool complet)
         {
             if (appel.Arguments is null || appel.Arguments.Count == 0)
             {
                 return "";
             }

             var parties = appel.Arguments.Select(a =>
             {
                 var valeur = a.Value switch
                 {
                     null => "null",
                     JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString() ?? "",
                     _ => a.Value.ToString() ?? "",
                 };
                 if (complet)
                 {
                     return $"{a.Key}={valeur}";
                 }

                 valeur = valeur.ReplaceLineEndings(" ");
                 return $"{a.Key}={(valeur.Length > 80 ? valeur[..77] + "…" : valeur)}";
             });
             return string.Join(", ", parties);
         }

    internal static string PremiereLigne(string texte)
    {
        var ligne = texte.Trim().Split('\n', 2)[0].Trim();
        return ligne.Length > 160 ? ligne[..157] + "…" : ligne;
    }

    private static void EcrireTranscription(string? chemin, DefinitionAgent definition, string modeles, List<ChatMessage> messages)
    {
        if (chemin is null)
        {
            return;
        }

        var texte = new StringBuilder();
        texte.AppendLine(CultureInfo.InvariantCulture, $"# {LibelleDe(definition)} — {modeles}");
        foreach (var message in messages)
        {
            texte.AppendLine();
            texte.AppendLine(CultureInfo.InvariantCulture, $"## {message.Role}");
            foreach (var contenu in message.Contents)
            {
                switch (contenu)
                {
                    case TextContent t:
                        texte.AppendLine(ExecuteurProcessus.Tronquer(t.Text, 20000));
                        break;
                    case FunctionCallContent f:
                        texte.AppendLine(CultureInfo.InvariantCulture, $"→ appel {f.Name} :");
                        texte.AppendLine("```json");
                        texte.AppendLine(ExecuteurProcessus.Tronquer(JsonSerializer.Serialize(f.Arguments, Json.Options), 8000));
                        texte.AppendLine("```");
                        break;
                    case FunctionResultContent r:
                        texte.AppendLine("← résultat :");
                        texte.AppendLine("```");
                        texte.AppendLine(ExecuteurProcessus.Tronquer(r.Result?.ToString() ?? "", 8000));
                        texte.AppendLine("```");
                        break;
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        File.WriteAllText(chemin, texte.ToString());
    }

    private static void EcrireTranscriptionEnCours(ContexteAgent contexte, DefinitionAgent definition, string modeles, List<ChatMessage> messages, bool provisoire = true)
    {
        try
        {
            EcrireTranscription(contexte.CheminTranscription, definition, modeles, messages);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            contexte.Journal($"{LibelleDe(definition)} : transcription {(provisoire ? "provisoire " : "")}impossible ({ex.Message}).");
        }
    }

    private static void ConserverSortie(ContexteAgent contexte, DefinitionAgent definition, int tour, FunctionCallContent appel, string sortie)
    {
        if (contexte.CheminSorties is null)
        {
            return;
        }

        var texte = new StringBuilder();
        texte.AppendLine(CultureInfo.InvariantCulture, $"## Tour {tour.ToString(CultureInfo.InvariantCulture)} : {appel.Name}({ExecuteurProcessus.Tronquer(ArgumentsComplets(appel), TailleMaxSortieConservee)})");
        texte.AppendLine(ExecuteurProcessus.Tronquer(sortie, TailleMaxSortieConservee));
        texte.AppendLine();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contexte.CheminSorties)!);
            File.AppendAllText(contexte.CheminSorties, texte.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            contexte.Journal($"{LibelleDe(definition)} : sortie du tour {tour.ToString(CultureInfo.InvariantCulture)} non conservée ({ex.Message}).");
        }
    }

    [GeneratedRegex(@"""retryDelay""\s*:\s*""(?<secondes>\d+(?:\.\d+)?)s""")]
    private static partial Regex DelaiReessai();
}

public sealed class AppelAuDelaDuQuotaException(string message) : Exception(message);
