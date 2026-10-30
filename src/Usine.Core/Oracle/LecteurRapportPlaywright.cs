using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Usine.Core.Etat;

namespace Usine.Core.Oracle;

public sealed record TestPlaywright(string Titre, string Statut, string? Erreur, string? Page = null, string? Console = null, string? Garde = null, string Fichier = "")
{
    public bool Reussi => Statut is "expected" or "flaky";
}

public sealed record GesteJournal(string Methode, string Url, int? Statut, string? Id);

public static partial class LecteurRapportPlaywright
{
    public const string MarqueurPage = "@@usine-page";

    public const string MarqueurSignal = "@@usine-signal";

    public const string MarqueurBilanSonde = "@@usine-sonde";

    public const string MarqueurSondeAbsente = "@@usine-sonde-absente";

    public const string MarqueurGestes = "@@usine-gestes";

    public const string PieceConsole = "usine-console";

    public const string PieceGarde = "usine-garde";

    public const int OctetsPageMax = 4000;

    public const string FinPageCoupee = " […]";

    public const string TitreErreurGlobale = "(erreur globale)";

    public static IReadOnlyList<TestPlaywright> Lire(string json, IReadOnlyDictionary<string, string>? pages = null)
    {
        using var document = JsonDocument.Parse(json);
        var tests = new List<TestPlaywright>();
        if (document.RootElement.TryGetProperty("suites", out var suites))
        {
            foreach (var suite in suites.EnumerateArray())
            {
                ParcourirSuite(suite, [], tests, "", estFichier: true, pages ?? new Dictionary<string, string>());
            }
        }

        return tests;
    }

    public static IReadOnlyDictionary<string, string> PagesEnEchec(string sortie)
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match ligne in LignePage().Matches(sortie))
        {
            try
            {
                var octets = Convert.FromBase64String(ligne.Groups["page"].Value);
                var page = Encoding.UTF8.GetString(octets).Trim();
                if (page.Length > 0)
                {
                    pages[Encoding.UTF8.GetString(Convert.FromBase64String(ligne.Groups["chemin"].Value))] = octets.Length >= OctetsPageMax ? page + FinPageCoupee : page;
                }
            }
            catch (FormatException)
            {
            }
        }

        return pages;
    }

    public static IReadOnlyList<SignalOracle> SignauxSonde(string sortie)
    {
        var signaux = new List<SignalOracle>();
        foreach (Match ligne in LigneSignal().Matches(sortie))
        {
            try
            {
                using var document = JsonDocument.Parse(Convert.FromBase64String(ligne.Groups["charge"].Value));
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("cle", out var cle) && cle.ValueKind == JsonValueKind.String && cle.GetString() is { Length: > 0 } texteCle
                    && document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String && message.GetString() is { Length: > 0 } texteMessage
                    && !signaux.Exists(s => string.Equals(s.Cle, texteCle, StringComparison.Ordinal)))
                {
                    signaux.Add(new SignalOracle { Cle = texteCle, Message = texteMessage });
                }
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
            }
        }

        return signaux;
    }

    public static string? BilanSonde(string sortie)
    {
        string? bilan = null;
        foreach (Match ligne in LigneBilanSonde().Matches(sortie))
        {
            try
            {
                var texte = Encoding.UTF8.GetString(Convert.FromBase64String(ligne.Groups["bilan"].Value)).Trim();
                if (texte.Length > 0)
                {
                    bilan = texte;
                }
            }
            catch (FormatException)
            {
            }
        }

        return bilan;
    }

    public static bool SondeAbsente(string sortie) => LigneSondeAbsente().IsMatch(sortie);

    public static IReadOnlyList<GesteJournal> GestesJournal(string sortie)
    {
        var gestes = new List<GesteJournal>();
        foreach (Match ligne in LigneGestes().Matches(sortie))
        {
            try
            {
                using var document = JsonDocument.Parse(Convert.FromBase64String(ligne.Groups["gestes"].Value));
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var geste in document.RootElement.EnumerateArray())
                {
                    if (geste.ValueKind == JsonValueKind.Array && geste.GetArrayLength() == 4
                        && geste[0].ValueKind == JsonValueKind.String && geste[1].ValueKind == JsonValueKind.String)
                    {
                        gestes.Add(new GesteJournal(
                            geste[0].GetString()!,
                            geste[1].GetString()!,
                            geste[2].ValueKind == JsonValueKind.Number && geste[2].TryGetInt32(out var statut) ? statut : null,
                            geste[3].ValueKind == JsonValueKind.String ? geste[3].GetString() : null));
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
            }
        }

        return gestes;
    }

    public static IReadOnlyList<string> ErreursGlobales(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("errors", out var erreurs) || erreurs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return erreurs.EnumerateArray()
            .Select(e => e.TryGetProperty("message", out var m) ? NettoyerAnsi(m.GetString() ?? "") : e.ToString())
            .Where(m => m.Length > 0)
            .ToList();
    }

    public static string ExtraireJson(string sortie)
    {
        var debut = sortie.IndexOf('{', StringComparison.Ordinal);
        var fin = sortie.LastIndexOf('}');
        return debut >= 0 && fin > debut ? sortie[debut..(fin + 1)] : sortie;
    }

    public static ResultatOracle VersResultat(IReadOnlyList<TestPlaywright> tests, IReadOnlyList<string> erreursGlobales, IReadOnlyList<string>? instablesConnus = null)
    {
        var connus = instablesConnus ?? [];
        var instables = new List<string>();
        var echoues = new List<TestPlaywright>();
        foreach (var test in tests)
        {
            if (!test.Reussi && connus.Contains(test.Titre, StringComparer.Ordinal))
            {
                instables.Add(test.Titre);
            }
            else if (!test.Reussi)
            {
                echoues.Add(test);
            }
        }

        var echecs = echoues
            .Select(t => new EchecTest { Titre = t.Titre, Erreur = t.Erreur ?? t.Statut, Page = t.Page, Console = AvecGarde(t.Console, t.Garde) })
            .ToList();
        foreach (var erreur in erreursGlobales)
        {
            echecs.Add(new EchecTest { Titre = TitreErreurGlobale, Erreur = erreur });
        }

        var succes = tests.Count > 0 && echecs.Count == 0;
        return new ResultatOracle
        {
            Total = tests.Count,
            Reussis = tests.Count - echoues.Count,
            Echoues = echoues.Count,
            Echecs = echecs,
            TitresReussis = [.. tests.Where(t => t.Reussi).Select(t => t.Titre)],
            TitresSautes = [.. tests.Where(t => t.Statut == "skipped").Select(t => t.Titre)],
            Succes = succes,
            EtapeEchec = succes ? null : "tests",
            Signaux = succes ? SignauxDeLaGarde(tests) : [],
            Instables = instables,
        };
    }

    private static List<SignalOracle> SignauxDeLaGarde(IReadOnlyList<TestPlaywright> tests)
    {
        var signaux = new List<SignalOracle>();
        foreach (var test in tests.Where(t => t.Reussi))
        {
            foreach (var (motif, chemin, extrait) in LignesGarde(test.Garde))
            {
                var cle = $"garde|{motif}|{chemin}";
                if (!signaux.Exists(s => string.Equals(s.Cle, cle, StringComparison.Ordinal)))
                {
                    signaux.Add(new SignalOracle { Cle = cle, Message = $"Pendant le test « {test.Titre} », {DecrireAnomalie(motif, chemin, extrait)}." });
                }
            }
        }

        return signaux;
    }

    private static string? AvecGarde(string? console, string? garde)
    {
        var lignes = LignesGarde(garde).Select(l => $"Garde de l'usine : {DecrireAnomalie(l.Motif, l.Chemin, l.Extrait)}.").ToList();
        return lignes.Count == 0 ? console : string.Join("\n", console is null ? lignes : lignes.Prepend(console));
    }

    private static IEnumerable<(string Motif, string Chemin, string Extrait)> LignesGarde(string? garde) =>
        (garde ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(" | ", 3))
            .Where(p => p.Length == 3)
            .Select(p => (p[0].Trim(), p[1].Trim(), p[2].Trim()));

    private static string DecrireAnomalie(string motif, string chemin, string extrait) => motif switch
    {
        "exception" => $"la page {chemin} a levé une exception JavaScript non interceptée : « {extrait} »",
        "reponse-5xx" => $"l'application a renvoyé une erreur serveur (réponse 5xx) : « {extrait} »",
        _ => $"la page {chemin} a affiché « {extrait} » ({MotifEnClair(motif)})",
    };

    private static string MotifEnClair(string motif) => motif switch
    {
        "zero-negatif" => "zéro négatif, un zéro s'affiche sans signe",
        "undefined" => "undefined, une valeur absente affichée telle quelle",
        "NaN" => "NaN, un calcul sur une valeur qui n'est pas un nombre",
        "objet" => "objet non converti en texte",
        "trace" => "trace de pile",
        _ => motif,
    };

    public static string NettoyerAnsi(string texte) => Ansi().Replace(texte, "");

    private static void ParcourirSuite(JsonElement suite, List<string> chemin, List<TestPlaywright> tests, string fichier, bool estFichier, IReadOnlyDictionary<string, string> pages)
    {
        var titre = suite.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var cheminCourant = estFichier || titre.Length == 0 ? chemin : [.. chemin, titre];
        var fichierCourant = estFichier ? titre : fichier;
        if (suite.TryGetProperty("specs", out var specs))
        {
            foreach (var spec in specs.EnumerateArray())
            {
                var titreSpec = spec.TryGetProperty("title", out var ts) ? ts.GetString() ?? "" : "";
                var titreComplet = string.Join(" › ", cheminCourant.Append(titreSpec));
                if (!spec.TryGetProperty("tests", out var testsSpec) || testsSpec.GetArrayLength() == 0)
                {
                    tests.Add(new TestPlaywright(titreComplet, "expected", null, Fichier: fichierCourant));
                    continue;
                }

                foreach (var test in testsSpec.EnumerateArray())
                {
                    var statut = test.TryGetProperty("status", out var s) ? s.GetString() ?? "unexpected" : "unexpected";
                    tests.Add(new TestPlaywright(titreComplet, statut, ExtraireErreur(test), ExtrairePage(test, pages), ExtrairePiece(test, PieceConsole), ExtrairePiece(test, PieceGarde), fichierCourant));
                }
            }
        }

        if (suite.TryGetProperty("suites", out var sousSuites))
        {
            foreach (var sousSuite in sousSuites.EnumerateArray())
            {
                ParcourirSuite(sousSuite, cheminCourant, tests, fichierCourant, estFichier: false, pages);
            }
        }
    }

    private static string? ExtraireErreur(JsonElement test)
    {
        if (!test.TryGetProperty("results", out var resultats) || resultats.GetArrayLength() == 0)
        {
            return null;
        }

        var dernier = resultats[resultats.GetArrayLength() - 1];
        if (dernier.TryGetProperty("errors", out var toutes) && toutes.ValueKind == JsonValueKind.Array && toutes.GetArrayLength() > 1
            && string.Join("\n", toutes.EnumerateArray()
                .Select(e => e.TryGetProperty("message", out var m) ? NettoyerAnsi(m.GetString() ?? "") : "")
                .Where(m => m.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(m => m.StartsWith("Test timeout of ", StringComparison.Ordinal))) is { Length: > 0 } messages)
        {
            return messages;
        }

        if (dernier.TryGetProperty("error", out var erreur) && erreur.TryGetProperty("message", out var message))
        {
            return NettoyerAnsi(message.GetString() ?? "");
        }

        if (dernier.TryGetProperty("errors", out var erreurs) && erreurs.GetArrayLength() > 0
            && erreurs[0].TryGetProperty("message", out var premier))
        {
            return NettoyerAnsi(premier.GetString() ?? "");
        }

        return dernier.TryGetProperty("status", out var statut) ? statut.GetString() : null;
    }

    private static string? ExtrairePage(JsonElement test, IReadOnlyDictionary<string, string> pages)
    {
        foreach (var piece in Pieces(test))
        {
            if (piece.TryGetProperty("name", out var nom) && nom.GetString() is "error-context"
                && piece.TryGetProperty("path", out var chemin) && chemin.GetString() is { } fichier
                && pages.TryGetValue(fichier, out var page))
            {
                return page;
            }
        }

        return null;
    }

    private static string? ExtrairePiece(JsonElement test, string nomPiece)
    {
        foreach (var piece in Pieces(test))
        {
            if (piece.TryGetProperty("name", out var nom) && string.Equals(nom.GetString(), nomPiece, StringComparison.Ordinal)
                && piece.TryGetProperty("body", out var corps) && corps.GetString() is { Length: > 0 } base64)
            {
                try
                {
                    var console = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Trim();
                    return console.Length > 0 ? console : null;
                }
                catch (FormatException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> Pieces(JsonElement test)
    {
        if (!test.TryGetProperty("results", out var resultats) || resultats.GetArrayLength() == 0
            || !resultats[resultats.GetArrayLength() - 1].TryGetProperty("attachments", out var pieces) || pieces.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return pieces.EnumerateArray();
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();

    [GeneratedRegex("^" + MarqueurPage + @" (?<chemin>[A-Za-z0-9+/=]+) (?<page>[A-Za-z0-9+/=]*)\r?$", RegexOptions.Multiline)]
    private static partial Regex LignePage();

    [GeneratedRegex("^" + MarqueurSignal + @" (?<charge>[A-Za-z0-9+/=]+)\r?$", RegexOptions.Multiline)]
    private static partial Regex LigneSignal();

    [GeneratedRegex("^" + MarqueurBilanSonde + @" (?<bilan>[A-Za-z0-9+/=]+)\r?$", RegexOptions.Multiline)]
    private static partial Regex LigneBilanSonde();

    [GeneratedRegex("^" + MarqueurSondeAbsente + @"\r?$", RegexOptions.Multiline)]
    private static partial Regex LigneSondeAbsente();

    [GeneratedRegex("^" + MarqueurGestes + @" (?<gestes>[A-Za-z0-9+/=]+)\r?$", RegexOptions.Multiline)]
    private static partial Regex LigneGestes();
}
