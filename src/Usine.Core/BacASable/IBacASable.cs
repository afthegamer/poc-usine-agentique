namespace Usine.Core.BacASable;

public enum ModeMontage
{
    Exploration,
    EcritureTests,
    EcritureTestsSansCode,
    Construction,
}

public interface IBacASable
{
    string NomConteneur { get; }

    Task InitialiserAsync(string? depotSource, CancellationToken ct);

    Task PreparerAsync(ModeMontage mode, CancellationToken ct);

    Task RedemarrerAsync(CancellationToken ct);

    Task<ResultatCommande> ExecuterAsync(string commande, TimeSpan delai, string repertoire = "/workspace", string? entree = null, CancellationToken ct = default);

    Task<ResultatCommande> ExecuterIsoleAsync(string commande, TimeSpan delai, CancellationToken ct);

    Task<string?> ExporterAsync(string repertoireHote, CancellationToken ct);

    Task SupprimerAsync(bool avecVolume, CancellationToken ct);

    Task<bool> EspaceDeTravailExisteAsync(CancellationToken ct);

    Task<bool> DockerRepondAsync(CancellationToken ct);
}

public interface IFabriqueBacASable
{
    IBacASable Creer(string idDemande);
}
