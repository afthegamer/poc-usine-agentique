using Usine.Core.Agents;
using Usine.Core.BacASable;
using Usine.Core.Configuration;
using Usine.Core.Etat;
using Usine.Core.Modeles;
using Usine.Core.Oracle;
using Usine.Core.Orchestration;
using Usine.Core.Services;

namespace Usine.Core;

public sealed class ContexteUsine
{
    private ContexteUsine(ConfigUsine config, DepotDemandes depot, IFabriqueBacASable fabriqueBac, ILanceurWorker lanceur, IFabriqueClientsChat fabriqueClients, IExecuteurAgent executeur, IOracle oracle)
    {
        Config = config;
        Depot = depot;
        FabriqueClients = fabriqueClients;
        Orchestrateur = new Orchestrateur(depot, config, fabriqueBac, executeur, oracle);
        Service = new ServiceDemandes(depot, lanceur, fabriqueBac, Orchestrateur);
    }

    public ConfigUsine Config { get; }

    public DepotDemandes Depot { get; }

    public IFabriqueClientsChat FabriqueClients { get; }

    public ServiceDemandes Service { get; }

    public Orchestrateur Orchestrateur { get; }

    public static ContexteUsine Creer(
        ConfigUsine? config = null,
        string? racine = null,
        string? projets = null,
        Func<DepotDemandes, ILanceurWorker>? lanceur = null,
        IFabriqueBacASable? fabriqueBac = null)
    {
        var resolue = config is null || racine is null || projets is null ? ChargeurConfig.Resoudre() : null;
        var configuration = config ?? resolue!.Config;
        var depot = new DepotDemandes(racine ?? resolue!.RepertoireDonnees, projets ?? resolue!.RepertoireProjets);
        var bacs = fabriqueBac ?? new FabriqueBacASableDocker(configuration, depot);
        var clients = new FabriqueClientsChat(configuration, resolue?.Cles);
        return new ContexteUsine(
            configuration,
            depot,
            bacs,
            lanceur?.Invoke(depot) ?? new LanceurWorkerProcessus(depot),
            clients,
            new ExecuteurAgent(clients, configuration),
            new OracleAcceptation(configuration));
    }
}
