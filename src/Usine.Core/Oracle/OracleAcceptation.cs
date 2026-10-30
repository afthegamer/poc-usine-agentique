using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Usine.Core.BacASable;
using Usine.Core.Configuration;
using Usine.Core.Etat;

namespace Usine.Core.Oracle;

public sealed record OptionsVerification(bool Construire, bool Demarrer, string? EmpreinteAttendue, bool Essai = false, Action<string>? Journaliser = null, bool Sondes = false, IReadOnlyList<string>? TestsInstables = null, bool IsolerEchecs = false, bool PersistanceExigee = false, bool ApplicationVide = false);

public interface IOracle
{
    Task<ResultatOracle> VerifierAsync(IBacASable bac, string repertoireAcceptationHote, OptionsVerification options, CancellationToken ct);

    Task<ResultatListe> ListerTestsAsync(IBacASable bac, CancellationToken ct);

    Task<ResultatCommande> ConstruireSiBesoinAsync(IBacASable bac, CancellationToken ct);

    Task<ResultatCommande> DemarrerApplicationAsync(IBacASable bac, CancellationToken ct);

    Task ArreterApplicationAsync(IBacASable bac, CancellationToken ct);
}

public sealed record ResultatListe(bool Reussi, IReadOnlyList<string> Titres, string Sortie, IReadOnlyList<string>? FichiersDesTitres = null);

public sealed class OracleAcceptation(ConfigUsine config, TimeProvider? temps = null) : IOracle
{
    public const string DossierNonRegression = "non-regression";

    private const string PreparationTests =
        "rm -rf /tmp/acc && mkdir -p /tmp/acc && cp -r /acceptance/. /tmp/acc/ " +
        "&& ln -sfn /opt/acceptance-runner/node_modules /tmp/acc/node_modules " +
        "&& cp /opt/acceptance-runner/playwright.config.ts /tmp/acc/playwright.config.ts";

    private static readonly string ExportPages =
        "find /tmp/acc/test-results -name error-context.md -print0 2>/dev/null | while IFS= read -r -d '' f; do "
        + "printf '\\n" + LecteurRapportPlaywright.MarqueurPage + " %s %s\\n' \"$(printf %s \"$f\" | base64 -w0)\" "
        + "\"$(awk '/^```yaml$/{p=1;next} p&&/^```$/{exit} p' \"$f\" | head -c " + LecteurRapportPlaywright.OctetsPageMax.ToString(CultureInfo.InvariantCulture) + " | base64 -w0)\"; done";

    private const string JournalSondes = "/tmp/usine-journal";

    private const string JournalApplication = "tail -c 6000 /tmp/usine-app.log";

    private const string ScriptSonde = "/opt/acceptance-runner/usine-sonde.mjs";

    private const int DelaiSondeSecondes = 240;

    private const int MargeSondeSecondes = 270;

    private const string RapportTests = "/tmp/usine-rapport.json";

    private const string ScriptInstablesConnus =
        $$"""const texte=require("fs").readFileSync("{{RapportTests}}","utf8");const rapport=JSON.parse(texte.slice(texte.indexOf("{"),texte.lastIndexOf("}")+1));"""
        + """const connus=new Set(JSON.parse(Buffer.from(process.argv[1],"base64").toString("utf8")));const echecs=[];"""
        + """const parcourir=(suite,chemin,fichier)=>{const courant=fichier||!suite.title?chemin:[...chemin,suite.title];"""
        + """for(const spec of suite.specs||[])for(const test of spec.tests||[])if(test.status!=="expected"&&test.status!=="flaky")echecs.push([...courant,spec.title||""].join(" › "));"""
        + """for(const sous of suite.suites||[])parcourir(sous,courant,false)};for(const suite of rapport.suites||[])parcourir(suite,[],true);"""
        + """process.exit((rapport.errors||[]).length===0&&echecs.length>0&&echecs.every(titre=>connus.has(titre))?0:1);""";

    private const int MaxEchecsIsoles = 2;

    private const string ScriptGestes =
        """const fs=require("fs"),chemin=require("path");const gestes=[];let fichiers=[];try{fichiers=fs.readdirSync(process.argv[1]).filter(f=>f.endsWith(".jsonl")).sort()}catch{}"""
        + """for(const f of fichiers){let texte="";try{texte=fs.readFileSync(chemin.join(process.argv[1],f),"utf8")}catch{}for(const ligne of texte.split("\n")){let o=null;try{o=JSON.parse(ligne)}catch{}"""
        + """if(o&&o.type==="requete"&&typeof o.methode==="string"&&typeof o.url==="string"){let id=null;try{const r=JSON.parse(o.reponse);for(const c of ["id","Id","_id"])if(id===null&&r&&(typeof r[c]==="string"||typeof r[c]==="number"))id=String(r[c])}catch{}"""
        + """gestes.push([o.methode.toUpperCase(),o.url,Number.isInteger(o.statut)?o.statut:null,id])}else if(o&&o.type==="soumission"&&typeof o.url==="string")gestes.push(["PAGE",o.url,null,null])}}"""
        + $$"""console.log("{{LecteurRapportPlaywright.MarqueurGestes}} "+Buffer.from(JSON.stringify(gestes.slice(-400)),"utf8").toString("base64"))""";

    private const string LectureRelue = "/tmp/usine-relecture";

    private const int AttenteArretSecondes = 10;

    private const string CaracteresRegex = "\\^$.*+?()[]{}|/";

    private const string MarqueurConstruction = "/tmp/usine-construit";

    private const string CopieApplication = "/tmp/usine-app";

    private const string CopieDepart = "/tmp/usine-depart";

    private const string MaisonDepart = "/tmp/usine-depart-home";

    private const string MarqueurDepart = "/tmp/usine-depart-construit";

    private const string DonneesDepart = "/tmp/usine-depart-donnees.tar";

    private const string RepriseImpossible = "/tmp/usine-depart-impossible";

    private const string ScriptApplicationVide = "require(\"http\").createServer((requete,reponse)=>reponse.end()).listen(Number(process.env.PORT))";

    private const string FichierConfigApplicationVide = "playwright.vide.config.ts";

    private const string EnvironnementDepart = $"NPM_CONFIG_PREFIX={MaisonDepart}/.npm-global PATH={MaisonDepart}/.npm-global/bin:$PATH HOME={MaisonDepart} ";

    private const string EtatDepart =
        $"if [ -f {DonneesDepart} ]; then echo REPRISE; tar -tf {DonneesDepart}; elif [ -f {RepriseImpossible} ]; then echo IMPOSSIBLE; "
        + $"else git rev-parse -q --verify '{BacASableDocker.RefDepart}^{{commit}}' 2>/dev/null | sed 's/^/DEPART /'; fi; true";

    private static readonly string[] DossiersHorsDonnees = ["node_modules", ".angular", "__pycache__", ".venv", "venv", ".pytest_cache", ".next", ".nuxt", ".cache", "test-results", "playwright-report", "bin", "obj", "dist"];

    private static readonly string ExclusionsDonnees = string.Join(' ', DossiersHorsDonnees.Select(d => $"':(exclude,glob)**/{d}/**'"));

    private readonly TimeProvider horloge = temps ?? TimeProvider.System;

    private ConfigLimites Limites => config.Limites;

    private int Port => config.BacASable.Port;

    private string ConfigApplicationVide =>
        "import base from './playwright.config';\n"
        + $"export default {{ ...base, testIgnore: '/tmp/acc/tests/{DossierNonRegression}/**', fullyParallel: true, workers: 4, timeout: 5000, globalTimeout: {(Limites.DelaiTestsSecondes * 4 / 5 * 1000).ToString(CultureInfo.InvariantCulture)}, expect: {{ ...base.expect, timeout: 2000 }} }};\n";

    private string SondeDeSaisie =>
        $"if [ $code -eq 0 ]; then if [ -f {ScriptSonde} ]; then timeout {DelaiSondeSecondes.ToString(CultureInfo.InvariantCulture)} node {ScriptSonde} --journal {JournalSondes} --base http://localhost:{Port} --contrat /acceptance/CONTRAT.md --delai 150 --max-rejeux 400 > /tmp/usine-sonde.out 2> /tmp/usine-sonde.err; "
        + $"grep -a -E '^@@usine-(signal|sonde) ' /tmp/usine-sonde.out; else echo {LecteurRapportPlaywright.MarqueurSondeAbsente}; fi; timeout 10 node -e '{ScriptGestes}' {JournalSondes} 2>/dev/null; fi";

    private static string SeulsInstablesConnusEnEchec(IReadOnlyList<string> titres) =>
        $"if [ $code -eq 1 ] && node -e '{ScriptInstablesConnus}' {Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(titres)))} 2>/dev/null; then code=0; fi";

    public async Task<ResultatOracle> VerifierAsync(IBacASable bac, string repertoireAcceptationHote, OptionsVerification options, CancellationToken ct)
    {
        var chrono = Stopwatch.StartNew();
        var resultat = await VerifierSansDureeAsync(bac, repertoireAcceptationHote, options, ct);
        resultat.Date = horloge.GetUtcNow();
        resultat.DureeSecondes = Math.Round(chrono.Elapsed.TotalSeconds, 1);
        return resultat;
    }

    public async Task<ResultatListe> ListerTestsAsync(IBacASable bac, CancellationToken ct)
    {
        var commande = $"{PreparationTests} && cd /tmp/acc && npx playwright test --config=playwright.config.ts --list --reporter=json";
        var resultat = await bac.ExecuterAsync(commande, TimeSpan.FromMinutes(3), "/tmp", ct: ct);
        if (!resultat.Reussi)
        {
            try
            {
                if (LecteurRapportPlaywright.ErreursGlobales(LecteurRapportPlaywright.ExtraireJson(resultat.Sortie)) is { Count: > 0 } erreurs)
                {
                    return new ResultatListe(false, [], string.Join("\n", erreurs));
                }
            }
            catch (JsonException)
            {
            }

            return new ResultatListe(false, [], resultat.SortieComplete);
        }

        try
        {
            var json = LecteurRapportPlaywright.ExtraireJson(resultat.Sortie);
            var erreurs = LecteurRapportPlaywright.ErreursGlobales(json);
            var tests = LecteurRapportPlaywright.Lire(json);
            var titres = tests.Select(t => t.Titre).ToList();
            return erreurs.Count > 0
                ? new ResultatListe(false, titres, string.Join("\n", erreurs))
                : new ResultatListe(titres.Count > 0, titres, titres.Count > 0 ? "" : "Aucun test trouvé dans /acceptance/tests.", [.. tests.Select(t => $"tests/{t.Fichier}")]);
        }
        catch (JsonException ex)
        {
            return new ResultatListe(false, [], $"Sortie illisible ({ex.Message}) : {resultat.SortieComplete}");
        }
    }

    public async Task<ResultatCommande> ConstruireSiBesoinAsync(IBacASable bac, CancellationToken ct)
    {
        var marqueur = await bac.ExecuterAsync($"test -f {MarqueurConstruction}", TimeSpan.FromSeconds(20), ct: ct);
        if (marqueur.Reussi)
        {
            return marqueur;
        }

        var construction = await ConstruireAsync(bac, ct);
        return construction.Reussi ? construction : construction with { Sortie = EchecConstruction(construction), Erreurs = "" };
    }

    public Task<ResultatCommande> DemarrerApplicationAsync(IBacASable bac, CancellationToken ct) => DemarrerAsync(bac, "/workspace", "", null, ct);

    private async Task<ResultatCommande> DemarrerAsync(IBacASable bac, string source, string environnement, string? donnees, CancellationToken ct)
    {
        await bac.RedemarrerAsync(ct);
        var lancement = await bac.ExecuterAsync(
            "rm -f /tmp/usine-app.pid /tmp/usine-app.log; " +
            "test -f usine-start.sh || { echo 'usine-start.sh introuvable à la racine du projet.'; exit 3; }; " +
            $"if curl -s -o /dev/null --max-time 2 http://localhost:{Port}/; then echo \"Le port {Port} est déjà occupé avant le démarrage de l'application.\"; exit 4; fi; " +
            "chmod +x usine-start.sh 2>/dev/null; " +
            $"rm -rf {CopieApplication} && cp -a {source} {CopieApplication} || {{ echo \"La copie de {source} dans {CopieApplication}, d'où l'application est lancée, a échoué.\"; exit 5; }}; " +
            (donnees is null ? "" : $"tar -xpf {donnees} -C {CopieApplication} || {{ echo \"La reprise des données de la version de départ dans {CopieApplication} a échoué.\"; exit 5; }}; ") +
            $"{environnement}PORT={Port} setsid nohup bash -c 'cd {CopieApplication} && exec ./usine-start.sh' > /tmp/usine-app.log 2>&1 < /dev/null & echo $! > /tmp/usine-app.pid",
            TimeSpan.FromMinutes(5),
            source,
            ct: ct);
        return lancement.Reussi ? await AttendreReponseAsync(bac, ct) : lancement;
    }

    private async Task<ResultatCommande> DemarrerApplicationVideAsync(IBacASable bac, CancellationToken ct)
    {
        var lancement = await bac.ExecuterAsync(
            "rm -f /tmp/usine-app.pid /tmp/usine-app.log; " +
            $"if curl -s -o /dev/null --max-time 2 http://localhost:{Port}/; then echo \"Le port {Port} est déjà occupé avant le démarrage de l'application vide.\"; exit 4; fi; " +
            $"PORT={Port} setsid nohup node -e '{ScriptApplicationVide}' > /tmp/usine-app.log 2>&1 < /dev/null & echo $! > /tmp/usine-app.pid",
            TimeSpan.FromMinutes(1),
            "/tmp",
            ct: ct);
        return lancement.Reussi ? await AttendreReponseAsync(bac, ct) : lancement;
    }

    private async Task<ResultatCommande> AttendreReponseAsync(IBacASable bac, CancellationToken ct)
    {
        var limite = horloge.GetUtcNow().AddSeconds(Limites.DelaiDemarrageSecondes);
        while (horloge.GetUtcNow() < limite)
        {
            ct.ThrowIfCancellationRequested();
            var sonde = await bac.ExecuterAsync(
                $"code=$(curl -s -o /dev/null -w '%{{http_code}}' --max-time 5 http://localhost:{Port}/ || true); " +
                "if [ \"$code\" != \"000\" ] && [ -n \"$code\" ]; then echo \"OK $code\"; exit 0; fi; " +
                "if ! kill -0 $(cat /tmp/usine-app.pid) 2>/dev/null; then echo ARRETEE; exit 2; fi; exit 1",
                TimeSpan.FromSeconds(20),
                ct: ct);
            if (sonde.Code == 0)
            {
                return sonde;
            }

            if (sonde.Code == 2)
            {
                var journal = await bac.ExecuterAsync(JournalApplication, TimeSpan.FromSeconds(20), ct: ct);
                return new ResultatCommande(2, $"L'application s'est arrêtée au démarrage.\n{journal.Sortie}", "", false);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), horloge, ct);
        }

        var journalFinal = await bac.ExecuterAsync(JournalApplication, TimeSpan.FromSeconds(20), ct: ct);
        await ArreterApplicationAsync(bac, CancellationToken.None);
        return new ResultatCommande(1, $"L'application ne répond pas sur le port {Port} après {Limites.DelaiDemarrageSecondes} s.\n{journalFinal.Sortie}", "", true);
    }

    public async Task ArreterApplicationAsync(IBacASable bac, CancellationToken ct)
    {
        await bac.ExecuterAsync(
            "if [ -f /tmp/usine-app.pid ]; then kill -TERM -- -$(cat /tmp/usine-app.pid) 2>/dev/null; sleep 1; kill -KILL -- -$(cat /tmp/usine-app.pid) 2>/dev/null; rm -f /tmp/usine-app.pid; fi; true",
            TimeSpan.FromSeconds(30),
            ct: ct);
    }

    private async Task<ResultatOracle> VerifierSansDureeAsync(IBacASable bac, string repertoireAcceptationHote, OptionsVerification options, CancellationToken ct)
    {
        if (options.EmpreinteAttendue is { Length: > 0 } attendue)
        {
            var actuelle = Empreinte.Calculer(repertoireAcceptationHote);
            if (!string.Equals(actuelle, attendue, StringComparison.Ordinal))
            {
                return Echec("empreinte", "Les tests d'acceptation ont été modifiés depuis leur validation : l'empreinte ne correspond plus.", empreinteValide: false);
            }
        }

        if (options.Construire && !options.Essai)
        {
            await bac.SupprimerAsync(avecVolume: false, ct);
            await bac.PreparerAsync(ModeMontage.Construction, ct);
            var retrait = await bac.ExecuterAsync(BacASableDocker.CommandeRetraitExclusions, TimeSpan.FromMinutes(5), ct: ct);
            if (!retrait.Reussi)
            {
                await ExigerBacDisponibleAsync(bac, ct);
                return Echec("build", Resume("Le retrait des dossiers exclus du livrable (node_modules, .venv, venv…), des fichiers ignorés par le .gitignore et des dossiers vides avant la construction a échoué", retrait));
            }
        }
        else if (options.Construire || options.Demarrer)
        {
            await bac.RedemarrerAsync(ct);
        }

        if (options.Construire)
        {
            var construction = await ConstruireAsync(bac, ct);
            if (!construction.Reussi)
            {
                await ExigerBacDisponibleAsync(bac, ct);
                return Echec("build", EchecConstruction(construction));
            }
        }

        var reprise = options.Demarrer ? await ReprendreDonneesDepartAsync(bac, options.Journaliser, ct) : [];
        var demarree = false;
        try
        {
            if (options.Demarrer)
            {
                var demarrage = await DemarrerAsync(bac, "/workspace", "", reprise.Count > 0 ? DonneesDepart : null, ct);
                if (demarrage.Code != 0)
                {
                    await ExigerBacDisponibleAsync(bac, ct);
                    return Echec("demarrage", AvecReprise(reprise, Resume("Le démarrage (usine-start.sh) a échoué", demarrage)));
                }

                demarree = true;
            }
            else if (options.ApplicationVide)
            {
                var demarrage = await DemarrerApplicationVideAsync(bac, ct);
                if (demarrage.Code != 0)
                {
                    await ExigerBacDisponibleAsync(bac, ct);
                    return Echec("demarrage", Resume("Le démarrage de l'application vide a échoué", demarrage));
                }

                demarree = true;
            }

            var sondes = options.Sondes ? $"mkdir -p {JournalSondes} && USINE_GARDE=1 USINE_JOURNAL={JournalSondes} " : "";
            var borneTests = options.Sondes ? $"timeout -k 10 {Limites.DelaiTestsSecondes.ToString(CultureInfo.InvariantCulture)} " : "";
            var configApplicationVide = options.ApplicationVide ? $" && printf %s {Convert.ToBase64String(Encoding.UTF8.GetBytes(ConfigApplicationVide))} | base64 -d > /tmp/acc/{FichierConfigApplicationVide}" : "";
            var lancement = $"{PreparationTests}{configApplicationVide} && cd /tmp/acc && {sondes}BASE_URL=http://localhost:{Port} {borneTests}npx playwright test --config={(options.ApplicationVide ? FichierConfigApplicationVide : "playwright.config.ts")} --reporter=json";
            var chronoTests = Stopwatch.StartNew();
            var execution = await bac.ExecuterIsoleAsync(
                options is { Sondes: true, TestsInstables: { Count: > 0 } connus }
                    ? $"{lancement} > {RapportTests}; code=$?; cat {RapportTests}; {ExportPages}; {SeulsInstablesConnusEnEchec(connus)}; {SondeDeSaisie}; exit $code"
                    : $"{lancement}; code=$?; {ExportPages}{(options.Sondes ? $"; {SondeDeSaisie}" : "")}; exit $code",
                TimeSpan.FromSeconds(Limites.DelaiTestsSecondes + (options.Sondes ? MargeSondeSecondes : 0)),
                ct);
            if (options.Sondes)
            {
                execution = BacASableDocker.MarquerDelai(execution, chronoTests.Elapsed, TimeSpan.FromSeconds(Limites.DelaiTestsSecondes));
            }

            if (execution.Code == 125 && execution.Sortie.Length == 0)
            {
                throw new InvalidOperationException($"Le conteneur des tests d'acceptation n'a pas pu être lancé : {ExecuteurProcessus.Tronquer(execution.SortieComplete, 1500)}");
            }

            if (options.ApplicationVide && execution.DelaiDepasse)
            {
                await ExigerBacDisponibleAsync(bac, ct);
                return Echec("tests", Resume($"Délai de {Limites.DelaiTestsSecondes} s dépassé pendant les tests contre l'application vide", execution));
            }

            ResultatOracle resultat;
            try
            {
                var json = LecteurRapportPlaywright.ExtraireJson(execution.Sortie);
                resultat = LecteurRapportPlaywright.VersResultat(LecteurRapportPlaywright.Lire(json, LecteurRapportPlaywright.PagesEnEchec(execution.Sortie)), LecteurRapportPlaywright.ErreursGlobales(json), options.TestsInstables);
            }
            catch (JsonException ex)
            {
                await ExigerBacDisponibleAsync(bac, ct);
                return Echec("tests", Resume($"Les tests d'acceptation n'ont pas produit de rapport lisible ({ex.Message})", execution));
            }

            if (resultat.Total == 0 && resultat.Echecs.Count == 0)
            {
                return Echec("tests", Resume("Aucun test d'acceptation n'a été exécuté", execution));
            }

            if (options.Sondes && resultat.Succes)
            {
                foreach (var signal in LecteurRapportPlaywright.SignauxSonde(execution.Sortie))
                {
                    if (!resultat.Signaux.Exists(s => string.Equals(s.Cle, signal.Cle, StringComparison.Ordinal)))
                    {
                        resultat.Signaux.Add(signal);
                    }
                }

                if (LecteurRapportPlaywright.BilanSonde(execution.Sortie) is { } bilan)
                {
                    options.Journaliser?.Invoke(bilan);
                }
                else if (LecteurRapportPlaywright.SondeAbsente(execution.Sortie))
                {
                    options.Journaliser?.Invoke("Sonde indisponible : l'image du bac à sable ne la contient pas, reconstruis-la (usine image --forcer).");
                }
                else
                {
                    options.Journaliser?.Invoke($"Sonde de saisie sans bilan : elle s'est arrêtée sur une panne ou a dépassé son délai de {DelaiSondeSecondes.ToString(CultureInfo.InvariantCulture)} s ; ses écarts déjà relevés sont gardés.");
                }

                if (demarree && await MotifRelectureAsync(bac, reprise, options, ct) is { } motifRelecture)
                {
                    await VerifierRedemarrageAsync(bac, LecteurRapportPlaywright.GestesJournal(execution.Sortie), motifRelecture, resultat, options.Journaliser, ct);
                }
            }

            if (!resultat.Succes && execution.DelaiDepasse)
            {
                resultat.Journal = $"Délai de {Limites.DelaiTestsSecondes} s dépassé pendant les tests.";
            }

            if (!resultat.Succes && demarree)
            {
                var journal = await bac.ExecuterAsync("tail -c 3000 /tmp/usine-app.log", TimeSpan.FromSeconds(20), ct: ct);
                if (journal.Reussi && !string.IsNullOrWhiteSpace(journal.Sortie))
                {
                    var sortieApplication = $"Sortie de l'application pendant les tests (fin de /tmp/usine-app.log, erreurs du serveur comprises) :\n{journal.Sortie.TrimEnd()}";
                    resultat.Journal = resultat.Journal is { Length: > 0 } delai ? $"{delai}\n\n{sortieApplication}" : sortieApplication;
                }
            }

            if (options.IsolerEchecs && resultat is { Succes: false, EtapeEchec: "tests", Echecs.Count: <= MaxEchecsIsoles })
            {
                (resultat.Dependants, resultat.EchouentSeuls) = await RejouerSeulsAsync(bac, [.. resultat.Echecs.Select(e => e.Titre).Distinct(StringComparer.Ordinal)], reprise, options.Journaliser, ct);
            }

            if (!resultat.Succes && reprise.Count > 0)
            {
                resultat.Journal = AvecReprise(reprise, resultat.Journal ?? "");
            }

            return resultat;
        }
        finally
        {
            if (demarree)
            {
                await ArreterApplicationAsync(bac, CancellationToken.None);
            }
        }
    }

    private async Task<(List<string> Dependants, List<string> EchouentSeuls)> RejouerSeulsAsync(IBacASable bac, IReadOnlyList<string> titres, IReadOnlyList<string> reprise, Action<string>? journaliser, CancellationToken ct)
    {
        var dependants = new List<string>();
        var echouentSeuls = new List<string>();
        foreach (var titre in titres)
        {
            var demarrage = await DemarrerAsync(bac, "/workspace", "", reprise.Count > 0 ? DonneesDepart : null, ct);
            if (demarrage.Code != 0)
            {
                journaliser?.Invoke($"Rejeu seul de « {titre} » impossible : l'application n'a pas redémarré ({ExecuteurProcessus.Tronquer(demarrage.SortieComplete, 300).Trim().ReplaceLineEndings(" ")}).");
                break;
            }

            var motif = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join(' ', titre.Split(" › ").Select(EchapperRegex))));
            var execution = await bac.ExecuterIsoleAsync(
                $"{PreparationTests} && cd /tmp/acc && BASE_URL=http://localhost:{Port} npx playwright test --config=playwright.config.ts --reporter=json --grep \"$(printf %s {motif} | base64 -d)\"",
                TimeSpan.FromSeconds(Limites.DelaiTestsSecondes),
                ct);
            bool reussi;
            try
            {
                var rejoues = LecteurRapportPlaywright.Lire(LecteurRapportPlaywright.ExtraireJson(execution.Sortie)).Where(t => string.Equals(t.Titre, titre, StringComparison.Ordinal)).ToList();
                reussi = execution.Code == 0 && rejoues.Count > 0 && rejoues.TrueForAll(t => t.Reussi);
            }
            catch (JsonException)
            {
                reussi = false;
            }

            if (reussi)
            {
                dependants.Add(titre);
            }
            else
            {
                echouentSeuls.Add(titre);
            }

            journaliser?.Invoke($"Rejeu seul sur une instance neuve de l'application, démarrée avec les mêmes données : « {titre} » {(reussi ? "réussit, il dépend de l'état laissé par les autres tests" : "échoue aussi")}.");
        }

        return (dependants, echouentSeuls);
    }

    private async Task<string?> MotifRelectureAsync(IBacASable bac, IReadOnlyList<string> reprise, OptionsVerification options, CancellationToken ct)
    {
        if (reprise.Count > 0)
        {
            return "données reprises de la version de départ";
        }

        if (options.PersistanceExigee)
        {
            return "persistance exigée par la demande";
        }

        var ecrits = await bac.ExecuterAsync(
            $"cd {CopieApplication} && git ls-files -z -o -- . {ExclusionsDonnees} | while IFS= read -r -d '' f; do if [ \"$f\" -nt {MarqueurConstruction} ]; then printf '%s\\n' \"$f\"; fi; done",
            TimeSpan.FromMinutes(1),
            ct: ct);
        var fichiers = ecrits.Reussi ? ecrits.Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        return fichiers.Length == 0 ? null : $"fichiers écrits par l'application : {ExecuteurProcessus.Tronquer(string.Join(", ", fichiers), 200)}";
    }

    private async Task VerifierRedemarrageAsync(IBacASable bac, IReadOnlyList<GesteJournal> gestes, string motif, ResultatOracle resultat, Action<string>? journaliser, CancellationToken ct)
    {
        var chrono = Stopwatch.StartNew();
        var jour = horloge.GetUtcNow().UtcDateTime.Date;
        var urls = RelectureRedemarrage.Urls(gestes);
        var premiere = await LireAsync(bac, urls, "", ct);
        var seconde = await LireAsync(bac, urls, "sleep 1.5; ", ct);
        await bac.ExecuterAsync(
            $"if [ -f /tmp/usine-app.pid ]; then g=$(cat /tmp/usine-app.pid); kill -TERM -- -$g 2>/dev/null; for i in $(seq 1 {(AttenteArretSecondes * 10).ToString(CultureInfo.InvariantCulture)}); do kill -0 -- -$g 2>/dev/null || break; sleep 0.1; done; kill -KILL -- -$g 2>/dev/null; rm -f /tmp/usine-app.pid; fi; true",
            TimeSpan.FromSeconds(AttenteArretSecondes + 20),
            ct: ct);
        var relance = await bac.ExecuterAsync(
            $"if curl -s -o /dev/null --max-time 2 http://localhost:{Port}/; then echo \"Le port {Port} est encore occupé après l'arrêt de l'application.\"; exit 4; fi; "
            + $"PORT={Port} setsid nohup bash -c 'cd {CopieApplication} && exec ./usine-start.sh' >> /tmp/usine-app.log 2>&1 < /dev/null & echo $! > /tmp/usine-app.pid",
            TimeSpan.FromMinutes(1),
            CopieApplication,
            ct: ct);
        if (!relance.Reussi)
        {
            journaliser?.Invoke($"Relecture après redémarrage ({motif}) abandonnée : la relance de l'application n'a pas pu être faite ({ExecuteurProcessus.Tronquer(relance.SortieComplete, 300).Trim().ReplaceLineEndings(" ")}).");
            return;
        }

        var demarrage = await AttendreReponseAsync(bac, ct);
        if (demarrage.Code != 0)
        {
            var fin = demarrage.Sortie.Split('\n', 2) is [_, var sortie] ? sortie.Trim().ReplaceLineEndings(" ") : "";
            AjouterSignalRedemarrage(
                resultat,
                "redemarrage|demarrage",
                RelectureRedemarrage.Message(
                    "L'application ne redémarre pas sur ses propres données : après son arrêt puis la relance de usine-start.sh dans le même dossier, sans reconstruction, ",
                    $"{(demarrage.Code == 2 ? "elle s'arrête au démarrage" : $"elle ne répond pas sur le port {Port} après {Limites.DelaiDemarrageSecondes.ToString(CultureInfo.InvariantCulture)} s")}{(fin.Length > 0 ? $" (fin de sa sortie : « {(fin.Length > 80 ? $"…{fin[^79..]}" : fin)} »)" : "")}",
                    ". Le démarrage doit réussir sur les données déjà enregistrées, sans les effacer."));
            journaliser?.Invoke($"Relecture après redémarrage ({motif}) : l'application ne redémarre pas, en {chrono.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s ; 1 écart(s).");
            return;
        }

        var pertes = RelectureRedemarrage.Pertes(urls, premiere, seconde, await LireAsync(bac, urls, "", ct));
        for (var essai = 0; essai < 3 && pertes.Count > 0; essai++)
        {
            pertes = RelectureRedemarrage.Pertes(urls, premiere, seconde, await LireAsync(bac, urls, "sleep 2; ", ct));
        }

        var bilan = $"Relecture après redémarrage ({motif}) : {RelectureRedemarrage.Stables(urls, premiere, seconde).ToString(CultureInfo.InvariantCulture)} URL relue(s) avant et après, en {chrono.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
        if (pertes.Count > 0 && horloge.GetUtcNow().UtcDateTime.Date != jour)
        {
            journaliser?.Invoke($"{bilan} ; écart ignoré, la date a changé entre les relevés.");
            return;
        }

        if (pertes.Count > 0)
        {
            AjouterSignalRedemarrage(
                resultat,
                "redemarrage|donnees",
                RelectureRedemarrage.Message(
                    "Données perdues au redémarrage : après l'arrêt de l'application puis la relance de usine-start.sh dans le même dossier, sans reconstruction, ",
                    pertes[0],
                    ". Les données enregistrées doivent survivre au redémarrage : ni usine-start.sh ni le démarrage ne doivent les effacer ou les réinitialiser."));
        }

        journaliser?.Invoke($"{bilan} ; {(pertes.Count > 0 ? 1 : 0).ToString(CultureInfo.InvariantCulture)} écart(s).");
    }

    private static void AjouterSignalRedemarrage(ResultatOracle resultat, string cle, string message)
    {
        resultat.Signaux.RemoveAll(s => string.Equals(s.Cle, cle, StringComparison.Ordinal));
        resultat.Signaux.Insert(0, new SignalOracle { Cle = cle, Message = message });
    }

    private async Task<IReadOnlyDictionary<string, LectureHttp>> LireAsync(IBacASable bac, IReadOnlyList<string> urls, string attente, CancellationToken ct)
    {
        var liste = string.Join(' ', urls.Select(u => Convert.ToBase64String(Encoding.UTF8.GetBytes(u))));
        var lecture = await bac.ExecuterAsync(
            $"{attente}for u in {liste}; do e=$(curl -s -o {LectureRelue} -w '%{{http_code}}' --max-time 5 \"http://localhost:{Port}$(printf %s \"$u\" | base64 -d)\" || true); "
            + $"printf '{RelectureRedemarrage.MarqueurLecture} %s %s %s\\n' \"$u\" \"${{e:-000}}\" \"$(head -c {(RelectureRedemarrage.OctetsMax + 1).ToString(CultureInfo.InvariantCulture)} {LectureRelue} 2>/dev/null | base64 -w0)\"; rm -f {LectureRelue}; done",
            TimeSpan.FromMinutes(3),
            ct: ct);
        return RelectureRedemarrage.Lectures(lecture.Sortie);
    }

    private static string EchapperRegex(string texte) => string.Concat(texte.Select(c => CaracteresRegex.Contains(c, StringComparison.Ordinal) ? $"\\{c}" : c.ToString()));

    private async Task<IReadOnlyList<string>> ReprendreDonneesDepartAsync(IBacASable bac, Action<string>? journaliser, CancellationToken ct)
    {
        var etat = (await bac.ExecuterAsync(EtatDepart, TimeSpan.FromSeconds(30), ct: ct)).Sortie
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (etat is ["REPRISE", .. var reprises])
        {
            return reprises;
        }

        if (etat is not [var ligne] || !ligne.StartsWith("DEPART ", StringComparison.Ordinal))
        {
            return [];
        }

        var commit = ligne["DEPART ".Length..];
        var chrono = Stopwatch.StartNew();
        var construction = await bac.ExecuterAsync(
            $"rm -rf {CopieDepart} {MaisonDepart} {MarqueurDepart} && mkdir -p {MaisonDepart}/.npm-global && git init -q {CopieDepart} "
            + $"&& git -C {CopieDepart} fetch -q /workspace {BacASableDocker.RefDepart} && git -C {CopieDepart} checkout -q FETCH_HEAD "
            + "|| { echo \"son commit n'a pas pu être extrait du dépôt\"; exit 6; }; "
            + $"cd {CopieDepart} && if [ ! -f usine-build.sh ] || [ ! -f usine-start.sh ]; then echo \"elle n'a pas de usine-build.sh ou de usine-start.sh\"; exit 3; fi; "
            + $"chmod +x usine-build.sh usine-start.sh 2>/dev/null; {EnvironnementDepart}./usine-build.sh && touch {MarqueurDepart}",
            TimeSpan.FromSeconds(Limites.DelaiBuildSecondes),
            "/tmp",
            ct: ct);
        var echec = construction.Code switch
        {
            0 => null,
            3 or 6 => construction.Sortie.Trim(),
            _ => Resume("sa construction (usine-build.sh) a échoué", construction),
        };
        if (echec is null)
        {
            var demarrage = await DemarrerAsync(bac, CopieDepart, EnvironnementDepart, null, ct);
            await ArreterApplicationAsync(bac, CancellationToken.None);
            echec = demarrage.Code == 0 ? null : Resume("elle ne démarre pas", demarrage);
        }

        if (echec is null)
        {
            var archive = await bac.ExecuterAsync(
                $"cd {CopieApplication} && git ls-files -z -o -- . {ExclusionsDonnees} | while IFS= read -r -d '' f; do if [ \"$f\" -nt {MarqueurDepart} ]; then printf '%s\\0' \"$f\"; fi; done > {DonneesDepart}.liste "
                + $"&& tar --null -T {DonneesDepart}.liste -cf {DonneesDepart} || {{ rm -f {DonneesDepart} {DonneesDepart}.liste; exit 7; }}; rm -f {DonneesDepart}.liste; tar -tf {DonneesDepart}",
                TimeSpan.FromMinutes(5),
                ct: ct);
            if (archive.Reussi)
            {
                var fichiers = archive.Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                await bac.ExecuterAsync($"rm -rf {CopieDepart} {MaisonDepart}", TimeSpan.FromMinutes(2), ct: ct);
                journaliser?.Invoke($"Mise à jour en place : version de départ {Empreinte.Abreger(commit)} construite, démarrée puis arrêtée en {chrono.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s ; {(fichiers.Length == 0 ? "elle n'a créé aucune donnée en démarrant (les fichiers créés par sa construction ne sont pas repris), la nouvelle version démarre sur des données neuves" : $"données reprises par la nouvelle version : {ExecuteurProcessus.Tronquer(string.Join(", ", fichiers), 500)}")}.");
                return fichiers;
            }

            echec = Resume("ses données n'ont pas pu être archivées", archive);
        }

        await bac.ExecuterAsync($"rm -rf {CopieDepart} {MaisonDepart} {DonneesDepart}; touch {RepriseImpossible}", TimeSpan.FromMinutes(2), ct: ct);
        journaliser?.Invoke($"Mise à jour en place non vérifiée, la nouvelle version démarre sur des données neuves : la version de départ du projet ({Empreinte.Abreger(commit)}) n'a pas pu servir ({ExecuteurProcessus.Tronquer(echec, 600).Trim().ReplaceLineEndings(" ")}).");
        return [];
    }

    private static string AvecReprise(IReadOnlyList<string> fichiers, string journal)
    {
        if (fichiers.Count == 0)
        {
            return journal;
        }

        var note = $"Mise à jour en place : cette version a été démarrée sur les données créées par la version précédente du projet (la version de départ, construite, démarrée puis arrêtée juste avant ; fichiers repris : {ExecuteurProcessus.Tronquer(string.Join(", ", fichiers), 500)}), comme chez un utilisateur qui installe la nouvelle version en gardant ses données. Au démarrage, l'application doit mettre à niveau ces données sans les perdre (par exemple ajouter la colonne ou le champ qui manque).";
        return journal.Length > 0 ? $"{note}\n\n{journal}" : note;
    }

    private Task<ResultatCommande> ConstruireAsync(IBacASable bac, CancellationToken ct) => bac.ExecuterAsync(
        "test -f usine-build.sh || { echo 'usine-build.sh introuvable à la racine du projet.'; exit 3; }; chmod +x usine-build.sh 2>/dev/null; "
        + $"./usine-build.sh && touch {MarqueurConstruction}",
        TimeSpan.FromSeconds(Limites.DelaiBuildSecondes),
        ct: ct);

    private string EchecConstruction(ResultatCommande construction) => Resume(
        construction.DelaiDepasse
            ? $"La construction (usine-build.sh) a dépassé le délai de {Limites.DelaiBuildSecondes} s : elle ne doit ni démarrer de serveur ni attendre, seul usine-start.sh démarre l'application"
            : "La construction (usine-build.sh) a échoué",
        construction);

    internal static async Task ExigerBacDisponibleAsync(IBacASable bac, CancellationToken ct)
    {
        var sonde = await bac.ExecuterAsync("true", TimeSpan.FromSeconds(20), ct: ct);
        if (!sonde.Reussi)
        {
            throw new InvalidOperationException($"Le bac à sable ne répond plus : {sonde.SortieComplete}");
        }
    }

    private static ResultatOracle Echec(string etape, string journal, bool empreinteValide = true) => new()
    {
        Succes = false,
        EtapeEchec = etape,
        Journal = journal,
        EmpreinteValide = empreinteValide,
    };

    private static string Resume(string titre, ResultatCommande commande) =>
        $"{titre} (code {commande.Code.ToString(CultureInfo.InvariantCulture)}{(commande.DelaiDepasse ? ", délai dépassé" : "")}).\n"
        + ExecuteurProcessus.Tronquer(commande.SortieComplete, 6000);
}
