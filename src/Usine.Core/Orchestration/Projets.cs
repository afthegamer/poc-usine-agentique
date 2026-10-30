using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Usine.Core.BacASable;
using Usine.Core.Etat;

namespace Usine.Core.Orchestration;

public sealed record ProjetUsine(string Nom, string Chemin, IReadOnlyList<Demande> Demandes, DateTimeOffset DerniereMiseAJour);

public static partial class Projets
{
    public const string BranchePrincipale = "main";

    public const int LongueurMaxNom = 50;

    private static readonly TimeSpan DelaiGit = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> NomsReserves = new(
        ["con", "prn", "aux", "nul", .. Enumerable.Range(1, 9).SelectMany(i => new[] { $"com{i}", $"lpt{i}" })],
        StringComparer.Ordinal);

    public static string Nommer(string titre, IEnumerable<string> pris)
    {
        var dejaPris = new HashSet<string>(pris, StringComparer.OrdinalIgnoreCase);
        var racine = Identifiant(titre);
        if (!dejaPris.Contains(racine) && !NomsReserves.Contains(racine))
        {
            return racine;
        }

        for (var numero = 2; ; numero++)
        {
            var candidat = string.Create(CultureInfo.InvariantCulture, $"{racine}-{numero}");
            if (!dejaPris.Contains(candidat))
            {
                return candidat;
            }
        }
    }

    public static bool EstNomValide(string nom) => nom.Length <= 64 && NomValide().IsMatch(nom);

    public static IReadOnlyList<string> Noms(DepotDemandes depot) =>
        Directory.Exists(depot.RepertoireProjets)
            ? Directory.EnumerateDirectories(depot.RepertoireProjets)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(EstNomValide)
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

    public static string Chemin(DepotDemandes depot, string nom)
    {
        var noms = Noms(depot);
        if (!noms.Contains(nom, StringComparer.Ordinal))
        {
            throw new ArgumentException(noms.Count == 0
                ? $"Projet inconnu : « {nom} ». Aucun projet n'existe encore : une demande lancée sans projet ni dépôt en crée un quand elle se termine avec succès."
                : $"Projet inconnu : « {nom} ». Projets existants : {string.Join(", ", noms)}.");
        }

        return Path.Combine(depot.RepertoireProjets, nom);
    }

    public static IReadOnlyList<ProjetUsine> Lister(DepotDemandes depot)
    {
        var demandes = depot.Lister();
        return Noms(depot)
            .Select(nom =>
            {
                var chemin = Path.Combine(depot.RepertoireProjets, nom);
                var siennes = demandes.Where(d => string.Equals(d.Projet, nom, StringComparison.Ordinal)).OrderBy(d => d.CreeLe).ToList();
                var publications = siennes.Select(d => d.PublicationProjet).OfType<PublicationProjet>().Where(p => p.Integree).Select(p => p.Date).ToList();
                var date = publications.Count > 0 ? publications.Max() : new DateTimeOffset(Directory.GetLastWriteTimeUtc(chemin));
                return new ProjetUsine(nom, chemin, siennes, date);
            })
            .ToList();
    }

    public static async Task<(bool Integree, string Message)?> PublierAsync(DepotDemandes depot, Demande demande)
    {
        if (demande.Livrable is not { } livrable || demande.ReevaluationDe is not null || (demande.Projet is null && demande.EstProjetExistant))
        {
            return null;
        }

        var code = livrable.RepertoireCode;
        var publication = demande.Projet is { } nom && demande.EstProjetExistant
            ? await MettreAJourAsync(nom, Path.Combine(depot.RepertoireProjets, nom), code, demande.Id)
            : await CreerAsync(depot, demande, code);
        return publication.Integree && !livrable.Succes
            ? (true, $"{publication.Message} Attention : la livraison n'a pas tous ses tests d'acceptation verts.")
            : publication;
    }

    public static string? NomDuDossier(DepotDemandes depot, string chemin)
    {
        var complet = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chemin));
        var nom = Path.GetFileName(complet);
        return string.Equals(Path.GetDirectoryName(complet), Path.TrimEndingDirectorySeparator(Path.GetFullPath(depot.RepertoireProjets)), StringComparison.OrdinalIgnoreCase)
            ? Noms(depot).FirstOrDefault(n => string.Equals(n, nom, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public static string CommandeIntegration(string chemin, string code) => $"git -C \"{chemin}\" pull --no-rebase \"{code}\" HEAD";

    private static string Identifiant(string titre)
    {
        var texte = new StringBuilder();
        var decompose = titre
            .Replace("œ", "oe", StringComparison.Ordinal).Replace("Œ", "OE", StringComparison.Ordinal)
            .Replace("æ", "ae", StringComparison.Ordinal).Replace("Æ", "AE", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);
        foreach (var caractere in decompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var minuscule = char.ToLowerInvariant(caractere);
            if (char.IsAsciiLetterLower(minuscule) || char.IsAsciiDigit(minuscule))
            {
                texte.Append(minuscule);
            }
            else if (texte.Length > 0 && texte[^1] != '-')
            {
                texte.Append('-');
            }
        }

        var nom = texte.ToString().Trim('-');
        if (nom.Length > LongueurMaxNom)
        {
            var coupure = nom.LastIndexOf('-', LongueurMaxNom);
            nom = (coupure > 0 ? nom[..coupure] : nom[..LongueurMaxNom]).Trim('-');
        }

        return nom.Length == 0 ? "projet" : nom;
    }

    private static async Task<(bool Integree, string Message)> CreerAsync(DepotDemandes depot, Demande demande, string code)
    {
        if (!BacASableDocker.EstDepotGit(code))
        {
            return (false, $"Projet non créé : le code livré n'a pas d'historique git. Le livrable reste disponible : {code}.");
        }

        if (demande.Projet is not { } nom)
        {
            var titre = string.IsNullOrWhiteSpace(demande.Backlog?.Titre) ? demande.Titre : demande.Backlog.Titre;
            nom = Nommer(titre, Noms(depot).Concat(depot.Lister().Select(d => d.Projet).OfType<string>()));
            var reservee = depot.Charger(demande.Id);
            reservee.Projet = nom;
            depot.Enregistrer(reservee);
            demande.Projet = nom;
        }

        var chemin = Path.Combine(depot.RepertoireProjets, nom);
        Directory.CreateDirectory(chemin);
        var erreur = await GitAsync(null, "init", "-q", "-b", BranchePrincipale, chemin)
            ?? await GitAsync(chemin, "config", "core.autocrlf", "false")
            ?? await IntegrerAsync(chemin, code, demande.Id);
        if (erreur is not null)
        {
            BacASableDocker.SupprimerRepertoire(chemin);
            demande.Projet = null;
            return (false, $"Projet non créé : {erreur} Le livrable reste disponible : {code}.");
        }

        return (true, $"Projet « {nom} » créé : {chemin}.");
    }

    private static async Task<(bool Integree, string Message)> MettreAJourAsync(string nom, string chemin, string code, string id)
    {
        var commande = CommandeIntegration(chemin, code);
        if (!BacASableDocker.EstDepotGit(chemin) || !BacASableDocker.EstDepotGit(code))
        {
            return (false, $"Projet « {nom} » non mis à jour : {(BacASableDocker.EstDepotGit(code) ? $"son dossier {chemin} n'est plus un dépôt git" : "le code livré n'a pas d'historique git")}. Le livrable reste disponible : {code}.");
        }

        var tete = await ExecuterGitAsync(chemin, "rev-parse", "--verify", "HEAD");
        var modifications = await ExecuterGitAsync(chemin, "status", "--porcelain", "--untracked-files=no");
        var ancetre = tete.Reussi ? await ExecuterGitAsync(code, "merge-base", "--is-ancestor", tete.Sortie.Trim(), "HEAD") : tete;
        if (!modifications.Reussi || modifications.Sortie.Trim().Length > 0 || !ancetre.Reussi)
        {
            return (false, $"Projet « {nom} » non mis à jour : il a changé depuis le début de la demande (avance rapide impossible), et son dossier {chemin} n'a pas été touché. Le livrable reste disponible : {code}. Pour l'intégrer : {commande}");
        }

        if (await IntegrerAsync(chemin, code, id) is { } erreur)
        {
            return (false, $"Projet « {nom} » non mis à jour : {erreur} Le livrable reste disponible : {code}. Pour l'intégrer : {commande}");
        }

        return (true, $"Projet « {nom} » mis à jour en avance rapide : {chemin}.");
    }

    private static async Task<string?> IntegrerAsync(string chemin, string code, string id)
    {
        var erreur = await GitAsync(chemin, "fetch", "-q", "--no-tags", code, "HEAD")
            ?? await GitAsync(chemin, "merge", "--ff-only", "-q", "FETCH_HEAD");
        if (erreur is null)
        {
            await ExecuterGitAsync(chemin, "tag", "-f", $"usine/{id}", "HEAD");
        }

        return erreur;
    }

    private static async Task<string?> GitAsync(string? repertoire, params string[] arguments)
    {
        var resultat = await ExecuterGitAsync(repertoire, arguments);
        return resultat.Reussi ? null : $"git {arguments[0]} a échoué ({ExecuteurProcessus.Tronquer(resultat.SortieComplete.Trim(), 300)}).";
    }

    private static Task<ResultatCommande> ExecuterGitAsync(string? repertoire, params string[] arguments) =>
        ExecuteurProcessus.ExecuterAsync("git", repertoire is null ? arguments : ["-C", repertoire, .. arguments], DelaiGit);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex NomValide();
}
