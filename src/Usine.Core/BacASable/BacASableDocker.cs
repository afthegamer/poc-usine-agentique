using System.Diagnostics;
using System.Globalization;
using Usine.Core.Configuration;
using Usine.Core.Etat;

namespace Usine.Core.BacASable;

public sealed class BacASableDocker : IBacASable
{
    private const string ListeIgnores =
        "{ git -C /workspace ls-files -z -o -i --directory --exclude-per-directory=.gitignore -- ':(exclude,glob)**/.gitignore' 2>/dev/null || true; }";

    private const string RetraitCopieExport = "{ chmod -R u+w /tmp/usine-export; rm -rf /tmp/usine-export; } 2>/dev/null";

    internal const string RefDepart = "refs/usine/depart";

    private static readonly TimeSpan DelaiDocker = TimeSpan.FromMinutes(2);

    private static readonly string[] Exclusions =
    [
        "./node_modules", "*/node_modules", "./.angular", "*/.angular",
        "./.venv", "./venv", "*/__pycache__", "./.pytest_cache", "./.next", "./.nuxt", "./.cache",
        "./test-results", "./playwright-report",
    ];

    private static readonly string[] FichiersGitAExecution = ["config", "config.worktree", "commondir", "gitdir", Path.Combine("objects", "info", "alternates")];

    private static readonly string[] RepertoiresGitAExecution = ["hooks", "worktrees", "modules", "rebase-merge"];

    private readonly string idDemande;
    private readonly ConfigBacASable config;
    private readonly string repertoireAcceptationHote;
    private readonly string repertoireTravailHote;

    public BacASableDocker(string idDemande, ConfigBacASable config, string repertoireAcceptationHote, string repertoireTravailHote)
    {
        if (!DepotDemandes.EstIdValide(idDemande))
        {
            throw new ArgumentException($"Identifiant de demande invalide : « {idDemande} ».", nameof(idDemande));
        }

        this.idDemande = idDemande;
        this.config = config;
        this.repertoireAcceptationHote = Path.GetFullPath(repertoireAcceptationHote);
        this.repertoireTravailHote = Path.GetFullPath(repertoireTravailHote);
    }

    internal static string CommandeRetraitExclusions =>
        $"find . \\( {string.Join(" -o ", Exclusions.Select(e => $"-path '{e}'"))} \\) -prune -exec rm -rf -- {{}} + && {ListeIgnores} | xargs -0 -r rm -rf -- && find . -mindepth 1 -depth -type d -empty -not -path '*/.git' -not -path '*/.git/*' -delete";

    public string NomConteneur => $"usine-{idDemande}";

    private string Volume => $"usine-ws-{idDemande}";

    private string[] LimitesRessources => ["--cpus", config.Cpus.ToString(CultureInfo.InvariantCulture), "--memory", config.Memoire, "--pids-limit", "4096", "--security-opt", "no-new-privileges"];

    private string MarqueurInitialisation => Path.Combine(repertoireTravailHote, "volume.initialise");

    public async Task InitialiserAsync(string? depotSource, CancellationToken ct)
    {
        if (File.Exists(MarqueurInitialisation))
        {
            return;
        }

        DepotDemandes.CreerRepertoireAcceptation(repertoireAcceptationHote);
        var volume = await DockerAsync(["volume", "inspect", Volume], ct, verifier: false);
        if (!volume.Reussi)
        {
            await DockerAsync(["volume", "create", "--label", $"usine.demande={idDemande}", Volume], ct);
        }

        await PreparerAsync(ModeMontage.Construction, ct);
        if (!string.IsNullOrWhiteSpace(depotSource))
        {
            await ImporterDepotAsync(depotSource, ct);
        }

        await File.WriteAllTextAsync(MarqueurInitialisation, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), ct);
    }

    public async Task PreparerAsync(ModeMontage mode, CancellationToken ct)
    {
        var inspection = await DockerAsync(
            ["inspect", "-f", "{{.State.Running}}|{{index .Config.Labels \"usine.mode\"}}", NomConteneur],
            ct,
            verifier: false);
        if (inspection.Reussi)
        {
            var parties = inspection.Sortie.Trim().Split('|');
            var enMarche = parties[0] == "true";
            var modeActuel = parties.Length > 1 ? parties[1] : "";
            if (modeActuel == mode.ToString())
            {
                if (!enMarche)
                {
                    await DockerAsync(["start", NomConteneur], ct);
                }

                return;
            }

            await DockerAsync(["rm", "-f", NomConteneur], ct);
        }

        if (File.Exists(MarqueurInitialisation) && !await EspaceDeTravailExisteAsync(ct) && await DockerRepondAsync(ct))
        {
            throw new InvalidOperationException($"L'espace de travail de la demande {idDemande} (volume Docker {Volume}) a disparu : il n'est pas recréé vide.");
        }

        var espaceLectureSeule = mode is ModeMontage.Exploration or ModeMontage.EcritureTests;
        var acceptationLectureSeule = mode is not (ModeMontage.EcritureTests or ModeMontage.EcritureTestsSansCode);
        string[] espace = mode is ModeMontage.EcritureTestsSansCode ? [] : ["--mount", $"type=volume,src={Volume},dst=/workspace" + (espaceLectureSeule ? ",readonly" : "")];
        List<string> arguments =
        [
            "run", "-d", "--init",
            "--name", NomConteneur,
            "--label", $"usine.demande={idDemande}",
            "--label", $"usine.mode={mode}",
            .. LimitesRessources,
            .. espace,
            "--mount", $"type=bind,\"src={repertoireAcceptationHote}\",dst=/acceptance" + (acceptationLectureSeule ? ",readonly" : ""),
            "-w", "/workspace",
            config.Image,
            "sleep", "infinity",
        ];
        await DockerAsync(arguments, ct);
    }

    public async Task RedemarrerAsync(CancellationToken ct) => await DockerAsync(["restart", "-t", "0", NomConteneur], ct);

    public async Task<ResultatCommande> ExecuterAsync(string commande, TimeSpan delai, string repertoire = "/workspace", string? entree = null, CancellationToken ct = default)
    {
        var arguments = new List<string> { "exec" };
        if (entree is not null)
        {
            arguments.Add("-i");
        }

        arguments.AddRange(["-w", repertoire, NomConteneur, .. AvecDelai(commande, delai)]);
        var chrono = Stopwatch.StartNew();
        var resultat = await ExecuteurProcessus.ExecuterAsync("docker", arguments, delai + TimeSpan.FromSeconds(30), entree, ct: ct);
        return MarquerDelai(resultat, chrono.Elapsed, delai);
    }

    public async Task<ResultatCommande> ExecuterIsoleAsync(string commande, TimeSpan delai, CancellationToken ct)
    {
        var nom = $"usine-tests-{idDemande}";
        await DockerAsync(["rm", "-f", nom], ct, verifier: false);
        List<string> arguments =
        [
            "run", "--rm", "--init",
            "--name", nom,
            "--label", $"usine.demande={idDemande}",
            "--network", $"container:{NomConteneur}",
            .. LimitesRessources,
            "--mount", $"type=bind,\"src={repertoireAcceptationHote}\",dst=/acceptance,readonly",
            "-w", "/tmp",
            config.Image,
            .. AvecDelai(commande, delai),
        ];
        try
        {
            var chrono = Stopwatch.StartNew();
            var resultat = await ExecuteurProcessus.ExecuterAsync("docker", arguments, delai + TimeSpan.FromSeconds(60), ct: ct);
            return MarquerDelai(resultat, chrono.Elapsed, delai);
        }
        finally
        {
            await DockerAsync(["rm", "-f", nom], CancellationToken.None, verifier: false);
        }
    }

    public async Task<string?> ExporterAsync(string repertoireHote, CancellationToken ct)
    {
        var exclusions = string.Join(' ', Exclusions.Select(e => $"--exclude='{e}'"));
        var archive = await ExecuterAsync(
            $"set -o pipefail; {RetraitCopieExport}; rm -f /tmp/usine-export.tar && mkdir /tmp/usine-export "
            + $"&& tar -C /workspace {exclusions} -cf - . | tar -C /tmp/usine-export -xpf - "
            + $"&& {{ {ListeIgnores} | while IFS= read -r -d '' f; do p=\"/tmp/usine-export/${{f%/}}\"; if [ -e \"$p\" ] || [ -L \"$p\" ]; then chmod u+w -- \"$(dirname -- \"$p\")\"; if [ -d \"$p\" ] && [ ! -L \"$p\" ]; then chmod -R u+w -- \"$p\"; fi; rm -rf -- \"$p\" && printf '%s\\n' \"$f\"; fi; done; true; }} "
            + $"&& tar -C /tmp/usine-export -cf /tmp/usine-export.tar .; code=$?; {RetraitCopieExport}; exit $code",
            TimeSpan.FromMinutes(5),
            ct: ct);
        if (!archive.Reussi)
        {
            throw new InvalidOperationException($"Export impossible : {archive.SortieComplete}");
        }

        var archiveHote = Path.Combine(repertoireTravailHote, "export.tar");
        await DockerAsync(["cp", $"{NomConteneur}:/tmp/usine-export.tar", archiveHote], ct);
        SupprimerRepertoire(repertoireHote);
        Directory.CreateDirectory(repertoireHote);
        var extraction = await ExecuteurProcessus.ExecuterAsync("tar", ["-xf", archiveHote, "-C", repertoireHote], TimeSpan.FromMinutes(5), ct: ct);
        File.Delete(archiveHote);
        if (!extraction.Reussi && !Directory.EnumerateFileSystemEntries(repertoireHote).Any())
        {
            throw new InvalidOperationException($"Extraction de l'export impossible : {extraction.SortieComplete}");
        }

        var avertissementGit = await AssainirDepotGitAsync(repertoireHote, ct);
        var avertissementExport = extraction.Reussi
            ? avertissementGit
            : $"Export incomplet : certains fichiers n'ont pas pu être extraits sur ce poste ({ExecuteurProcessus.Tronquer(extraction.SortieComplete, 1500)}).{(avertissementGit is null ? "" : $" {avertissementGit}")}";
        var ignores = archive.Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var avertissementIgnores = ignores.Length == 0
            ? null
            : $"Fichiers ignorés par le .gitignore du projet, non exportés : {ExecuteurProcessus.Tronquer(string.Join(", ", ignores), 1500)}.";
        return (avertissementExport, avertissementIgnores) switch
        {
            (null, _) => avertissementIgnores,
            (_, null) => avertissementExport,
            _ => $"{avertissementExport} {avertissementIgnores}",
        };
    }

    public async Task SupprimerAsync(bool avecVolume, CancellationToken ct)
    {
        await DockerAsync(["rm", "-f", NomConteneur], ct, verifier: false);
        if (avecVolume)
        {
            await DockerAsync(["volume", "rm", "-f", Volume], ct);
            if (File.Exists(MarqueurInitialisation))
            {
                File.Delete(MarqueurInitialisation);
            }
        }
    }

    public async Task<bool> EspaceDeTravailExisteAsync(CancellationToken ct)
    {
        if (!File.Exists(MarqueurInitialisation))
        {
            return false;
        }

        var volume = await DockerAsync(["volume", "inspect", Volume], ct, verifier: false);
        return volume.Reussi;
    }

    public async Task<bool> DockerRepondAsync(CancellationToken ct) =>
        (await DockerAsync(["version", "--format", "{{.Server.Version}}"], ct, verifier: false)).Reussi;

    public static async Task<ResultatCommande> DockerAsync(IEnumerable<string> arguments, CancellationToken ct, bool verifier = true, TimeSpan? delai = null)
    {
        var liste = arguments.ToList();
        var resultat = await ExecuteurProcessus.ExecuterAsync("docker", liste, delai ?? DelaiDocker, ct: ct);
        if (verifier && !resultat.Reussi)
        {
            throw new InvalidOperationException($"La commande « docker {string.Join(' ', liste.Take(3))} … » a échoué : {resultat.SortieComplete}");
        }

        return resultat;
    }

    public static void SupprimerRepertoire(string chemin)
    {
        if (!Directory.Exists(chemin))
        {
            return;
        }

        var racine = new DirectoryInfo(chemin);
        if (racine.LinkTarget is not null)
        {
            racine.Delete();
            return;
        }

        SupprimerLiens(chemin);
        foreach (var fichier in Directory.EnumerateFiles(chemin, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(fichier, FileAttributes.Normal);
        }

        Directory.Delete(chemin, recursive: true);
    }

    private static void SupprimerLiens(string repertoire)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var liens = Directory.EnumerateDirectories(repertoire, "*", options)
            .Prepend(repertoire)
            .SelectMany(d => new DirectoryInfo(d).EnumerateFileSystemInfos())
            .Where(i => i.LinkTarget is not null)
            .ToList();
        foreach (var lien in liens)
        {
            lien.Delete();
        }
    }

    public static bool EstDepotGit(string chemin)
    {
        var git = Path.Combine(chemin, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    private static IEnumerable<string> AvecDelai(string commande, TimeSpan delai) =>
        ["timeout", "-k", "10", Math.Max(1, (int)Math.Ceiling(delai.TotalSeconds)).ToString(CultureInfo.InvariantCulture), "bash", "-c", commande];

    internal static ResultatCommande MarquerDelai(ResultatCommande resultat, TimeSpan duree, TimeSpan delai) =>
        resultat.Code is 124 or 137 && duree >= delai ? resultat with { DelaiDepasse = true } : resultat;

    internal static void SupprimerDepotsGitImbriques(string repertoire)
    {
        var tete = Path.Combine(repertoire, "HEAD");
        if ((File.Exists(tete) || new FileInfo(tete).LinkTarget is not null)
            && (File.Exists(Path.Combine(repertoire, "commondir"))
                || (Path.Exists(Path.Combine(repertoire, "objects")) && Path.Exists(Path.Combine(repertoire, "refs")))))
        {
            if (new FileInfo(tete).LinkTarget is null)
            {
                File.SetAttributes(tete, FileAttributes.Normal);
            }

            if (Directory.Exists(tete))
            {
                Directory.Delete(tete);
            }
            else
            {
                File.Delete(tete);
            }
        }

        var comparaison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var dossier in new DirectoryInfo(repertoire).EnumerateDirectories()
            .Where(d => d.LinkTarget is null && !string.Equals(d.Name, ".git", comparaison)))
        {
            SupprimerDepotGitEtImbriques(dossier);
        }
    }

    private static void SupprimerDepotGitEtImbriques(DirectoryInfo dossier)
    {
        foreach (var git in dossier.EnumerateFileSystemInfos(".git").ToList())
        {
            if (git.LinkTarget is not null)
            {
                git.Delete();
            }
            else if (git is DirectoryInfo)
            {
                SupprimerRepertoire(git.FullName);
            }
            else
            {
                File.SetAttributes(git.FullName, FileAttributes.Normal);
                File.Delete(git.FullName);
            }
        }

        SupprimerDepotsGitImbriques(dossier.FullName);
    }

    internal static async Task<string?> AssainirDepotGitAsync(string repertoire, CancellationToken ct)
    {
        SupprimerDepotsGitImbriques(repertoire);
        var git = Path.Combine(repertoire, ".git");
        if (File.Exists(git))
        {
            File.Delete(git);
            return null;
        }

        if (!Directory.Exists(git))
        {
            return null;
        }

        if (new DirectoryInfo(git).LinkTarget is not null)
        {
            Directory.Delete(git);
            return null;
        }

        SupprimerLiens(git);
        foreach (var fichier in FichiersGitAExecution.Select(f => Path.Combine(git, f)).Where(File.Exists))
        {
            File.SetAttributes(fichier, FileAttributes.Normal);
            File.Delete(fichier);
        }

        foreach (var dossier in RepertoiresGitAExecution)
        {
            SupprimerRepertoire(Path.Combine(git, dossier));
        }

        foreach (var dossier in new DirectoryInfo(git).EnumerateDirectories().Where(d => d.LinkTarget is null))
        {
            SupprimerDepotGitEtImbriques(dossier);
        }

        var reinitialisation = await ExecuteurProcessus.ExecuterAsync("git", ["-C", repertoire, "init", "-q"], TimeSpan.FromMinutes(1), ct: ct);
        if (!reinitialisation.Reussi)
        {
            SupprimerRepertoire(git);
            return $"Export sans historique git : le dépôt n'a pas pu être réinitialisé sur ce poste (git est-il installé ?) et a été retiré ({ExecuteurProcessus.Tronquer(reinitialisation.SortieComplete, 300)}).";
        }

        var referencesUsine = await ExecuteurProcessus.ExecuterAsync("git", ["-C", repertoire, "for-each-ref", "--format=%(refname)", "refs/usine/"], TimeSpan.FromMinutes(1), ct: ct);
        var referencesASupprimer = referencesUsine.Reussi
            ? referencesUsine.Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        if (referencesASupprimer.Length > 0)
        {
            foreach (var reference in referencesASupprimer)
            {
                await ExecuteurProcessus.ExecuterAsync("git", ["-C", repertoire, "update-ref", "-d", reference], TimeSpan.FromMinutes(1), ct: ct);
            }

            await ExecuteurProcessus.ExecuterAsync("git", ["-C", repertoire, "gc", "--quiet", "--force", "--prune=now"], TimeSpan.FromMinutes(5), ct: ct);
        }

        var index = Path.Combine(git, "index");
        if (!File.Exists(index))
        {
            return null;
        }

        File.SetAttributes(index, FileAttributes.Normal);
        File.Delete(index);
        var reconstruction = await ExecuteurProcessus.ExecuterAsync("git", ["--git-dir", git, "--work-tree", repertoire, "reset", "-q"], TimeSpan.FromMinutes(1), ct: ct);
        return reconstruction.Reussi
            ? null
            : $"Export sans index git : l'index du dépôt a été retiré et n'a pas pu être reconstruit sur ce poste ({ExecuteurProcessus.Tronquer(reconstruction.SortieComplete, 300)}).";
    }

    private async Task ImporterDepotAsync(string depotSource, CancellationToken ct)
    {
        var source = Path.GetFullPath(depotSource);
        if (!EstDepotGit(source))
        {
            throw new InvalidOperationException($"« {source} » n'est pas un dépôt git.");
        }

        var paquet = Path.Combine(repertoireTravailHote, "import-depot.bundle");
        File.Delete(paquet);
        var empaquetage = await ExecuteurProcessus.ExecuterAsync("git", ["-C", source, "bundle", "create", paquet, "HEAD"], TimeSpan.FromMinutes(10), ct: ct);
        if (!empaquetage.Reussi)
        {
            throw new InvalidOperationException($"Empaquetage de « {source} » impossible (le dépôt a-t-il au moins un commit ?) : {empaquetage.SortieComplete}");
        }

        try
        {
            await DockerAsync(["cp", paquet, $"{NomConteneur}:/tmp/usine-depot.bundle"], ct, delai: TimeSpan.FromMinutes(10));
        }
        finally
        {
            File.Delete(paquet);
        }

        var import = await ExecuterAsync(
            "git init -q /workspace && git -C /workspace fetch -q /tmp/usine-depot.bundle HEAD "
            + $"&& git -C /workspace checkout -q -B usine/{idDemande} FETCH_HEAD && git -C /workspace update-ref {RefDepart} FETCH_HEAD; code=$?; rm -f /tmp/usine-depot.bundle 2>/dev/null; exit $code",
            TimeSpan.FromMinutes(10),
            ct: ct);
        if (!import.Reussi)
        {
            throw new InvalidOperationException($"Import du dépôt dans le bac à sable impossible : {import.SortieComplete}");
        }
    }
}

public sealed class FabriqueBacASableDocker(ConfigUsine config, DepotDemandes depot) : IFabriqueBacASable
{
    public IBacASable Creer(string idDemande) =>
        new BacASableDocker(idDemande, config.BacASable, depot.RepertoireAcceptation(idDemande), depot.Repertoire(idDemande));
}
