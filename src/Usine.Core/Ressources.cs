using System.Reflection;

namespace Usine.Core;

public static class Ressources
{
    private static readonly Assembly Assemblage = typeof(Ressources).Assembly;

    public static string Lire(string cheminRelatif)
    {
        var nom = Trouver(cheminRelatif);
        using var flux = Assemblage.GetManifestResourceStream(nom)!;
        return Lire(flux);
    }

    internal static string Lire(Stream flux)
    {
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd().ReplaceLineEndings("\n");
    }

    public static IReadOnlyList<string> Lister(string prefixe)
    {
        var normalise = "Usine." + prefixe.Replace('\\', '/').TrimEnd('/') + "/";
        return Assemblage.GetManifestResourceNames()
            .Select(n => n.Replace('\\', '/'))
            .Where(n => n.StartsWith(normalise, StringComparison.Ordinal))
            .Select(n => n["Usine.".Length..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public static void Extraire(string prefixe, string repertoireCible)
    {
        var normalise = prefixe.Replace('\\', '/').TrimEnd('/') + "/";
        foreach (var chemin in Lister(prefixe))
        {
            var relatif = chemin[normalise.Length..];
            var cible = Path.Combine(repertoireCible, relatif.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
            File.WriteAllText(cible, Lire(chemin));
        }
    }

    private static string Trouver(string cheminRelatif)
    {
        var attendu = "Usine." + cheminRelatif.Replace('\\', '/');
        return Assemblage.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == attendu)
            ?? throw new InvalidOperationException($"Ressource introuvable : {cheminRelatif}");
    }
}
