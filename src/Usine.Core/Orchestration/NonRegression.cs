using System.Text.RegularExpressions;
using Usine.Core.BacASable;
using Usine.Core.Etat;
using Usine.Core.Oracle;

namespace Usine.Core.Orchestration;

public sealed record RepriseNonRegression(string Id, int Identifiants);

public sealed record ScriptNonRegression(string Demande, int Numero, string Attendu, string Copie);

public static partial class NonRegression
{
    public const string Dossier = OracleAcceptation.DossierNonRegression;

    public const string Prefixe = "[NR ";

    public const string DossierScripts = "qa/" + Dossier;

    public const string AvantCodeImpossible = "contrôle contre l'application vide impossible";

    public static string Repertoire(DepotDemandes depot, string id) => Path.Combine(depot.RepertoireAcceptation(id), "tests", Dossier);

    public static bool MemeDepot(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        var comparaison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Normaliser(a), Normaliser(b), comparaison);
    }

    public static bool EstTitre(string titre) => titre.Contains(Prefixe, StringComparison.Ordinal);

    public static bool ControleAvantCodeImpossible(ResultatOracle initiale) => initiale is { Total: 0, EtapeEchec: not null };

    public static string SansReprisAvantCode(ResultatOracle initiale, IReadOnlyCollection<string> titres) => titres.Count(EstTitre) switch
    {
        0 => "",
        var repris when initiale.Total + repris != titres.Count => "",
        1 => ", sans le test de non-régression repris, vérifié à la construction",
        var repris => $", sans les {repris} tests de non-régression repris, vérifiés à la construction",
    };

    public static string AvantToutCode(ResultatOracle initiale, IReadOnlyCollection<string> titres) =>
        $"Avant tout code : {(ControleAvantCodeImpossible(initiale) ? AvantCodeImpossible : $"{initiale.Echoues} test(s) en échec sur {initiale.Total}{SansReprisAvantCode(initiale, titres)} (attendu : tous rouges)")}.";

    public static IReadOnlyList<Demande> Precedentes(DepotDemandes depot, Demande demande)
    {
        if (!demande.EstProjetExistant)
        {
            return [];
        }

        var demandes = depot.Lister();
        var source = demandes.FirstOrDefault(d => d.Id == demande.ReevaluationDe);
        var limite = source?.CreeLe ?? DateTimeOffset.MaxValue;
        var projet = demande.Projet ?? source?.Projet;
        return demandes
            .Where(d => d.Id != demande.Id
                && d.ReevaluationDe is null
                && d.CreeLe < limite
                && d.Statut == StatutDemande.Terminee
                && d.Livrable is { Succes: true }
                && (MemeDepot(d.DepotSource, demande.DepotSource) || (projet is not null && string.Equals(d.Projet, projet, StringComparison.Ordinal))))
            .OrderBy(d => d.CreeLe)
            .ToList();
    }

    public static string PrefixerTitres(string contenu, out int identifiants)
    {
        var compte = 0;
        var resultat = IdentifiantDeCritere().Replace(contenu, _ =>
        {
            compte++;
            return Prefixe;
        });
        identifiants = compte;
        return resultat;
    }

    public static IReadOnlyList<RepriseNonRegression> Reprendre(DepotDemandes depot, Demande demande, IReadOnlyList<Demande> precedentes)
    {
        var cible = Repertoire(depot, demande.Id);
        BacASableDocker.SupprimerRepertoire(cible);

        var reprises = new List<RepriseNonRegression>();
        foreach (var precedente in precedentes)
        {
            var source = Path.Combine(depot.RepertoireAcceptation(precedente.Id), "tests");
            if (!Directory.Exists(source))
            {
                continue;
            }

            var identifiants = 0;
            var fichiers = 0;
            foreach (var fichier in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relatif = Path.GetRelativePath(source, fichier).Replace('\\', '/');
                if (relatif.StartsWith(Dossier + "/", StringComparison.Ordinal))
                {
                    continue;
                }

                var destination = Path.Combine(cible, precedente.Id, relatif);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (FichierDeTest().IsMatch(relatif))
                {
                    File.WriteAllText(destination, PrefixerTitres(File.ReadAllText(fichier), out var prefixes));
                    identifiants += prefixes;
                }
                else
                {
                    File.Copy(fichier, destination, overwrite: true);
                }

                fichiers++;
            }

            if (fichiers > 0)
            {
                reprises.Add(new RepriseNonRegression(precedente.Id, identifiants));
            }
        }

        return reprises;
    }

    public static IReadOnlyList<ScriptNonRegression> ReprendreScripts(DepotDemandes depot, Demande demande, IReadOnlyList<Demande> precedentes)
    {
        var scripts = new List<ScriptNonRegression>();
        foreach (var precedente in precedentes)
        {
            var bloquants = precedente.Evaluation?.Bloquants ?? [];
            for (var i = 0; i < bloquants.Count; i++)
            {
                if (bloquants[i] is not { Corrige: true, Copie: { } copie } || !File.Exists(Path.Combine(depot.Repertoire(precedente.Id), copie)))
                {
                    continue;
                }

                var reprise = $"{DossierScripts}/{precedente.Id}-bloquant-{i + 1}{Path.GetExtension(copie)}";
                var destination = Path.Combine(depot.Repertoire(demande.Id), reprise);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(depot.Repertoire(precedente.Id), copie), destination, overwrite: true);
                scripts.Add(new ScriptNonRegression(precedente.Id, i + 1, bloquants[i].Attendu, reprise));
            }
        }

        return scripts;
    }

    public static bool Restaurer(DepotDemandes depot, Demande demande, IReadOnlyList<Demande> precedentes)
    {
        var repertoire = Repertoire(depot, demande.Id);
        var avant = Empreinte.Calculer(repertoire);
        Reprendre(depot, demande, precedentes);
        return !string.Equals(avant, Empreinte.Calculer(repertoire), StringComparison.Ordinal);
    }

    private static string Normaliser(string chemin) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(chemin));

    [GeneratedRegex(@"\[(?=(?:S\d+-C\d+|F\d+)\])")]
    private static partial Regex IdentifiantDeCritere();

    [GeneratedRegex(@"\.(?i:spec|test)\.[cm]?[jt]sx?$")]
    private static partial Regex FichierDeTest();
}
