using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Usine.Api.Dto;
using Usine.Core;
using Usine.Core.Configuration;

namespace Usine.Api;

public static class ServeurApi
{
    public const int PortParDefaut = 5180;

    private const string PageDeLEcran = "index.html";

    private static readonly string[] FluxAvecJetonDansLAdresse = ["/api/flux", "/api/discussions/{id}/flux"];

    public static string TirerJeton() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static WebApplication Construire(ContexteUsine usine, int port, string jeton, string? ecran, TimeProvider? temps = null, ICommandesDocker? docker = null, IFabriqueDiscussions? discussions = null, Func<ConfigurationUsine>? configuration = null, HttpClient? clientIa = null, Func<IDataProtectionProvider>? protection = null)
    {
        var lireConfiguration = configuration ?? ChargeurConfig.Resoudre;
        ArgumentException.ThrowIfNullOrWhiteSpace(jeton);
        var attendu = Encoding.UTF8.GetBytes(jeton);
        var fichiers = Directory.Exists(ecran) ? new PhysicalFileProvider(Path.GetFullPath(ecran)) : null;
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseKestrelCore().ConfigureKestrel(options => options.ListenLocalhost(port));
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddHostFiltering(options =>
        {
            options.AllowedHosts = ["localhost", "127.0.0.1", "[::1]"];
            options.IncludeFailureMessage = false;
        });
        builder.Services.AddRoutingCore();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<TraducteurErreurs>();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton(usine);
        builder.Services.AddSingleton(docker ?? new CommandesDocker());
        builder.Services.AddSingleton<EssaisApplications>();
        builder.Services.AddSingleton(services => new Discussions(usine, lireConfiguration, discussions ?? new FabriqueDiscussionsUsine(), services.GetRequiredService<IHostApplicationLifetime>(), temps));
        builder.Services.AddSingleton(new Parametres(lireConfiguration, clientIa ?? ClientIaParDefaut(), protection ?? DepotParametresInterface.ProtectionParDefaut, temps ?? TimeProvider.System));
        var app = builder.Build();
        app.UseExceptionHandler();
        app.Use(InterdireLesCadresAsync);
        app.UseHostFiltering();
        app.Use(RefuserLesPagesEtrangeresAsync);
        app.Use((contexte, suivant) => ExigerLeJetonAsync(contexte, suivant, attendu));
        app.Use((contexte, suivant) => ServirLEcranAsync(contexte, suivant, fichiers));
        if (fichiers is not null)
        {
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fichiers, OnPrepareResponse = NePasGarderLaPageEnCache });
        }

        app.MapOpenApi();
        app.MapGet("/api/sante", () => "OK");
        app.MapGet("/api/projets", (ContexteUsine usine) => usine.Service.ListerProjets().Select(projet => projet.VersDto()));
        app.MapGet("/api/projets/{nom}/essai", (string nom, EssaisApplications essais, CancellationToken ct) => essais.EtatAsync(nom, ct));
        app.MapPost("/api/projets/{nom}/essai", (string nom, LancementEssaiDto? corps, EssaisApplications essais, CancellationToken ct) => essais.LancerAsync(nom, corps, ct));
        app.MapDelete("/api/projets/{nom}/essai", (string nom, EssaisApplications essais, CancellationToken ct) => essais.ArreterAsync(nom, ct));
        app.MapGet("/api/demandes", (ContexteUsine usine) => usine.Service.Lister().Select(demande => demande.VersDto()));
        app.MapPost("/api/demandes", (LancementDto corps, ContexteUsine usine) => ActionsDemandes.Lancer(usine, corps));
        app.MapGet("/api/demandes/{id}", (string id, ContexteUsine usine) => usine.Service.Avancement(id).VersDetailDto());
        app.MapPost("/api/demandes/{id}/reponses", (string id, ReponsesDto corps, ContexteUsine usine) => ActionsDemandes.Repondre(usine, id, corps));
        app.MapPost("/api/demandes/{id}/validation", (string id, ValidationDto corps, ContexteUsine usine, CancellationToken ct) => ActionsDemandes.ValiderAsync(usine, id, corps, ct));
        app.MapPost("/api/demandes/{id}/annulation", async (string id, ContexteUsine usine, CancellationToken ct) => (await usine.Service.AnnulerAsync(id, ct)).VersDetailDto());
        app.MapPost("/api/demandes/{id}/reprise", async (string id, ContexteUsine usine, CancellationToken ct) => (await usine.Service.ReprendreAsync(id, ct)).VersDetailDto());
        app.MapGet("/api/flux", (ContexteUsine usine, HttpContext contexte) => TypedResults.ServerSentEvents(FluxDemandes.SuivreAsync(usine.Depot, temps ?? TimeProvider.System, contexte.Response, app.Lifetime.ApplicationStopping, contexte.RequestAborted), FluxDemandes.Evenement));
        app.MapPost("/api/discussions", (LancementDiscussionDto? corps, Discussions discussions, CancellationToken ct) => discussions.OuvrirAsync(corps, ct));
        app.MapPost("/api/discussions/{id}/messages", (string id, MessageDto corps, Discussions discussions) => discussions.Transmettre(id, corps));
        app.MapGet("/api/discussions/{id}/flux", (string id, Discussions discussions, HttpContext contexte) => TypedResults.ServerSentEvents(discussions.Trouver(id).SuivreAsync(contexte.Response, app.Lifetime.ApplicationStopping, contexte.RequestAborted)));
        app.MapPost("/api/discussions/{id}/confirmation", (string id, ConfirmationDto corps, Discussions discussions) => discussions.Confirmer(id, corps));
        app.MapDelete("/api/discussions/{id}", (string id, Discussions discussions) => discussions.FermerAsync(id));
        var routesParametres = app.MapGroup("/api/parametres").AddEndpointFilter(Parametres.FichiersIllisiblesAsync);
        routesParametres.MapGet("", (Parametres parametres, CancellationToken ct) => parametres.LireAsync(ct));
        routesParametres.MapPut("/cles/{ia}", (string ia, CleDto corps, Parametres parametres, CancellationToken ct) => parametres.EnregistrerCleAsync(ia, corps, ct));
        routesParametres.MapDelete("/cles/{ia}", (string ia, Parametres parametres, CancellationToken ct) => parametres.RetirerCleAsync(ia, ct));
        routesParametres.MapPut("/choix", (ChoixDto corps, Parametres parametres, CancellationToken ct) => parametres.ChoisirAsync(corps, ct));
        routesParametres.MapGet("/autre/modeles", (Parametres parametres, CancellationToken ct) => parametres.ModelesAutreAsync(ct));
        routesParametres.MapPut("/roles", (RolesDto corps, Parametres parametres, CancellationToken ct) => parametres.ReglerRolesAsync(corps, ct));
        routesParametres.MapPut("/ollama", (OllamaDto corps, Parametres parametres, CancellationToken ct) => parametres.ReglerOllamaAsync(corps, ct));
        return app;
    }

    internal static HttpClient ClientIaParDefaut() => new() { Timeout = Timeout.InfiniteTimeSpan };

    private static Task InterdireLesCadresAsync(HttpContext contexte, RequestDelegate suivant)
    {
        contexte.Response.OnStarting(() =>
        {
            contexte.Response.Headers.XFrameOptions = "DENY";
            return Task.CompletedTask;
        });
        return suivant(contexte);
    }

    private static async Task RefuserLesPagesEtrangeresAsync(HttpContext contexte, RequestDelegate suivant)
    {
        var origine = contexte.Request.Headers.Origin;
        if (origine.Count > 0 && !(origine.Count == 1 && Uri.TryCreate(origine[0], UriKind.Absolute, out var page) && HoteDeBoucle(page)))
        {
            await RefuserAsync(contexte, StatusCodes.Status403Forbidden, "L'API de l'usine ne répond qu'aux pages ouvertes depuis ce poste.");
            return;
        }

        await suivant(contexte);
    }

    private static async Task ExigerLeJetonAsync(HttpContext contexte, RequestDelegate suivant, byte[] jeton)
    {
        if (CheminDeLApi(contexte.Request.Path) && !JetonPresente(contexte, jeton))
        {
            contexte.Response.Headers.WWWAuthenticate = "Bearer";
            await RefuserAsync(contexte, StatusCodes.Status401Unauthorized, "Accès refusé : ouvre l'interface de l'usine depuis l'adresse affichée par « usine interface ».");
            return;
        }

        await suivant(contexte);
    }

    private static async Task ServirLEcranAsync(HttpContext contexte, RequestDelegate suivant, IFileProvider? fichiers)
    {
        var requete = contexte.Request;
        var chemin = requete.Path.Value ?? "";
        if (contexte.GetEndpoint() is not null || !(HttpMethods.IsGet(requete.Method) || HttpMethods.IsHead(requete.Method)) || CheminDeLApi(requete.Path) || chemin[(chemin.LastIndexOf('/') + 1)..].Contains('.'))
        {
            await suivant(contexte);
            return;
        }

        if (fichiers?.GetFileInfo(PageDeLEcran).Exists != true)
        {
            contexte.Response.StatusCode = StatusCodes.Status404NotFound;
            contexte.Response.ContentType = "text/plain; charset=utf-8";
            await contexte.Response.WriteAsync("L'écran de l'usine n'est pas construit : lance « make interface ».");
            return;
        }

        requete.Path = "/" + PageDeLEcran;
        await suivant(contexte);
    }

    private static void NePasGarderLaPageEnCache(StaticFileResponseContext reponse)
    {
        if (string.Equals(reponse.File.Name, PageDeLEcran, StringComparison.OrdinalIgnoreCase))
        {
            reponse.Context.Response.Headers.CacheControl = "no-cache";
        }
    }

    private static bool CheminDeLApi(PathString chemin) => chemin.StartsWithSegments("/api") || chemin.StartsWithSegments("/openapi");

    private static bool JetonPresente(HttpContext contexte, byte[] jeton) =>
        (contexte.Request.Headers.Authorization is [{ } entete] && entete.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && Egal(entete["Bearer ".Length..], jeton))
        || (contexte.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } modele } && FluxAvecJetonDansLAdresse.Contains(modele, StringComparer.Ordinal)
            && contexte.Request.Query["jeton"] is [{ } parametre] && Egal(parametre, jeton));

    private static bool Egal(string presente, byte[] jeton) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presente), jeton);

    private static async Task RefuserAsync(HttpContext contexte, int statut, string detail)
    {
        contexte.Response.StatusCode = statut;
        await contexte.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexte,
            ProblemDetails = { Status = statut, Detail = detail },
        });
    }

    private static bool HoteDeBoucle(Uri page)
    {
        var debut = page.Scheme + Uri.SchemeDelimiter;
        if (page.UserInfo.Length > 0 || !page.OriginalString.StartsWith(debut, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var autorite = page.OriginalString[debut.Length..];
        var fin = autorite.IndexOfAny(['/', '?', '#']);
        var hote = new HostString(fin < 0 ? autorite : autorite[..fin]).Host;
        return string.Equals(hote, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(hote, out var adresse) && IPAddress.IsLoopback(adresse));
    }
}