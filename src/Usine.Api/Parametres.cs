using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Usine.Api.Dto;
using Usine.Core.Configuration;

namespace Usine.Api;

internal sealed class Parametres(Func<ConfigurationUsine> lireConfiguration, HttpClient http, Func<IDataProtectionProvider> protection, TimeProvider temps)
{
    public static readonly TimeSpan DelaiAppel = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan DelaiPreparation = TimeSpan.FromSeconds(120);

    public static readonly TimeSpan DelaiDetection = TimeSpan.FromSeconds(2);

    private const int LongueurMaxCle = 500;

    private const string AdresseGemini = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1";

    private const string AdresseAnthropic = "https://api.anthropic.com/v1/models?limit=1";

    private const string AdresseInvalide = "Cette adresse n'est pas valide.";

    private readonly Lazy<IDataProtectionProvider> protecteur = new(protection);

    private readonly SemaphoreSlim ecriture = new(1, 1);

    public static async ValueTask<object?> FichiersIllisiblesAsync(EndpointFilterInvocationContext contexte, EndpointFilterDelegate suivant)
    {
        try
        {
            return await suivant(contexte);
        }
        catch (InvalidOperationException)
        {
            throw Refus(StatusCodes.Status500InternalServerError, "Les paramètres n'ont pas pu être lus. Réessaie dans un instant.");
        }
    }

    public async Task<ParametresDto> LireAsync(CancellationToken ct)
    {
        var configuration = lireConfiguration();
        var parametres = Depot(configuration).Lire();
        var ollama = await ModelesOllamaAsync(parametres.Ollama.Adresse, ct);
        var po = ModelePo(configuration);
        var iaDuPo = CatalogueIa.Ias.FirstOrDefault(catalogue => string.Equals(catalogue.Id, po?.Fournisseur, StringComparison.OrdinalIgnoreCase))?.Id;
        var ia = parametres.Ia ?? iaDuPo;
        var ias = CatalogueIa.Ias.Select(catalogue =>
        {
            IReadOnlyList<ModeleDto> modeles = catalogue.Id == CatalogueIa.Ollama
                ? [.. (ollama ?? []).Select(modele => modele.VersDto())]
                : [.. CatalogueIa.ModelesProposes(catalogue.Id).Select(modele => modele.VersDto())];
            var cle = catalogue.AvecCle ? Cle(configuration, catalogue.Id) : null;
            var connectee = catalogue.AvecCle ? cle is not null : ollama is not null;
            var modele = parametres.Modeles.Pour(catalogue.Id) ?? (catalogue.Id == ia ? ModeleDuPo(po, ia, modeles) : null);
            return new IaDto(
                catalogue.Id,
                connectee,
                Origine(cle),
                modele,
                modeles,
                catalogue.Id == CatalogueIa.Autre ? parametres.Autre?.Service : null,
                catalogue.Id switch
                {
                    CatalogueIa.Autre => parametres.Autre?.Adresse,
                    CatalogueIa.Ollama => parametres.Ollama.Adresse,
                    _ => null,
                });
        });
        var pret = configuration.Prete() && (parametres.Ia is null || parametres.Modeles.Pour(parametres.Ia) is not null);
        return new ParametresDto(pret, ia, [.. ias], parametres.Roles.VersDto());
    }

    public async Task<ParametresDto> EnregistrerCleAsync(string ia, CleDto corps, CancellationToken ct)
    {
        var nom = IaAvecCle(ia).Nom;
        var cle = corps.Cle?.Trim() ?? "";
        if (cle.Length is 0 or > LongueurMaxCle || cle.Any(caractere => caractere is < '!' or > '~'))
        {
            throw Refus(StatusCodes.Status400BadRequest, "Colle une clé d'accès valide, puis réessaie.");
        }

        ServiceAutreInterface? autre = null;
        if (ia == CatalogueIa.Autre)
        {
            var service = CatalogueIa.Services.FirstOrDefault(service => service.Id == corps.Service) ?? throw Refus(StatusCodes.Status400BadRequest, "Choisis un service dans la liste.");
            autre = new ServiceAutreInterface(service.Id, service.Adresse ?? CatalogueIa.AdresseService(corps.Adresse) ?? throw Refus(StatusCodes.Status400BadRequest, AdresseInvalide));
            nom = service.Nom;
        }

        var configuration = lireConfiguration();
        var prete = configuration.Prete();
        using var verification = ia switch
        {
            CatalogueIa.Gemini => Requete(AdresseGemini, ("x-goog-api-key", cle)),
            CatalogueIa.Anthropic => Requete(AdresseAnthropic, ("x-api-key", cle), ("anthropic-version", "2023-06-01")),
            _ => Requete($"{autre!.Adresse}/models", ("Authorization", $"Bearer {cle}")),
        };
        var reponse = await AppelerAsync(verification, DelaiAppel, ct);
        if (reponse?.Statut is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw Refus(StatusCodes.Status400BadRequest, $"{nom} refuse cette clé. Vérifie que tu l'as copiée en entier, puis réessaie.");
        }

        if (reponse?.Statut != HttpStatusCode.OK)
        {
            throw Refus(StatusCodes.Status502BadGateway, NeRepondPas(nom));
        }

        await EcrireAsync(configuration, depot =>
        {
            depot.EnregistrerCle(ia, cle);
            var parametres = depot.Lire();
            if (autre is not null && autre != parametres.Autre)
            {
                parametres = SansServiceAutre(parametres) with { Autre = autre };
            }

            if (!prete && parametres.Ia is null)
            {
                parametres = parametres with { Ia = ia, Modeles = Avec(parametres.Modeles, ia, ModeleParDefaut(ia)) };
            }

            return Task.FromResult(parametres);
        }, ct);
        return await LireAsync(ct);
    }

    public async Task<ParametresDto> RetirerCleAsync(string ia, CancellationToken ct)
    {
        IaAvecCle(ia);
        await EcrireAsync(lireConfiguration(), async depot =>
        {
            if (!depot.RetirerCle(ia))
            {
                throw Refus(StatusCodes.Status404NotFound, "Aucune clé de cette IA n'a été ajoutée dans les Paramètres.");
            }

            var parametres = depot.Lire();
            parametres = ia == CatalogueIa.Autre
                ? SansServiceAutre(parametres)
                : parametres with { Roles = RolesSans(parametres.Roles, ia) };
            return parametres.Ia == ia ? await ReplierAsync(parametres, ct) : parametres;
        }, ct);
        return await LireAsync(ct);
    }

    public async Task<ParametresDto> ChoisirAsync(ChoixDto corps, CancellationToken ct)
    {
        var configuration = lireConfiguration();
        var enregistres = Depot(configuration).Lire();
        if (await ValiderAsync(corps, configuration, enregistres, ct) is { } aPreparer)
        {
            await PreparerAsync(enregistres.Ollama.Adresse, aPreparer, ct);
        }

        await EcrireAsync(configuration, depot =>
        {
            var parametres = depot.Lire();
            return Task.FromResult(parametres with { Ia = corps.Ia, Modeles = Avec(parametres.Modeles, corps.Ia, corps.Modele) });
        }, ct);
        return await LireAsync(ct);
    }

    public async Task<IReadOnlyList<ModeleDto>> ModelesAutreAsync(CancellationToken ct)
    {
        var configuration = lireConfiguration();
        return AccesAutre(configuration) is { } acces
            ? await ModelesAutreAsync(acces, Depot(configuration).Lire(), ct)
            : throw Refus(StatusCodes.Status409Conflict, "Ajoute d'abord la clé de ce service.");
    }

    public async Task<ParametresDto> ReglerRolesAsync(RolesDto corps, CancellationToken ct)
    {
        var configuration = lireConfiguration();
        var parametres = Depot(configuration).Lire();
        var general = parametres.Ia is null
            ? await ChoixGeneralDeduitAsync(ct) ?? throw Refus(StatusCodes.Status400BadRequest, "Choisis d'abord ton IA dans Paramètres.")
            : null;
        var aPreparer = new List<string>();
        foreach (var choix in new[] { corps.Po, corps.QaAmont, corps.Dev, corps.Qa }.OfType<ChoixDto>().Distinct())
        {
            if (await ValiderAsync(choix, configuration, parametres, ct) is { } modele)
            {
                aPreparer.Add(modele);
            }
        }

        foreach (var modele in aPreparer)
        {
            await PreparerAsync(parametres.Ollama.Adresse, modele, ct);
        }

        await EcrireAsync(configuration, depot =>
        {
            var enregistres = depot.Lire();
            if (enregistres.Ia is null && general is not null)
            {
                enregistres = enregistres with { Ia = general.Ia, Modeles = Avec(enregistres.Modeles, general.Ia, general.Modele) };
            }

            return Task.FromResult(enregistres with { Roles = corps.VersRoles() });
        }, ct);
        return await LireAsync(ct);
    }

    public async Task<ParametresDto> ReglerOllamaAsync(OllamaDto corps, CancellationToken ct)
    {
        var adresse = CatalogueIa.AdresseOllama(corps.Adresse) ?? throw Refus(StatusCodes.Status400BadRequest, AdresseInvalide);
        await EcrireAsync(lireConfiguration(), depot => Task.FromResult(depot.Lire() with { Ollama = new OllamaInterface { Adresse = adresse } }), ct);
        return await LireAsync(ct);
    }

    private async Task<string?> ValiderAsync(ChoixDto choix, ConfigurationUsine configuration, ParametresInterface parametres, CancellationToken ct)
    {
        IReadOnlyList<ModeleDto>? modeles = choix.Ia switch
        {
            CatalogueIa.Gemini or CatalogueIa.Anthropic when Cle(configuration, choix.Ia) is not null => [.. CatalogueIa.ModelesProposes(choix.Ia).Select(modele => modele.VersDto())],
            CatalogueIa.Autre when AccesAutre(configuration) is { } acces => await ModelesAutreAsync(acces, parametres, ct),
            CatalogueIa.Ollama when await ModelesOllamaAsync(parametres.Ollama.Adresse, ct) is { } ollama => [.. ollama.Select(modele => modele.VersDto())],
            _ => null,
        };
        if (modeles is null)
        {
            throw Refus(StatusCodes.Status400BadRequest, "Connecte d'abord cette IA.");
        }

        var modele = modeles.FirstOrDefault(modele => modele.Id == choix.Modele) ?? throw Refus(StatusCodes.Status400BadRequest, "Ce modèle n'est pas proposé.");
        return modele.Prepare == false ? modele.Id : null;
    }

    private async Task<ChoixDto?> ChoixGeneralDeduitAsync(CancellationToken ct)
    {
        var etat = await LireAsync(ct);
        return etat is { Pret: true, Ia: { } ia } && etat.Ias.First(dto => dto.Id == ia).Modele is { } modele ? new ChoixDto(ia, modele) : null;
    }

    private async Task<ParametresInterface> ReplierAsync(ParametresInterface parametres, CancellationToken ct)
    {
        var configuration = lireConfiguration();
        foreach (var ia in new[] { CatalogueIa.Gemini, CatalogueIa.Anthropic, CatalogueIa.Autre })
        {
            if (Cle(configuration, ia) is not null && (parametres.Modeles.Pour(ia) ?? ModeleParDefaut(ia)) is { } modele)
            {
                return parametres with { Ia = ia, Modeles = Avec(parametres.Modeles, ia, modele) };
            }
        }

        var ollamaUtilisable = parametres.Modeles.Ollama is not null && await ModelesOllamaAsync(parametres.Ollama.Adresse, ct) is not null;
        return parametres with { Ia = ollamaUtilisable ? CatalogueIa.Ollama : null };
    }

    private async Task<IReadOnlyList<ModeleDto>> ModelesAutreAsync((string Adresse, string Cle) acces, ParametresInterface parametres, CancellationToken ct)
    {
        using var requete = Requete($"{acces.Adresse}/models", ("Authorization", $"Bearer {acces.Cle}"));
        var reponse = await AppelerAsync(requete, DelaiAppel, ct);
        var ids = reponse is { Statut: HttpStatusCode.OK } ? Noms(reponse.Value.Corps, "data", "id") : null;
        if (ids is null)
        {
            var nom = CatalogueIa.Services.FirstOrDefault(service => service.Id == parametres.Autre?.Service)?.Nom ?? IaAvecCle(CatalogueIa.Autre).Nom;
            throw Refus(StatusCodes.Status502BadGateway, NeRepondPas(nom));
        }

        return [.. ids.Order(StringComparer.Ordinal).Select(id => new ModeleDto(id, id, null, false))];
    }

    private async Task<IReadOnlyList<ModeleOllama>?> ModelesOllamaAsync(string adresse, CancellationToken ct)
    {
        using var requete = Requete($"{adresse}/api/tags");
        var reponse = await AppelerAsync(requete, DelaiDetection, ct);
        return reponse is { Statut: HttpStatusCode.OK } && Noms(reponse.Value.Corps, "models", "name") is { } noms ? CatalogueIa.ModelesOllama(noms) : null;
    }

    private async Task PreparerAsync(string adresse, string modele, CancellationToken ct)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Post, $"{adresse}/api/create")
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["model"] = CatalogueIa.NomPrepare(modele),
                ["from"] = modele,
                ["parameters"] = new JsonObject { ["num_ctx"] = CatalogueIa.ContextePreparation, ["num_predict"] = CatalogueIa.SortiePreparation },
                ["stream"] = false,
            }),
        };
        if ((await AppelerAsync(requete, DelaiPreparation, ct))?.Statut != HttpStatusCode.OK)
        {
            throw Refus(StatusCodes.Status502BadGateway, "Ollama n'a pas pu préparer ce modèle. Réessaie dans un instant.");
        }
    }

    private async Task<(HttpStatusCode Statut, string Corps)?> AppelerAsync(HttpRequestMessage requete, TimeSpan delai, CancellationToken ct)
    {
        using var limite = new CancellationTokenSource(delai, temps);
        using var arret = CancellationTokenSource.CreateLinkedTokenSource(ct, limite.Token);
        try
        {
            using var reponse = await http.SendAsync(requete, arret.Token);
            return (reponse.StatusCode, await reponse.Content.ReadAsStringAsync(arret.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    private async Task EcrireAsync(ConfigurationUsine configuration, Func<DepotParametresInterface, Task<ParametresInterface>> modifier, CancellationToken ct)
    {
        await ecriture.WaitAsync(ct);
        try
        {
            var depot = Depot(configuration);
            depot.Ecrire(await modifier(depot));
        }
        finally
        {
            ecriture.Release();
        }
    }

    private DepotParametresInterface Depot(ConfigurationUsine configuration) => new(configuration.RepertoireDonnees, protecteur.Value);

    private static CleApi? Cle(ConfigurationUsine configuration, string ia) =>
        configuration.Config.Fournisseurs.TryGetValue(ia, out var fournisseur) && !string.IsNullOrWhiteSpace(fournisseur.VariableCleApi)
            ? configuration.Cles.Trouver(fournisseur.VariableCleApi)
            : null;

    private static (string Adresse, string Cle)? AccesAutre(ConfigurationUsine configuration) =>
        configuration.Config.Fournisseurs.TryGetValue(CatalogueIa.Autre, out var fournisseur)
        && Uri.TryCreate(fournisseur.UrlBase, UriKind.Absolute, out _)
        && Cle(configuration, CatalogueIa.Autre) is { } cle
            ? (fournisseur.UrlBase.TrimEnd('/'), cle.Valeur)
            : null;

    private static ReferenceModele? ModelePo(ConfigurationUsine configuration)
    {
        try
        {
            return configuration.Config.ModelePour(RoleAgent.Po);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? ModeleDuPo(ReferenceModele? po, string ia, IReadOnlyList<ModeleDto> modeles) =>
        po is not null && string.Equals(po.Fournisseur, ia, StringComparison.OrdinalIgnoreCase) && modeles.Any(modele => modele.Id == po.Modele) ? po.Modele : null;

    private static string? ModeleParDefaut(string ia) => CatalogueIa.ModelesProposes(ia).FirstOrDefault(modele => modele.ParDefaut)?.Id;

    private static ModelesInterface Avec(ModelesInterface modeles, string ia, string? modele) => ia switch
    {
        CatalogueIa.Gemini => modeles with { Gemini = modele },
        CatalogueIa.Anthropic => modeles with { Anthropic = modele },
        CatalogueIa.Autre => modeles with { Autre = modele },
        CatalogueIa.Ollama => modeles with { Ollama = modele },
        _ => modeles,
    };

    private static string? Origine(CleApi? cle) => cle switch
    {
        null => null,
        { Origine: ChargeurConfig.OrigineParametresInterface } => "interface",
        _ => "poste",
    };

    private static ParametresInterface SansServiceAutre(ParametresInterface parametres) =>
        parametres with { Autre = null, Modeles = parametres.Modeles with { Autre = null }, Roles = RolesSans(parametres.Roles, CatalogueIa.Autre) };

    private static RolesInterface RolesSans(RolesInterface roles, string ia) =>
        roles with { Po = SaufIa(roles.Po, ia), QaAmont = SaufIa(roles.QaAmont, ia), Dev = SaufIa(roles.Dev, ia), Qa = SaufIa(roles.Qa, ia) };

    private static string? SaufIa(string? reglage, string ia) => reglage?.StartsWith($"{ia}:", StringComparison.Ordinal) == true ? null : reglage;

    private static IaCatalogue IaAvecCle(string ia) =>
        CatalogueIa.Ias.FirstOrDefault(catalogue => catalogue.Id == ia && catalogue.AvecCle) ?? throw Refus(StatusCodes.Status404NotFound, "Cette IA n'est pas proposée.");

    private static HttpRequestMessage Requete(string adresse, params ReadOnlySpan<(string Nom, string Valeur)> entetes)
    {
        var requete = new HttpRequestMessage(HttpMethod.Get, adresse);
        foreach (var (nom, valeur) in entetes)
        {
            requete.Headers.TryAddWithoutValidation(nom, valeur);
        }

        return requete;
    }

    private static List<string>? Noms(string corps, string tableau, string champ)
    {
        try
        {
            if (JsonNode.Parse(corps)?[tableau] is not JsonArray elements)
            {
                return null;
            }

            return [.. elements.Select(element => element?[champ]?.GetValue<string>()).OfType<string>()];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string NeRepondPas(string nom) => $"{nom} ne répond pas. Réessaie dans un instant.";

    private static RefusParametresException Refus(int statut, string detail) => new(statut, detail);
}

internal sealed class RefusParametresException(int statut, string detail) : Exception(detail)
{
    public int Statut { get; } = statut;
}
