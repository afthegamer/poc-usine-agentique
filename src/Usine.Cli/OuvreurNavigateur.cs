using System.ComponentModel;
using System.Diagnostics;

namespace Usine.Cli;

public interface IOuvreurNavigateur
{
    bool Ouvrir(string adresse);
}

public sealed class OuvreurNavigateur : IOuvreurNavigateur
{
    public bool Ouvrir(string adresse)
    {
        try
        {
            using var processus = Process.Start(new ProcessStartInfo(adresse) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
