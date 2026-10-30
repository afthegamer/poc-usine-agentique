using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Usine.Core.Configuration;
using Usine.Core.Etat;

namespace Usine.Core.Services;

public interface ILanceurWorker
{
    void Lancer(string idDemande);

    bool EstActif(string idDemande);
}

public sealed class LanceurWorkerProcessus(DepotDemandes depot) : ILanceurWorker
{
    public void Lancer(string idDemande)
    {
        if (EstActif(idDemande))
        {
            return;
        }

        var (executable, prefixe) = CommandeUsine();
        Demarrer(depot.CheminLancementWorker(idDemande), () =>
        {
            if (OperatingSystem.IsWindows())
            {
                LancerHorsJob(LigneDeCommande(executable, prefixe, idDemande), depot.Racine, EnvironnementDuWorker(Environment.GetEnvironmentVariables()));
            }
            else
            {
                using var processus = Process.Start(InfoLancementUnixDuWorker(executable, prefixe, idDemande))
                    ?? throw new InvalidOperationException("aucun processus n'a été démarré.");
            }
        });

        depot.Journaliser(idDemande, $"Worker lancé ({executable} {string.Join(' ', prefixe)} worker {idDemande}).");
    }

    public bool EstActif(string idDemande)
    {
        if (VerrouWorker.EstActif(depot.CheminVerrouWorker(idDemande)))
        {
            return true;
        }

        var lancement = depot.CheminLancementWorker(idDemande);
        return File.Exists(lancement) && DateTime.UtcNow - File.GetLastWriteTimeUtc(lancement) < DelaiDemarrageWorker;
    }

    internal static void Demarrer(string lancement, Action demarrage)
    {
        File.WriteAllText(lancement, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        try
        {
            demarrage();
        }
        catch (Exception ex) when (ex is Win32Exception or COMException or UnauthorizedAccessException or InvalidOperationException or ManagementException)
        {
            File.Delete(lancement);
            throw new InvalidOperationException($"Impossible de lancer le worker de l'usine : {ex.Message}", ex);
        }
    }

    private static readonly TimeSpan DelaiDemarrageWorker = TimeSpan.FromSeconds(30);

    private static readonly string[] VariablesChemins = [ChargeurConfig.VariableRepertoire, ChargeurConfig.VariableFichier, ChargeurConfig.VariableProjets, DepotParametresInterface.VariableTrousseau];

    [SupportedOSPlatform("windows")]
    private static void LancerHorsJob(string ligneDeCommande, string racine, string[] environnement)
    {
        using var classeDemarrage = new ManagementClass("Win32_ProcessStartup");
        using var demarrage = classeDemarrage.CreateInstance();
        demarrage["ShowWindow"] = (ushort)0;
        demarrage["EnvironmentVariables"] = environnement;
        using var classeProcessus = new ManagementClass("Win32_Process");
        using var parametres = classeProcessus.GetMethodParameters("Create");
        parametres["CommandLine"] = ligneDeCommande;
        parametres["CurrentDirectory"] = racine;
        parametres["ProcessStartupInformation"] = demarrage;
        using var resultat = classeProcessus.InvokeMethod("Create", parametres, null);
        var code = Convert.ToUInt32(resultat["ReturnValue"], CultureInfo.InvariantCulture);
        if (code != 0)
        {
            throw new InvalidOperationException($"Win32_Process.Create a renvoyé le code {code.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    internal static string LigneDeCommande(string executable, IReadOnlyList<string> prefixe, string idDemande) =>
        string.Join(' ', new[] { executable }.Concat(prefixe).Append("worker").Append(idDemande).Select(Citer));

    internal static string Citer(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return argument;
        }

        var resultat = new StringBuilder("\"");
        var barres = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                barres++;
                continue;
            }

            if (c == '"')
            {
                resultat.Append('\\', barres * 2 + 1).Append('"');
                barres = 0;
                continue;
            }

            resultat.Append('\\', barres).Append(c);
            barres = 0;
        }

        return resultat.Append('\\', barres * 2).Append('"').ToString();
    }

    internal static string? CheminTrousseau(string? variable) => string.IsNullOrWhiteSpace(variable) ? null : Path.GetFullPath(variable);

    internal string[] EnvironnementDuWorker(IDictionary variables)
    {
        var (cheminConfig, trousseau) = CheminsTransmis();
        return Environnement(variables, depot.Racine, depot.RepertoireProjets, cheminConfig, trousseau);
    }

    internal ProcessStartInfo InfoLancementUnixDuWorker(string executable, IReadOnlyList<string> prefixe, string idDemande)
    {
        var (cheminConfig, trousseau) = CheminsTransmis();
        return InfoLancementUnix(executable, prefixe, idDemande, depot.Racine, depot.RepertoireProjets, cheminConfig, trousseau);
    }

    private static (string? Configuration, string? Trousseau) CheminsTransmis() =>
        (ChargeurConfig.FichierImpose(), CheminTrousseau(Environment.GetEnvironmentVariable(DepotParametresInterface.VariableTrousseau)));

    internal static string[] Environnement(IDictionary variables, string racine, string projets, string? cheminConfig, string? trousseau) =>
        variables.Cast<DictionaryEntry>()
            .Where(e => !VariablesChemins.Contains($"{e.Key}", StringComparer.OrdinalIgnoreCase))
            .Select(e => $"{e.Key}={e.Value}")
            .Append($"{ChargeurConfig.VariableRepertoire}={racine}")
            .Append($"{ChargeurConfig.VariableProjets}={projets}")
            .Concat(cheminConfig is null ? [] : [$"{ChargeurConfig.VariableFichier}={cheminConfig}"])
            .Concat(trousseau is null ? [] : [$"{DepotParametresInterface.VariableTrousseau}={trousseau}"])
            .Order(StringComparer.Ordinal)
            .ToArray();

    internal static ProcessStartInfo InfoLancementUnix(string executable, IReadOnlyList<string> prefixe, string idDemande, string racine, string projets, string? cheminConfig, string? trousseau)
    {
        var commande = new List<string>(prefixe) { "worker", idDemande };
        var ligne = string.Join(' ', new[] { executable }.Concat(commande).Select(a => "'" + a.Replace("'", "'\\''", StringComparison.Ordinal) + "'"));
        var info = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = racine,
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add($"nohup {ligne} >/dev/null 2>&1 &");
        info.Environment[ChargeurConfig.VariableRepertoire] = racine;
        info.Environment[ChargeurConfig.VariableProjets] = projets;
        if (cheminConfig is not null)
        {
            info.Environment[ChargeurConfig.VariableFichier] = cheminConfig;
        }

        if (trousseau is not null)
        {
            info.Environment[DepotParametresInterface.VariableTrousseau] = trousseau;
        }

        return info;
    }

    public static (string Executable, IReadOnlyList<string> Prefixe) CommandeUsine()
    {
        var processus = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin du processus inconnu.");
        var nom = Path.GetFileNameWithoutExtension(processus);
        if (nom.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entree = Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("Assemblage d'entrée inconnu.");
            return (processus, [entree]);
        }

        return (processus, []);
    }
}
