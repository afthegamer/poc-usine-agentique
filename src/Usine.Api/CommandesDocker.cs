using Usine.Core.BacASable;

namespace Usine.Api;

public interface ICommandesDocker
{
    Task<ResultatCommande> ExecuterAsync(IReadOnlyList<string> arguments, CancellationToken ct);
}

public sealed class CommandesDocker : ICommandesDocker
{
    public Task<ResultatCommande> ExecuterAsync(IReadOnlyList<string> arguments, CancellationToken ct) =>
        BacASableDocker.DockerAsync(arguments, ct, verifier: false);
}
