using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Usine.Core.Configuration;
using Usine.Core.Discussion;
using Usine.Core.Services;

namespace Usine.Mcp;

public static class ClientUsine
{
    private static readonly TimeSpan DelaiArretServeur = TimeSpan.FromMilliseconds(500);

    public static StdioClientTransportOptions OptionsTransport(ConfigurationUsine configuration, Action<string> erreursServeur, bool autoApresBacklog = false)
    {
        var (executable, prefixe) = LanceurWorkerProcessus.CommandeUsine();
        var variables = new Dictionary<string, string?> { [ChargeurConfig.VariableRepertoire] = configuration.RepertoireDonnees, [ChargeurConfig.VariableProjets] = configuration.RepertoireProjets };
        if (configuration.FichierImpose is { } fichier)
        {
            variables[ChargeurConfig.VariableFichier] = fichier;
        }

        return new StdioClientTransportOptions
        {
            Name = "usine",
            Command = executable,
            Arguments = autoApresBacklog ? [.. prefixe, "mcp", "--auto-apres-backlog"] : [.. prefixe, "mcp"],
            EnvironmentVariables = variables,
            StandardErrorLines = erreursServeur,
            ShutdownTimeout = DelaiArretServeur,
        };
    }

    public static Task<McpClient> ConnecterAsync(ConfigurationUsine configuration, Action<string> erreursServeur, CancellationToken ct, bool autoApresBacklog = false) =>
        McpClient.CreateAsync(new StdioClientTransport(OptionsTransport(configuration, erreursServeur, autoApresBacklog)), cancellationToken: ct);

    public static async Task<IReadOnlyList<OutilDiscussion>> OutilsAsync(McpClient client, CancellationToken ct)
    {
        var outils = await client.ListToolsAsync(cancellationToken: ct);
        return outils.Select(o => new OutilDiscussion(new OutilTexte(o), EstDestructif(o.ProtocolTool.Annotations))).ToList();
    }

    public static bool EstDestructif(ToolAnnotations? annotations) =>
        annotations?.ReadOnlyHint != true && annotations?.DestructiveHint != false;
}

internal sealed class OutilTexte(McpClientTool outil) : DelegatingAIFunction(outil)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var resultat = await outil.CallAsync(arguments, cancellationToken: cancellationToken);
        var texte = string.Join("\n", resultat.Content.OfType<TextContentBlock>().Select(b => b.Text));
        return resultat.IsError == true ? $"Erreur : {texte}" : texte;
    }
}
