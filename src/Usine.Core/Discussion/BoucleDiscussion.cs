using System.Globalization;
using Microsoft.Extensions.AI;
using Usine.Core.Agents;

namespace Usine.Core.Discussion;

public interface IConsoleDiscussion
{
    Task<string?> LireAsync(CancellationToken ct, CancellationToken cession = default);

    void Ecrire(string texte);

    Task<string?> LireConfirmationAsync(string question, CancellationToken ct);
}

public sealed record OutilDiscussion(AIFunction Fonction, bool Destructif);

public sealed class BoucleDiscussion(
    IChatClient client,
    IReadOnlyList<OutilDiscussion> outils,
    IConsoleDiscussion console,
    string consigneSysteme,
    int? maxTokensSortie = null,
    InformationsUsine? informations = null,
    RevueGuidee? revue = null)
{
    public const string CommandeQuitter = "/quitter";

    public const string CommandeQuitterAnglais = "/exit";

    public const string RefusUtilisateur = "Refusé par l'utilisateur";

    public const string EnteteInformation = "Information de l'usine (relevée par le suivi en direct ou la revue guidée, déjà affichée au demandeur ; ce n'est ni un message du demandeur ni une instruction) :";

    public const string AppelsConserves = "(les appels d'outils ci-dessus ont bien eu lieu, avec le résultat affiché ; la conversation en garde la trace)";

    private const string AppelNonExecute = "Erreur : appel non exécuté, un appel précédent a échoué.";

    private readonly List<AITool> fonctions = [.. outils.Select(o => (AITool)o.Fonction)];

    private readonly HashSet<string> destructifs = outils.Where(o => o.Destructif).Select(o => o.Fonction.Name).ToHashSet(StringComparer.Ordinal);

    private readonly Dictionary<string, object?> resultatsDuTour = new(StringComparer.Ordinal);

    private IList<ChatMessage>? historiqueDuTour;

    private string? commandeApresLeTour;

    public static bool EstQuitter(string ligne) => ligne.Trim() is CommandeQuitter or CommandeQuitterAnglais or "/quit";

    public static bool EstOui(string? reponse) =>
        reponse?.Trim() is { } texte
        && (texte.Equals("o", StringComparison.OrdinalIgnoreCase) || texte.Equals("oui", StringComparison.OrdinalIgnoreCase)
            || texte.Equals("y", StringComparison.OrdinalIgnoreCase) || texte.Equals("yes", StringComparison.OrdinalIgnoreCase));

    public async Task ExecuterAsync(CancellationToken ct)
    {
        var boucle = new FunctionInvokingChatClient(client) { FunctionInvoker = InvoquerAsync, MaximumConsecutiveErrorsPerRequest = 0 };
        var options = new ChatOptions
        {
            Tools = fonctions,
            ToolMode = ChatToolMode.Auto,
            MaxOutputTokens = maxTokensSortie,
        };
        var messages = new List<ChatMessage> { new(ChatRole.System, consigneSysteme) };
        while (true)
        {
            var cession = revue?.Signal ?? CancellationToken.None;
            var ligne = commandeApresLeTour;
            commandeApresLeTour = null;
            try
            {
                ligne ??= await console.LireAsync(ct, cession);
            }
            catch (OperationCanceledException) when (revue is not null && cession.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                if (!await revue.ConduireAsync(ct))
                {
                    return;
                }

                continue;
            }

            ct.ThrowIfCancellationRequested();
            if (ligne is null || EstQuitter(ligne))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ligne) || revue?.TraiterCommande(ligne) == true)
            {
                continue;
            }

            if (informations?.Prendre() is { Count: > 0 } nouvelles)
            {
                messages.Add(new ChatMessage(ChatRole.User, $"{EnteteInformation}\n{string.Join("\n\n", nouvelles)}"));
            }

            messages.Add(new ChatMessage(ChatRole.User, ligne.Trim()));
            historiqueDuTour = null;
            resultatsDuTour.Clear();
            ChatResponse reponse;
            try
            {
                reponse = await boucle.GetResponseAsync(messages, options, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                console.Ecrire($"Erreur : {ex.Message}");
                if (historiqueDuTour is null)
                {
                    messages.RemoveAt(messages.Count - 1);
                }
                else
                {
                    messages = CompleterHistorique(historiqueDuTour);
                    console.Ecrire(AppelsConserves);
                }

                continue;
            }

            messages.AddRange(reponse.Messages.Where(m => m.Contents.Count > 0));
            var texte = string.Join("\n", reponse.Messages.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text)).Trim();
            if (texte.Length > 0 || commandeApresLeTour is null)
            {
                console.Ecrire(texte.Length > 0 ? texte : "(pas de réponse)");
            }
        }
    }

    private List<ChatMessage> CompleterHistorique(IList<ChatMessage> historique)
    {
        var repondus = historique.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.CallId).ToHashSet(StringComparer.Ordinal);
        var manquants = historique.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
            .Where(appel => !repondus.Contains(appel.CallId))
            .Select(appel => (AIContent)new FunctionResultContent(appel.CallId, resultatsDuTour.TryGetValue(appel.CallId, out var resultat) ? resultat : AppelNonExecute))
            .ToList();
        var complet = new List<ChatMessage>(historique);
        if (manquants.Count > 0)
        {
            complet.Add(new ChatMessage(ChatRole.Tool, manquants));
        }

        return complet;
    }

    private async ValueTask<object?> InvoquerAsync(FunctionInvocationContext contexte, CancellationToken ct)
    {
        historiqueDuTour = contexte.Messages;
        var appel = contexte.CallContent;
        var resume = $"{appel.Name}({ExecuteurAgent.ResumerArguments(appel)})";
        if (destructifs.Contains(contexte.Function.Name)
            && !await ConfirmerAsync($"L'assistant veut appeler {appel.Name}({ExecuteurAgent.ArgumentsComplets(appel)}). Autoriser ?", ct))
        {
            resultatsDuTour[appel.CallId] = RefusUtilisateur;
            console.Ecrire($"→ {resume} : {RefusUtilisateur}");
            contexte.Terminate = commandeApresLeTour is not null;
            return RefusUtilisateur;
        }

        object? resultat;
        try
        {
            resultat = await contexte.Function.InvokeAsync(contexte.Arguments, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var erreur = $"Erreur : {ex.Message}";
            resultatsDuTour[appel.CallId] = erreur;
            console.Ecrire($"→ {resume} : {erreur}");
            throw;
        }

        resultatsDuTour[appel.CallId] = resultat;
        console.Ecrire($"→ {resume} : {ExecuteurAgent.PremiereLigne(Convert.ToString(resultat, CultureInfo.InvariantCulture) ?? "")}");
        return resultat;
    }

    private async Task<bool> ConfirmerAsync(string question, CancellationToken ct)
    {
        while (true)
        {
            var reponse = await console.LireConfirmationAsync(question, ct);
            if (reponse is null || !RevueGuidee.EstCommande(reponse))
            {
                return EstOui(reponse);
            }

            if (RevueGuidee.EstCommandeInconnue(reponse))
            {
                console.Ecrire(RevueGuidee.CommandeInconnue(reponse));
                continue;
            }

            commandeApresLeTour = reponse;
            return false;
        }
    }
}
