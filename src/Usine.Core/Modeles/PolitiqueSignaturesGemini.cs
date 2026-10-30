using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace Usine.Core.Modeles;

public sealed class PolitiqueSignaturesGemini : PipelinePolicy
{
    public const string SignatureNeutre = "skip_thought_signature_validator";

    private readonly Dictionary<string, string> signatures = new(StringComparer.Ordinal);

    private readonly Lock verrou = new();

    public static bool Concerne(Uri urlBase) =>
        urlBase.Host.Equals("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Completer(message);
        ProcessNext(message, pipeline, currentIndex);
        Memoriser(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Completer(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
        Memoriser(message);
    }

    public static string? CompleterRequete(string json, IReadOnlyDictionary<string, string> connues)
    {
        if (JsonNode.Parse(json) is not JsonObject requete || requete["messages"] is not JsonArray messages)
        {
            return null;
        }

        var modifie = false;
        foreach (var appel in messages.OfType<JsonObject>().Select(m => m["tool_calls"]).OfType<JsonArray>().SelectMany(a => a.OfType<JsonObject>()))
        {
            if (appel["extra_content"]?["google"]?["thought_signature"] is not null)
            {
                continue;
            }

            var id = appel["id"]?.GetValue<string>();
            var signature = id is not null && connues.TryGetValue(id, out var connue) ? connue : SignatureNeutre;
            appel["extra_content"] = new JsonObject { ["google"] = new JsonObject { ["thought_signature"] = signature } };
            modifie = true;
        }

        return modifie ? requete.ToJsonString() : null;
    }

    public static IEnumerable<KeyValuePair<string, string>> LireSignatures(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject reponse || reponse["choices"] is not JsonArray choix)
        {
            yield break;
        }

        foreach (var appel in choix.OfType<JsonObject>().Select(c => c["message"]?["tool_calls"]).OfType<JsonArray>().SelectMany(a => a.OfType<JsonObject>()))
        {
            if (appel["id"]?.GetValue<string>() is { Length: > 0 } id
                && appel["extra_content"]?["google"]?["thought_signature"]?.GetValue<string>() is { Length: > 0 } signature)
            {
                yield return new(id, signature);
            }
        }
    }

    private void Completer(PipelineMessage message)
    {
        if (message.Request.Content is not { } contenu)
        {
            return;
        }

        using var flux = new MemoryStream();
        contenu.WriteTo(flux, message.CancellationToken);
        Dictionary<string, string> connues;
        lock (verrou)
        {
            connues = new Dictionary<string, string>(signatures, StringComparer.Ordinal);
        }

        try
        {
            if (CompleterRequete(System.Text.Encoding.UTF8.GetString(flux.ToArray()), connues) is { } complete)
            {
                message.Request.Content = BinaryContent.Create(BinaryData.FromString(complete));
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }
    }

    private void Memoriser(PipelineMessage message)
    {
        if (message.Response is not { IsError: false } reponse || !message.BufferResponse)
        {
            return;
        }

        List<KeyValuePair<string, string>> lues;
        try
        {
            lues = LireSignatures(reponse.Content.ToString()).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        lock (verrou)
        {
            foreach (var (id, signature) in lues)
            {
                signatures[id] = signature;
            }
        }
    }
}
