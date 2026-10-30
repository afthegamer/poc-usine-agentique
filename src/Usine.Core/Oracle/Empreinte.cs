using System.Security.Cryptography;
using System.Text;
using Usine.Core.Etat;

namespace Usine.Core.Oracle;

public static class Empreinte
{
    public static string Calculer(string repertoire) =>
        Directory.Exists(repertoire) ? Calculer(repertoire, ListerFichiers(repertoire)) : "";

    public static string Calculer(string repertoire, IEnumerable<string> fichiers)
    {
        using var hachage = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relatif in fichiers.Order(StringComparer.Ordinal))
        {
            var complet = Path.Combine(repertoire, relatif);
            if (!File.Exists(complet))
            {
                return "";
            }

            hachage.AppendData(Encoding.UTF8.GetBytes(relatif));
            hachage.AppendData([0]);
            hachage.AppendData(File.ReadAllBytes(complet));
            hachage.AppendData([0]);
        }

        return Convert.ToHexString(hachage.GetHashAndReset()).ToLowerInvariant();
    }

    internal static string Abreger(string valeur) => valeur.Length > 12 ? valeur[..12] : valeur;

    public static void Verrouiller(TestsAcceptation tests, string repertoire)
    {
        tests.Empreinte = Calculer(repertoire);
        tests.Fichiers = [.. ListerFichiers(repertoire)];
    }

    public static IReadOnlyList<string> ListerFichiers(string repertoire) =>
        Directory.Exists(repertoire)
            ? Directory.EnumerateFiles(repertoire, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(repertoire, f).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList()
            : [];
}
