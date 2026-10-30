using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace Usine.Core.Configuration;

public sealed class DepotParametresInterface(string repertoireDonnees, IDataProtectionProvider protection)
{
    public const string FichierParametres = "parametres-interface.json";

    public const string FichierCles = "cles-interface.json";

    public const string VariableTrousseau = "USINE_TROUSSEAU";

    public const string NomApplication = "usine";

    public const string But = "Usine.ParametresInterface.Cles";

    private static readonly Lock Ecriture = new();

    private static readonly JsonSerializerOptions Options = new(Json.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NewLine = "\n",
        RespectNullableAnnotations = true,
    };

    private readonly IDataProtector protecteur = protection.CreateProtector(But);

    public string CheminParametres { get; } = Path.Combine(repertoireDonnees, FichierParametres);

    public string CheminCles { get; } = Path.Combine(repertoireDonnees, FichierCles);

    public static bool Present(string repertoireDonnees) =>
        File.Exists(Path.Combine(repertoireDonnees, FichierParametres)) || File.Exists(Path.Combine(repertoireDonnees, FichierCles));

    public static IDataProtectionProvider ProtectionParDefaut() => Protection(Environment.GetEnvironmentVariable(VariableTrousseau));

    public static IDataProtectionProvider Protection(string? trousseau) =>
        string.IsNullOrWhiteSpace(trousseau)
            ? DataProtectionProvider.Create(NomApplication)
            : DataProtectionProvider.Create(new DirectoryInfo(trousseau), constructeur =>
            {
                constructeur.SetApplicationName(NomApplication);
                if (OperatingSystem.IsWindows())
                {
                    constructeur.ProtectKeysWithDpapi();
                }
            });

    public ParametresInterface Lire() => Lire<ParametresInterface>(CheminParametres) ?? new ParametresInterface();

    public void Ecrire(ParametresInterface parametres)
    {
        lock (Ecriture)
        {
            EcrireJson(CheminParametres, parametres);
        }
    }

    public void EnregistrerCle(string ia, string cle)
    {
        lock (Ecriture)
        {
            var cles = ClesProtegees();
            cles[ia] = protecteur.Protect(cle);
            EcrireJson(CheminCles, cles);
        }
    }

    public bool RetirerCle(string ia)
    {
        lock (Ecriture)
        {
            var cles = ClesProtegees();
            if (!cles.Remove(ia))
            {
                return false;
            }

            EcrireJson(CheminCles, cles);
            return true;
        }
    }

    public IReadOnlyCollection<string> IasEnregistrees() => ClesProtegees().Keys;

    public IReadOnlyDictionary<string, string> LireCles()
    {
        var cles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (ia, protegee) in ClesProtegees())
        {
            if (Dechiffrer(protegee) is { } cle)
            {
                cles[ia] = cle;
            }
        }

        return cles;
    }

    private Dictionary<string, string?> ClesProtegees() => Lire<Dictionary<string, string?>>(CheminCles) ?? new Dictionary<string, string?>(StringComparer.Ordinal);

    private string? Dechiffrer(string? protegee)
    {
        if (protegee is null)
        {
            return null;
        }

        try
        {
            return protecteur.Unprotect(protegee);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static T? Lire<T>(string chemin)
        where T : class
    {
        if (!File.Exists(chemin))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(LireAvecReessais(chemin), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Le fichier des paramètres de l'interface « {chemin} » est invalide : {ex.Message}", ex);
        }
    }

    private static void EcrireJson<T>(string chemin, T valeur)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        var temporaire = $"{chemin}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaire, JsonSerializer.Serialize(valeur, Options));
            for (var essai = 1; ; essai++)
            {
                try
                {
                    File.Move(temporaire, chemin, overwrite: true);
                    return;
                }
                catch (Exception ex) when (EstConflitDAcces(ex) && essai < 10)
                {
                    Thread.Sleep(50 * essai);
                }
            }
        }
        finally
        {
            File.Delete(temporaire);
        }
    }

    private static string LireAvecReessais(string chemin)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                return File.ReadAllText(chemin);
            }
            catch (Exception ex) when (EstConflitDAcces(ex) && essai < 10)
            {
                Thread.Sleep(50 * essai);
            }
        }
    }

    private static bool EstConflitDAcces(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException && ex is not FileNotFoundException and not DirectoryNotFoundException);
}
