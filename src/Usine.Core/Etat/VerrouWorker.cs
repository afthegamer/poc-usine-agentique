using System.Diagnostics;
using System.Globalization;

namespace Usine.Core.Etat;

public sealed class VerrouWorker : IDisposable
{
    private readonly FileStream flux;

    private VerrouWorker(FileStream flux)
    {
        this.flux = flux;
    }

    public static VerrouWorker? Acquerir(string chemin)
    {
        for (var essai = 0; essai < 2; essai++)
        {
            try
            {
                var flux = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                var contenu = System.Text.Encoding.UTF8.GetBytes(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                flux.Write(contenu);
                flux.Flush(flushToDisk: true);
                return new VerrouWorker(flux);
            }
            catch (IOException) when (File.Exists(chemin))
            {
                if (EstActif(chemin))
                {
                    return null;
                }

                try
                {
                    File.Delete(chemin);
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    public static bool EstActif(string chemin)
    {
        if (!File.Exists(chemin))
        {
            return false;
        }

        DateTime ecriture;
        try
        {
            ecriture = File.GetLastWriteTimeUtc(chemin);
        }
        catch (IOException)
        {
            return false;
        }

        return ProcessusVivant(LirePid(chemin), ecriture);
    }

    public void Dispose() => flux.Dispose();

    private static int? LirePid(string chemin)
    {
        try
        {
            using var lecteur = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var texte = new StreamReader(lecteur);
            return int.TryParse(texte.ReadToEnd().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ? pid : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ProcessusVivant(int? pid, DateTime ecritureVerrouUtc)
    {
        if (pid is null)
        {
            return false;
        }

        try
        {
            using var processus = Process.GetProcessById(pid.Value);
            return !processus.HasExited && processus.StartTime.ToUniversalTime() <= ecritureVerrouUtc.AddSeconds(5);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
