using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Usine.Core.Modeles;

public sealed class ScenarioScripte
{
    public required string Cle { get; init; }

    public required IReadOnlyList<TourScripte> Tours { get; init; }

    public static ScenarioScripte Charger(string chemin, string cle)
    {
        if (!File.Exists(chemin))
        {
            throw new InvalidOperationException($"Scénario scripté introuvable : « {chemin} ».");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(chemin));
        if (!document.RootElement.TryGetProperty("roles", out var roles) || !roles.TryGetProperty(cle, out var tours))
        {
            throw new InvalidOperationException($"Le scénario « {chemin} » ne contient pas de rôle « {cle} ».");
        }

        return new ScenarioScripte
        {
            Cle = cle,
            Tours = tours.EnumerateArray().Select(TourScripte.Lire).ToList(),
        };
    }
}

public sealed class TourScripte
{
    public string? Texte { get; init; }

    public IReadOnlyList<AppelScripte> Appels { get; init; } = [];

    public static TourScripte Lire(JsonElement element)
    {
        var appels = new List<AppelScripte>();
        if (element.TryGetProperty("appels", out var liste))
        {
            foreach (var appel in liste.EnumerateArray())
            {
                var arguments = new Dictionary<string, object?>();
                if (appel.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    foreach (var propriete in args.EnumerateObject())
                    {
                        arguments[propriete.Name] = propriete.Value.Clone();
                    }
                }

                appels.Add(new AppelScripte(appel.GetProperty("outil").GetString()!, arguments));
            }
        }

        return new TourScripte
        {
            Texte = element.TryGetProperty("texte", out var texte) ? texte.GetString() : null,
            Appels = appels,
        };
    }
}

public sealed record AppelScripte(string Outil, IDictionary<string, object?> Arguments);

public sealed class ClientChatScripte(ScenarioScripte scenario) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var index = messages.Count(m => m.Role == ChatRole.Assistant);
        var contenus = new List<AIContent>();
        if (index >= scenario.Tours.Count)
        {
            contenus.Add(new TextContent("(scénario épuisé)"));
        }
        else
        {
            var tour = scenario.Tours[index];
            if (!string.IsNullOrEmpty(tour.Texte))
            {
                contenus.Add(new TextContent(tour.Texte));
            }

            for (var i = 0; i < tour.Appels.Count; i++)
            {
                var appel = tour.Appels[i];
                var identifiant = $"{scenario.Cle}-{index.ToString(CultureInfo.InvariantCulture)}-{i.ToString(CultureInfo.InvariantCulture)}";
                FunctionCallContent contenuAppel = new(identifiant, appel.Outil, new Dictionary<string, object?>(appel.Arguments));
                contenus.Add(contenuAppel);
            }
        }

        var reponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, contenus))
        {
            ModelId = $"scripte:{scenario.Cle}",
            Usage = new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 200, TotalTokenCount = 1200 },
        };
        return Task.FromResult(reponse);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Le client scripté ne gère pas le streaming.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
