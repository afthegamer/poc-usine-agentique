using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Usine.Core.Oracle;

internal sealed record LectureHttp(int Statut, string Corps, bool Tronquee)
{
    public bool Reussie => Statut is >= 200 and < 300;
}

internal static partial class RelectureRedemarrage
{
    public const string MarqueurLecture = "@@usine-lecture";

    public const int OctetsMax = 32768;

    private const int MaxUrls = 12;

    private const int LongueurMessage = 400;

    private const int LongueurValeur = 60;

    private static readonly string[] MethodesEcriture = ["POST", "PUT", "PATCH", "DELETE"];

    private static readonly string[] ClesIdentifiant = ["id", "Id", "_id"];

    public static IReadOnlyList<string> Urls(IReadOnlyList<GesteJournal> gestes)
    {
        var identifiants = gestes
            .Where(g => EstEcritureReussie(g) && g.Id is not null)
            .Select(g => g.Id!)
            .ToHashSet(StringComparer.Ordinal);
        var collections = new List<string>();
        var details = new List<string>();
        var pages = new List<string>();
        foreach (var geste in gestes)
        {
            if (string.Equals(geste.Methode, "PAGE", StringComparison.Ordinal) && geste.Url.StartsWith('/'))
            {
                pages.Add(geste.Url);
                continue;
            }

            if (!EstEcritureReussie(geste) || !geste.Url.StartsWith('/'))
            {
                continue;
            }

            var chemin = CheminSeul(geste.Url);
            var segments = chemin.Split('/');
            var dernierId = Array.FindLastIndex(segments, s => EstIdentifiant(s, identifiants));
            if (dernierId > 1)
            {
                collections.Add(string.Join('/', segments[..dernierId]));
                if (!string.Equals(geste.Methode, "DELETE", StringComparison.Ordinal))
                {
                    details.Add(string.Join('/', segments[..(dernierId + 1)]));
                }
            }

            if (dernierId == segments.Length - 1 || string.Equals(geste.Methode, "DELETE", StringComparison.Ordinal))
            {
                continue;
            }

            collections.Add(chemin);
            if (string.Equals(geste.Methode, "POST", StringComparison.Ordinal) && geste.Id is { Length: > 0 } id)
            {
                details.Add($"{chemin.TrimEnd('/')}/{Uri.EscapeDataString(id)}");
            }
        }

        details.Reverse();
        return [.. new[] { "/" }.Concat(collections).Concat(pages).Concat(details).Distinct(StringComparer.Ordinal).Take(MaxUrls)];
    }

    public static IReadOnlyDictionary<string, LectureHttp> Lectures(string sortie)
    {
        var lectures = new Dictionary<string, LectureHttp>(StringComparer.Ordinal);
        foreach (Match ligne in LigneLecture().Matches(sortie))
        {
            try
            {
                var url = Encoding.UTF8.GetString(Convert.FromBase64String(ligne.Groups["url"].Value));
                var octets = Convert.FromBase64String(ligne.Groups["corps"].Value);
                lectures[url] = new LectureHttp(int.Parse(ligne.Groups["statut"].Value, NumberStyles.None, CultureInfo.InvariantCulture), Encoding.UTF8.GetString(octets), octets.Length > OctetsMax);
            }
            catch (FormatException)
            {
            }
        }

        return lectures;
    }

    public static IReadOnlyList<string> Pertes(IReadOnlyList<string> urls, IReadOnlyDictionary<string, LectureHttp> premiere, IReadOnlyDictionary<string, LectureHttp> seconde, IReadOnlyDictionary<string, LectureHttp> apres)
    {
        var pertes = new List<string>();
        foreach (var url in urls)
        {
            if (!premiere.TryGetValue(url, out var avant) || !seconde.TryGetValue(url, out var confirme) || !apres.TryGetValue(url, out var ensuite)
                || !avant.Reussie || !confirme.Reussie)
            {
                continue;
            }

            if (!ensuite.Reussie)
            {
                pertes.Add($"GET {url} répondait {confirme.Statut.ToString(CultureInfo.InvariantCulture)}, il répond maintenant {(ensuite.Statut == 0 ? "sans réponse" : ensuite.Statut.ToString(CultureInfo.InvariantCulture))}");
            }
            else if (!avant.Tronquee && !confirme.Tronquee && Perte(url, avant, confirme, ensuite) is { } perte)
            {
                pertes.Add(perte);
            }
        }

        return pertes;
    }

    public static string Message(string debut, string detail, string fin)
    {
        var place = LongueurMessage - debut.Length - fin.Length;
        return $"{debut}{(detail.Length > place ? $"{detail[..(place - 1)]}…" : detail)}{fin}";
    }

    public static int Stables(IReadOnlyList<string> urls, IReadOnlyDictionary<string, LectureHttp> premiere, IReadOnlyDictionary<string, LectureHttp> seconde) =>
        urls.Count(u => premiere.TryGetValue(u, out var avant) && avant.Reussie && seconde.TryGetValue(u, out var confirme) && confirme.Reussie);

    private static string? Perte(string url, LectureHttp avant, LectureHttp confirme, LectureHttp ensuite)
    {
        if (VueJson(avant.Corps) is { } premiere && VueJson(confirme.Corps) is { } seconde)
        {
            var apres = VueJson(ensuite.Corps) ?? new VueDonnees([], []);
            var stables = premiere.Entites.Keys.Where(seconde.Entites.ContainsKey).ToList();
            var disparues = stables.Where(k => !apres.Entites.ContainsKey(k)).ToList();
            if (disparues.Count > 0)
            {
                return $"GET {url} ne renvoie plus {disparues.Count.ToString(CultureInfo.InvariantCulture)} des {stables.Count.ToString(CultureInfo.InvariantCulture)} éléments lus juste avant (dont l'identifiant {Couper(disparues[0].Id)})";
            }

            foreach (var cle in stables)
            {
                foreach (var (champ, valeur) in premiere.Entites[cle])
                {
                    if (seconde.Entites[cle].TryGetValue(champ, out var relue) && string.Equals(relue, valeur, StringComparison.Ordinal) && !ValeurInstable().IsMatch(valeur)
                        && (!apres.Entites[cle].TryGetValue(champ, out var nouvelle) || !string.Equals(nouvelle, valeur, StringComparison.Ordinal)))
                    {
                        return $"GET {url} : « {champ} » de l'élément {Couper(cle.Id)} valait {Couper(valeur)}, il {(nouvelle is null ? "n'est plus renvoyé" : $"vaut maintenant {Couper(nouvelle)}")}";
                    }
                }
            }

            foreach (var (chemin, nombre) in premiere.Tableaux)
            {
                var restants = apres.Tableaux.GetValueOrDefault(chemin);
                if (nombre > 0 && seconde.Tableaux.GetValueOrDefault(chemin) == nombre && restants < nombre)
                {
                    return $"GET {url} renvoie {restants.ToString(CultureInfo.InvariantCulture)} élément(s) au lieu des {nombre.ToString(CultureInfo.InvariantCulture)} lus juste avant";
                }
            }

            return null;
        }

        var masques = Lignes(confirme.Corps).Select(l => l.Masque).ToHashSet(StringComparer.Ordinal);
        var apresMasques = Lignes(ensuite.Corps).Select(l => l.Masque).ToHashSet(StringComparer.Ordinal);
        var perdues = Lignes(avant.Corps).Where(l => masques.Contains(l.Masque) && !apresMasques.Contains(l.Masque)).ToList();
        return perdues.Count == 0
            ? null
            : $"GET {url} n'affiche plus « {Couper(perdues[0].Texte)} »{(perdues.Count > 1 ? $" ni {(perdues.Count - 1).ToString(CultureInfo.InvariantCulture)} autre(s) ligne(s) lue(s) juste avant" : "")}";
    }

    private static bool EstEcritureReussie(GesteJournal geste) =>
        MethodesEcriture.Contains(geste.Methode, StringComparer.Ordinal) && geste.Statut is >= 200 and < 400;

    private static bool EstIdentifiant(string segment, HashSet<string> identifiants) =>
        segment.Length > 0 && (Entier().IsMatch(segment) || Uuid().IsMatch(segment) || identifiants.Contains(Uri.UnescapeDataString(segment)));

    private static string CheminSeul(string url)
    {
        var fin = url.IndexOfAny(['?', '#']);
        return fin < 0 ? url : url[..fin];
    }

    private static string Couper(string texte) => texte.Length > LongueurValeur ? $"{texte[..(LongueurValeur - 1)]}…" : texte;

    private sealed record VueDonnees(Dictionary<(string Chemin, string Id), Dictionary<string, string>> Entites, Dictionary<string, int> Tableaux);

    private static VueDonnees? VueJson(string corps)
    {
        var texte = corps.TrimStart();
        if (!texte.StartsWith('{') && !texte.StartsWith('['))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(texte);
            var vue = new VueDonnees([], new Dictionary<string, int>(StringComparer.Ordinal));
            Parcourir(document.RootElement, "$", vue);
            return vue;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Parcourir(JsonElement element, string chemin, VueDonnees vue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (Identifiant(element) is { } id)
            {
                vue.Entites.TryAdd((chemin, id), element.EnumerateObject()
                    .Where(p => !ClesIdentifiant.Contains(p.Name, StringComparer.Ordinal) && p.Value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False)
                    .GroupBy(p => p.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Value.GetRawText(), StringComparer.Ordinal));
            }

            foreach (var propriete in element.EnumerateObject())
            {
                Parcourir(propriete.Value, $"{chemin}.{propriete.Name}", vue);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var elements = element.EnumerateArray().ToList();
            if (!elements.Exists(e => Identifiant(e) is not null))
            {
                vue.Tableaux[chemin] = vue.Tableaux.GetValueOrDefault(chemin) + elements.Count;
            }

            foreach (var sousElement in elements)
            {
                Parcourir(sousElement, $"{chemin}[]", vue);
            }
        }
    }

    private static string? Identifiant(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var cle in ClesIdentifiant)
        {
            if (element.TryGetProperty(cle, out var valeur) && valeur.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                return valeur.ValueKind == JsonValueKind.String ? valeur.GetString() : valeur.GetRawText();
            }
        }

        return null;
    }

    private static List<(string Masque, string Texte)> Lignes(string corps)
    {
        var texte = WebUtility.HtmlDecode(Balise().Replace(BlocInvisible().Replace(corps, "\n"), "\n"));
        return texte.Split('\n')
            .Select(l => Espaces().Replace(l, " ").Trim())
            .Where(l => l.Length > 0)
            .Select(l => (Chiffres().Replace(l, "#"), l))
            .DistinctBy(l => l.Item1, StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex("^" + MarqueurLecture + @" (?<url>[A-Za-z0-9+/=]+) (?<statut>\d{3}) (?<corps>[A-Za-z0-9+/=]*)\r?$", RegexOptions.Multiline)]
    private static partial Regex LigneLecture();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Chiffres();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Entier();

    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex Uuid();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}|\b\d{1,2}:\d{2}\b|^""[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}""$|^""\d{9,}""$|^""(?=[^""]*\d)[A-Za-z0-9+/_=.-]{20,}""$", RegexOptions.IgnoreCase)]
    private static partial Regex ValeurInstable();

    [GeneratedRegex(@"<script\b.*?</script\s*>|<style\b.*?</style\s*>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlocInvisible();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Balise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espaces();
}
