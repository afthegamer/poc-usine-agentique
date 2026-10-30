using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace Usine.Core.Configuration;

public static class ChargeurConfig
{
    public const string VariableRepertoire = "USINE_HOME";

    public const string VariableFichier = "USINE_CONFIG";

    public const string VariableProjets = "projets";

    public const string FichierProjet = "appsettings.json";

    public const string FichierLocal = "appsettings.Local.json";

    public const string OrigineSecrets = "User Secrets";

    public const string OrigineEnvironnement = "variable d'environnement";

    public const string OrigineParametresInterface = "paramètres de l'interface";

    private static readonly Action<BinderOptions> Strict = options => options.ErrorOnUnknownConfiguration = true;

    public static ConfigurationUsine Resoudre() =>
        Resoudre(AppContext.BaseDirectory, Assembly.GetEntryAssembly()?.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId);

    internal static ConfigurationUsine Resoudre(string repertoireExecutable, string? identifiantSecrets, IDataProtectionProvider? protection = null)
    {
        var fichiers = Fichiers(repertoireExecutable);
        var constructeur = new ConfigurationBuilder();
        var origines = new List<string>();
        foreach (var fichier in fichiers)
        {
            Charger(fichier);
            constructeur.AddJsonFile(fichier, optional: false, reloadOnChange: false);
            origines.Add(fichier);
        }

        if (identifiantSecrets is not null)
        {
            constructeur.AddUserSecrets(identifiantSecrets);
            origines.Add(OrigineSecrets);
        }

        constructeur.AddEnvironmentVariables();
        origines.Add(OrigineEnvironnement);
        var sources = constructeur.Build();
        var config = Lier(sources, origines);
        var repertoireBase = Path.GetDirectoryName(fichiers[0])!;
        var repertoireDonnees = RepertoireDonnees(config, repertoireBase, fichiers);
        var avecParametresInterface = DepotParametresInterface.Present(repertoireDonnees);
        if (avecParametresInterface)
        {
            var depot = new DepotParametresInterface(repertoireDonnees, protection ?? DepotParametresInterface.ProtectionParDefaut());
            var source = new MemoryConfigurationSource { InitialData = SourceParametresInterface.Entrees(depot.Lire(), depot.IasEnregistrees(), depot.LireCles()) };
            var avantEnvironnement = constructeur.Sources.Count - 1;
            constructeur.Sources.Insert(avantEnvironnement, source);
            origines.Insert(avantEnvironnement, OrigineParametresInterface);
            sources = constructeur.Build();
            config = Lier(sources, origines);
        }

        Normaliser(config, repertoireBase);
        return new ConfigurationUsine(config, fichiers, FichierImpose(), repertoireDonnees, RepertoireProjets(config, repertoireExecutable), new ClesApi(sources, origines), avecParametresInterface);
    }

    public static IReadOnlyList<string> Fichiers() => Fichiers(AppContext.BaseDirectory);

    internal static IReadOnlyList<string> Fichiers(string repertoireExecutable)
    {
        if (FichierImpose() is { } impose)
        {
            return [impose];
        }

        var racine = TrouverRacine(repertoireExecutable);
        var projet = Path.Combine(racine, FichierProjet);
        var local = Path.Combine(racine, FichierLocal);
        return File.Exists(local) ? [projet, local] : [projet];
    }

    public static string RepertoireDonnees()
    {
        var personnalise = Environment.GetEnvironmentVariable(VariableRepertoire);
        return string.IsNullOrWhiteSpace(personnalise) ? Resoudre().RepertoireDonnees : Path.GetFullPath(personnalise);
    }

    public static string RepertoireProjets()
    {
        var transmis = Environment.GetEnvironmentVariable(VariableProjets);
        return transmis is not null && Path.IsPathFullyQualified(transmis) ? Path.GetFullPath(transmis) : Resoudre().RepertoireProjets;
    }

    public static string? FichierImpose()
    {
        var personnalise = Environment.GetEnvironmentVariable(VariableFichier);
        return string.IsNullOrWhiteSpace(personnalise) ? null : Path.GetFullPath(personnalise);
    }

    public static string TrouverRacine(string depart) =>
        ChercherRacine(depart) ?? throw new InvalidOperationException(
            $"Configuration introuvable : {FichierProjet} est attendu à la racine du dépôt de l'usine, dans « {depart} » ou l'un de ses dossiers parents. Lance l'usine construite dans le dépôt (make start), ou désigne un fichier de configuration avec la variable {VariableFichier}.");

    public static ConfigUsine Charger(string chemin)
    {
        if (!File.Exists(chemin))
        {
            throw new InvalidOperationException($"Le fichier de configuration « {chemin} » est introuvable.");
        }

        var config = new ConfigUsine();
        try
        {
            new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(chemin), optional: false, reloadOnChange: false).Build().Bind(config, Strict);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or FormatException)
        {
            throw new InvalidOperationException($"Le fichier de configuration « {chemin} » est invalide : {Messages(ex)}", ex);
        }

        return Normaliser(config, Path.GetDirectoryName(chemin)!);
    }

    public static string? ChercherRacine(string depart)
    {
        for (var repertoire = new DirectoryInfo(depart); repertoire is not null; repertoire = repertoire.Parent)
        {
            if (File.Exists(Path.Combine(repertoire.FullName, FichierProjet)))
            {
                return repertoire.FullName;
            }
        }

        return null;
    }

    private static string RepertoireProjets(ConfigUsine config, string repertoireExecutable) =>
        Path.IsPathFullyQualified(config.Projets)
            ? Path.GetFullPath(config.Projets)
            : Path.GetFullPath(config.Projets, ChercherRacine(repertoireExecutable) ?? throw new InvalidOperationException(
                $"Dossier des projets introuvable : « {config.Projets} » se lit par rapport à la racine du dépôt de l'usine, où {FichierProjet} est attendu, dans « {repertoireExecutable} » ou l'un de ses dossiers parents. Lance l'usine construite dans le dépôt (make start), ou donne un chemin absolu au réglage « projets »."));

    private static string RepertoireDonnees(ConfigUsine config, string repertoireBase, IReadOnlyList<string> fichiers)
    {
        var personnalise = Environment.GetEnvironmentVariable(VariableRepertoire);
        if (!string.IsNullOrWhiteSpace(personnalise))
        {
            return Path.GetFullPath(personnalise);
        }

        return string.IsNullOrWhiteSpace(config.Donnees)
            ? throw new InvalidOperationException($"Aucun dossier de données : renseigne « donnees » dans la configuration ({string.Join(", ", fichiers)}) ou définis la variable {VariableRepertoire}.")
            : Path.GetFullPath(config.Donnees, repertoireBase);
    }

    private static ConfigUsine Lier(IConfigurationRoot sources, IReadOnlyList<string> origines)
    {
        var config = new ConfigUsine();
        try
        {
            sources.GetSection(nameof(ConfigUsine.Fournisseurs)).Bind(config.Fournisseurs, Strict);
            sources.GetSection(nameof(ConfigUsine.Roles)).Bind(config.Roles, Strict);
            sources.GetSection(nameof(ConfigUsine.EffortsRaisonnement)).Bind(config.EffortsRaisonnement, Strict);
            sources.GetSection(nameof(ConfigUsine.BacASable)).Bind(config.BacASable, Strict);
            sources.GetSection(nameof(ConfigUsine.Limites)).Bind(config.Limites, Strict);
            config.Donnees = sources[nameof(ConfigUsine.Donnees)];
            if (sources[nameof(ConfigUsine.Projets)] is { } projets && !string.IsNullOrWhiteSpace(projets))
            {
                config.Projets = projets;
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"La configuration ({string.Join(", ", origines)}) est invalide : {Messages(ex)}", ex);
        }

        return config;
    }

    private static ConfigUsine Normaliser(ConfigUsine config, string repertoireBase)
    {
        config.Fournisseurs = new Dictionary<string, ConfigFournisseur>(config.Fournisseurs, StringComparer.OrdinalIgnoreCase);
        foreach (var fournisseur in config.Fournisseurs.Values)
        {
            fournisseur.Tarifs = new Dictionary<string, TarifModele>(fournisseur.Tarifs, StringComparer.OrdinalIgnoreCase);
            fournisseur.TokensEntreeParMinute = new Dictionary<string, int>(fournisseur.TokensEntreeParMinute, StringComparer.OrdinalIgnoreCase);
            if (fournisseur.Scenario is { Length: > 0 } scenario && !Path.IsPathRooted(scenario))
            {
                fournisseur.Scenario = Path.GetFullPath(Path.Combine(repertoireBase, scenario));
            }
        }

        return config;
    }

    private static string Messages(Exception ex)
    {
        var messages = new List<string>();
        for (var courante = ex; courante is not null; courante = courante.InnerException)
        {
            messages.Add(courante.Message);
        }

        return string.Join(" ", messages.Distinct(StringComparer.Ordinal));
    }
}
