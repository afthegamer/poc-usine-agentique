using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Usine.Core.BacASable;

public sealed record ResultatCommande(int Code, string Sortie, string Erreurs, bool DelaiDepasse)
{
    public bool Reussi => Code == 0 && !DelaiDepasse;

    public string SortieComplete => string.IsNullOrEmpty(Erreurs) ? Sortie : string.IsNullOrEmpty(Sortie) ? Erreurs : Sortie + "\n" + Erreurs;
}

public static class ExecuteurProcessus
{
    public const int TailleMaxCapture = 1_000_000;

    public static async Task<ResultatCommande> ExecuterAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan delai,
        string? entree = null,
        CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = entree is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (entree is not null)
        {
            info.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var processus = new Process { StartInfo = info };
        var sortie = new CaptureBornee(TailleMaxCapture);
        var erreurs = new CaptureBornee(TailleMaxCapture);
        processus.OutputDataReceived += (_, e) => sortie.Ajouter(e.Data);
        processus.ErrorDataReceived += (_, e) => erreurs.Ajouter(e.Data);

        try
        {
            processus.Start();
        }
        catch (Win32Exception ex)
        {
            return new ResultatCommande(-1, "", $"Impossible de lancer « {executable} » : {ex.Message}", false);
        }

        processus.BeginOutputReadLine();
        processus.BeginErrorReadLine();

        using var delaiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delaiCts.CancelAfter(delai);
        var delaiDepasse = false;
        try
        {
            if (entree is not null)
            {
                try
                {
                    await processus.StandardInput.WriteAsync(entree.AsMemory(), delaiCts.Token);
                    await processus.StandardInput.FlushAsync(delaiCts.Token);
                    processus.StandardInput.Close();
                }
                catch (IOException)
                {
                }
            }

            await processus.WaitForExitAsync(delaiCts.Token);
        }
        catch (OperationCanceledException)
        {
            delaiDepasse = !ct.IsCancellationRequested;
            try
            {
                processus.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await processus.WaitForExitAsync(CancellationToken.None);
            if (ct.IsCancellationRequested)
            {
                throw;
            }
        }

        processus.WaitForExit();
        return new ResultatCommande(delaiDepasse ? -1 : processus.ExitCode, sortie.Texte(), erreurs.Texte(), delaiDepasse);
    }

    public static string Tronquer(string texte, int tailleMax)
    {
        if (texte.Length <= tailleMax)
        {
            return texte;
        }

        var debut = tailleMax / 3;
        var fin = tailleMax - debut;
        return texte[..debut] + $"\n[… {texte.Length - tailleMax} caractères omis …]\n" + texte[^fin..];
    }

    internal sealed class CaptureBornee(int tailleMax)
    {
        private readonly StringBuilder debut = new();
        private readonly LinkedList<string> fin = new();
        private readonly Lock verrou = new();
        private int tailleFin;
        private long omis;
        private bool debutFerme;

        public void Ajouter(string? ligne)
        {
            if (ligne is null)
            {
                return;
            }

            lock (verrou)
            {
                if (!debutFerme && debut.Length + ligne.Length + 1 <= tailleMax / 4)
                {
                    debut.AppendLine(ligne);
                    return;
                }

                debutFerme = true;
                fin.AddLast(ligne);
                tailleFin += ligne.Length + 1;
                while (tailleFin > tailleMax - tailleMax / 4 && fin.First is { } premiere)
                {
                    tailleFin -= premiere.Value.Length + 1;
                    omis += premiere.Value.Length + 1;
                    fin.RemoveFirst();
                }
            }
        }

        public string Texte()
        {
            lock (verrou)
            {
                var texte = new StringBuilder(debut.ToString());
                if (omis > 0)
                {
                    texte.AppendLine(CultureInfo.InvariantCulture, $"[… {omis} caractères omis …]");
                }

                foreach (var ligne in fin)
                {
                    texte.AppendLine(ligne);
                }

                return texte.ToString().TrimEnd();
            }
        }
    }
}
