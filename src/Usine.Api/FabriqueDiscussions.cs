using Microsoft.Extensions.AI;
using Usine.Core.Configuration;
using Usine.Core.Discussion;
using Usine.Core.Modeles;
using Usine.Mcp;

namespace Usine.Api;

public interface IFabriqueDiscussions
{
    Task<OutillageDiscussion> CreerAsync(ConfigurationUsine configuration, ReferenceModele reference, CancellationToken ct);
}

public sealed class OutillageDiscussion(IChatClient client, IReadOnlyList<OutilDiscussion> outils, string? instructions, IAsyncDisposable? connexion = null) : IAsyncDisposable
{
    public IChatClient Client { get; } = client;

    public IReadOnlyList<OutilDiscussion> Outils { get; } = outils;

    public string? Instructions { get; } = instructions;

    public async ValueTask DisposeAsync()
    {
        if (connexion is not null)
        {
            await connexion.DisposeAsync();
        }

        Client.Dispose();
    }
}

internal sealed class FabriqueDiscussionsUsine : IFabriqueDiscussions
{
    public async Task<OutillageDiscussion> CreerAsync(ConfigurationUsine configuration, ReferenceModele reference, CancellationToken ct)
    {
        var mcp = await ClientUsine.ConnecterAsync(configuration, Console.Error.WriteLine, ct, autoApresBacklog: true);
        try
        {
            var outils = await ClientUsine.OutilsAsync(mcp, ct);
            return new OutillageDiscussion(Client(configuration, reference), outils, mcp.ServerInstructions, mcp);
        }
        catch
        {
            await mcp.DisposeAsync();
            throw;
        }
    }

    internal static IChatClient Client(ConfigurationUsine configuration, ReferenceModele reference) =>
        new FabriqueClientsChat(configuration.Config, configuration.Cles).Creer(reference);
}
