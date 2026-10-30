using System.Text;
using System.Text.RegularExpressions;

namespace Usine.Core.Discussion;

public static partial class TraducteurJournal
{
    public static string? Traduire(string contenu)
    {
        if (StatistiqueAppel().IsMatch(contenu))
        {
            return null;
        }

        var appel = AppelOutil().Match(contenu);
        if (!appel.Success)
        {
            return contenu;
        }

        var reste = appel.Groups["reste"].Value;
        var action = appel.Groups["outil"].Value switch
        {
            "ecrire_fichier" => $"écrit {Fichier(reste)}",
            "remplacer_dans_fichier" => $"retouche {Fichier(reste)}",
            "lire_fichier" => $"lit {Fichier(reste)}",
            "lister_fichiers" => "fait le tour des fichiers",
            "rechercher" => "cherche dans le code",
            "executer_commande" => "lance une commande",
            "lancer_tests_acceptation" => $"lance les tests : {Resultat(reste)}",
            "terminer_construction" => "rend sa copie",
            "soumettre_besoin" => "soumet le besoin compris",
            "soumettre_backlog" => "soumet son backlog",
            "soumettre_tests" => "soumet ses tests",
            "lister_tests" => "vérifie que ses tests se chargent",
            "contester_tests" => "conteste des tests",
            "maintenir_tests" => "maintient ses tests",
            "poser_questions" => "pose ses questions",
            "soumettre_evaluation" => "soumet son évaluation",
            var autre => $"appelle {autre}",
        };
        return $"{appel.Groups["role"].Value} {action}";
    }

    public static IReadOnlyList<string> Formater(IEnumerable<string> lignes, string suffixe, ref string heure)
    {
        var resultat = new List<string>();
        foreach (var ligne in lignes)
        {
            var horodatee = Horodatage().Match(ligne);
            if (!horodatee.Success)
            {
                continue;
            }

            heure = horodatee.Groups["heure"].Value;
            if (Traduire(horodatee.Groups["contenu"].Value) is { } texte)
            {
                resultat.Add($"{heure} {suffixe} {texte}".TrimEnd());
            }
        }

        return resultat;
    }

    private static string Fichier(string reste)
    {
        var chemin = Chemin().Match(reste);
        if (!chemin.Success)
        {
            return "un fichier";
        }

        var valeur = chemin.Groups["chemin"].Value.Trim();
        var nom = valeur.Split('/', '\\')[^1];
        return nom.Length > 0 ? nom : valeur;
    }

    private static string Resultat(string reste)
    {
        var debut = reste.IndexOf(") : ", StringComparison.Ordinal);
        return debut < 0 ? "" : reste[(debut + 4)..];
    }

    [GeneratedRegex(@"^[^\[→]+ : appel \d+ \(")]
    private static partial Regex StatistiqueAppel();

    [GeneratedRegex(@"^(?<role>[^\[→]+?) → (?<outil>[a-z_]+)\((?<reste>.*)$")]
    private static partial Regex AppelOutil();

    [GeneratedRegex(@"(?:^|, )chemin=(?<chemin>.*?)(?=, [a-z_]+=|\) : |\)$)")]
    private static partial Regex Chemin();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} (?<heure>\d{2}\D\d{2}\D\d{2}) (?<contenu>.*)$")]
    private static partial Regex Horodatage();
}

public sealed class LecteurJournal(string chemin, string suffixe, long debut)
{
    private string heure = "--:--:--";

    private long position = debut;

    public IReadOnlyList<string> Lire()
    {
        byte[] octets;
        try
        {
            using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (flux.Length < position)
            {
                position = 0;
            }

            if (flux.Length == position)
            {
                return [];
            }

            flux.Seek(position, SeekOrigin.Begin);
            octets = new byte[flux.Length - position];
            flux.ReadExactly(octets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var fin = Array.LastIndexOf(octets, (byte)'\n');
        if (fin < 0)
        {
            return [];
        }

        position += fin + 1;
        var lignes = Encoding.UTF8.GetString(octets, 0, fin).Split('\n').Select(l => l.TrimEnd('\r'));
        return TraducteurJournal.Formater(lignes, suffixe, ref heure);
    }

    public static long Taille(string chemin)
    {
        try
        {
            return File.Exists(chemin) ? new FileInfo(chemin).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
