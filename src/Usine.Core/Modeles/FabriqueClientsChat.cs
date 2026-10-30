using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Usine.Core.Configuration;

namespace Usine.Core.Modeles;

public interface IFabriqueClientsChat
{
    IChatClient Creer(ReferenceModele reference, RoleAgent? role = null);
}

public sealed class FabriqueClientsChat(ConfigUsine config, ClesApi? cles = null) : IFabriqueClientsChat
{
    public const string UrlAnthropic = "https://api.anthropic.com";

    public static readonly TimeSpan DelaiAppel = TimeSpan.FromMinutes(10);

    public IChatClient Creer(ReferenceModele reference, RoleAgent? role = null)
    {
        if (!config.Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur))
        {
            throw new InvalidOperationException($"Le fournisseur « {reference.Fournisseur} » n'est pas déclaré dans la configuration.");
        }

        var effort = (role is { } choisi ? config.EffortsRaisonnement.Pour(choisi) : null) ?? fournisseur.EffortRaisonnement;
        var maxTokens = config.MaxTokensSortiePour(reference);
        var delai = fournisseur.DelaiAppelSecondes is > 0 ? TimeSpan.FromSeconds(fournisseur.DelaiAppelSecondes.Value) : DelaiAppel;
        return fournisseur.Type.ToLowerInvariant() switch
        {
            TypesFournisseur.OpenAiCompatible => CreerOpenAiCompatible(reference, fournisseur, delai, cles, effort),
            TypesFournisseur.Anthropic => new AnthropicClient
                {
                    ApiKey = LireCle(reference.Fournisseur, fournisseur, obligatoire: true, cles),
                    AuthToken = null,
                    BaseUrl = string.IsNullOrWhiteSpace(fournisseur.UrlBase) ? UrlAnthropic : fournisseur.UrlBase,
                    Timeout = delai,
                    MaxRetries = 0,
                }
                .AsIChatClient(reference.Modele, maxTokens),
            TypesFournisseur.Gemini => AvecEffort(
                new ConsommationGemini(new Google.GenAI.Client(
                        apiKey: LireCle(reference.Fournisseur, fournisseur, obligatoire: true, cles),
                        httpOptions: new Google.GenAI.Types.HttpOptions { Timeout = (int)delai.TotalMilliseconds })
                    .AsIChatClient(reference.Modele)),
                effort),
            TypesFournisseur.Scripte => new ClientChatScripte(ScenarioScripte.Charger(
                fournisseur.Scenario ?? throw new InvalidOperationException($"Le fournisseur scripté « {reference.Fournisseur} » n'a pas de « scenario »."),
                reference.Modele)),
            _ => throw new InvalidOperationException($"Type de fournisseur inconnu : « {fournisseur.Type} »."),
        };
    }

    public static string? LireCle(string nomFournisseur, ConfigFournisseur fournisseur, bool obligatoire, ClesApi? cles)
    {
        if (string.IsNullOrWhiteSpace(fournisseur.VariableCleApi))
        {
            return obligatoire
                ? throw new InvalidOperationException($"Le fournisseur « {nomFournisseur} » n'a pas de « variableCleApi ».")
                : null;
        }

        var cle = (cles ?? ClesApi.Environnement()).Trouver(fournisseur.VariableCleApi);
        if (cle is null && obligatoire)
        {
            throw new InvalidOperationException(
                $"La clé API du fournisseur « {nomFournisseur} » est absente : enregistre-la dans les User Secrets (dotnet user-secrets set {fournisseur.VariableCleApi} <clé> --project src/Usine.Cli) ou définis la variable d'environnement {fournisseur.VariableCleApi}.");
        }

        return cle?.Valeur;
    }

    private static IChatClient CreerOpenAiCompatible(ReferenceModele reference, ConfigFournisseur fournisseur, TimeSpan delai, ClesApi? cles, string? effort)
    {
        if (string.IsNullOrWhiteSpace(fournisseur.UrlBase))
        {
            throw new InvalidOperationException($"Le fournisseur « {reference.Fournisseur} » n'a pas d'« urlBase ».");
        }

        var cle = LireCle(reference.Fournisseur, fournisseur, obligatoire: !string.IsNullOrWhiteSpace(fournisseur.VariableCleApi), cles) ?? "sans-cle";
        var urlBase = new Uri(fournisseur.UrlBase);
        var options = new OpenAIClientOptions { Endpoint = urlBase, NetworkTimeout = delai, RetryPolicy = new ClientRetryPolicy(maxRetries: 0) };
        if (PolitiqueSignaturesGemini.Concerne(urlBase))
        {
            options.AddPolicy(new PolitiqueSignaturesGemini(), PipelinePosition.PerCall);
        }

        return AvecEffort(new ChatClient(reference.Modele, new ApiKeyCredential(cle), options).AsIChatClient(), effort);
    }

    private static IChatClient AvecEffort(IChatClient client, string? valeur)
    {
        var effort = EffortsRaisonnement.Lire(valeur);
        return effort is null
            ? client
            : new ConfigureOptionsChatClient(client, optionsAppel => optionsAppel.Reasoning ??= new ReasoningOptions { Effort = effort });
    }
}
