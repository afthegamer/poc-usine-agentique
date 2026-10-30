using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Usine.Core;

namespace Usine.Mcp;

public static class ServeurMcp
{
    public const string Instructions =
        "Usine agentique locale : transforme une demande (site, application, nouvelle fonctionnalité) en logiciel vérifié par des tests. "
        + "Lance avec lancer_demande, puis suis avec voir_avancement. Pour faire évoluer une application déjà produite par l'usine, passe projet=<nom> à lancer_demande (lister_projets donne les noms) : son dossier est mis à jour à la fin, sans nouveau dossier. L'usine s'arrête pour poser des questions (repondre_questions) "
        + "et faire valider le besoin compris par le PO, le backlog, les tests puis la livraison (valider_etape). Une demande dure de quelques minutes à une heure. "
        + "Pour refuser une livraison sur un défaut que les tests ne couvrent pas, le demandeur peut d'abord déposer de nouveaux tests dans le dossier des tests d'acceptation de la demande : ils sont contrôlés puis verrouillés avec les autres au refus (voir valider_etape). "
        + "Une demande échouée se reprend à l'étape de son échec avec reprendre_demande, une fois la cause corrigée.";

    public static async Task ExecuterAsync(ContexteUsine usine, CancellationToken ct, bool autoApresBacklog = false)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(usine);
        builder.Services.AddSingleton(new OptionsServeur(autoApresBacklog));
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "usine-agentique", Version = "1.0.0" };
                options.ServerInstructions = Instructions;
            })
            .WithStdioServerTransport()
            .WithTools<OutilsUsine>();
        await builder.Build().RunAsync(ct);
    }
}
