using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using Usine.Api;
using Usine.Core;
using Usine.Core.BacASable;
using Usine.Core.Configuration;
using Usine.Core.Discussion;
using Usine.Core.Etat;
using Usine.Core.Modeles;
using Usine.Core.Orchestration;
using Usine.Core.Services;
using Usine.Mcp;

namespace Usine.Cli;

public static class Commandes
{
    public const string Aide = """
        usine — usine agentique locale (POC)

        Utilisation :
          usine lancer "<demande>" [--projet <nom> | --depot <chemin>] [--auto] [--titre <titre>] [--suivre]
          usine statut <id> [--detail]
          usine suivre <id>
          usine repondre <id> "<réponses>"
          usine valider <id> <besoin|backlog|tests|livraison> [--refuser "<commentaire>"] [--commentaire "<texte>"]
          usine liste
          usine projets             liste les projets de l'usine, avec les demandes qui les ont créés et fait évoluer
          usine essayer <id> | --projet <nom> [--port <port>]   lance dans Docker l'application livrée d'une demande, ou un projet de l'usine (Ctrl+C pour arrêter)
          usine annuler <id>
          usine reprendre <id>      reprend une demande échouée à l'étape de son échec
          usine reevaluer <id>      réévalue la livraison d'une demande terminée dans une nouvelle demande (mode auto, à partir de la vérification, livrable non publié dans les projets)
          usine reevaluer <id> --nouveaux-tests   même réévaluation, mais le PO est relancé pour les seules notions, le QA amont réécrit les tests d'acceptation sans voir le code du livrable, puis l'oracle vérifie directement le livrable importé
          usine rapport <id>
          usine image [--forcer]
          usine config [chemin|donnees|afficher]
          usine diagnostic
          usine mcp [--auto-apres-backlog]   serveur MCP (stdio) pour un assistant IA ; --auto-apres-backlog : chaque demande lancée est en mode auto après le backlog
          usine mcp-config          affiche comment brancher l'usine dans Claude Code, Cursor, VS Code
          usine chat [--modele <fournisseur:modèle>] [--auto-apres-backlog]   discute avec l'usine en langage naturel (outils MCP, confirmation avant valider_etape, annuler_demande et reprendre_demande) ; --auto-apres-backlog : chaque demande lancée est en mode auto après le backlog
          usine interface [--port <port>] [--sans-navigateur]   démarre l'interface web de l'usine (http://localhost:5180 par défaut, Ctrl+C pour arrêter) et l'ouvre dans le navigateur ; --sans-navigateur : ne l'ouvre pas
          usine worker <id>         (interne) traite une demande en arrière-plan
          usine executer <id>       traite une demande au premier plan, avec le journal

        Projets : une application nouvelle terminée avec succès devient le dossier <nom> du dossier des projets (réglage « projets » de la configuration, par défaut <racine>/projets-generes, essais compris ; un dépôt git, branche main). --projet <nom> fait évoluer ce projet : son dossier est mis à jour en avance rapide quand la demande se termine avec succès, une seule demande à la fois par projet. --depot désigne un dépôt git extérieur, jamais modifié : le résultat reste dans le livrable.

        Un commentaire d'approbation (--commentaire) n'est lu par aucun rôle : il reste une note consignée dans la chronologie de la demande. Pour qu'une précision soit prise en compte, refuse l'étape (--refuser).

        Configuration : appsettings.json à la racine du dépôt, puis appsettings.Local.json (facultatif, non versionné), les User Secrets, les paramètres de l'interface et les variables d'environnement, chacun primant sur le précédent ; données : dossier « donnees » de la configuration (par défaut <racine>/donnees). Essais : USINE_CONFIG désigne un fichier qui remplace les deux fichiers du dépôt, USINE_HOME le dossier des données. Clés API : User Secrets (dotnet user-secrets set GEMINI_API_KEY <clé> --project src/Usine.Cli) ou variables d'environnement.
        """;

    public static async Task<int> ExecuterAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "aide" or "--help" or "-h" or "help")
        {
            Console.WriteLine(Aide);
            return 0;
        }

        try
        {
            var options = Arguments.Analyser(args.Skip(1).ToArray());
            options.LimiterPositionnels(args[0] switch
            {
                "repondre" or "valider" => 2,
                "lancer" or "statut" or "suivre" or "annuler" or "reprendre" or "reevaluer" or "rapport" or "config" or "worker" or "executer" or "essayer" => 1,
                "liste" or "projets" or "interface" or "image" or "diagnostic" or "mcp" or "mcp-config" or "chat" => 0,
                _ => int.MaxValue,
            });
            options.LimiterOptions(args[0], OptionsPermises(args[0]));
            return args[0] switch
            {
                "lancer" => await LancerAsync(options, ct),
                "statut" => Statut(options),
                "suivre" => await SuivreAsync(options.Positionnel(0, "id"), ct),
                "repondre" => Repondre(options),
                "valider" => await ValiderAsync(options, ct),
                "liste" => Liste(),
                "projets" => ListeProjets(),
                "essayer" => await EssayerAsync(options, ct),
                "annuler" => await AnnulerAsync(options, ct),
                "reprendre" => await ReprendreAsync(options, ct),
                "reevaluer" => await ReevaluerAsync(options, ct),
                "rapport" => Rapport(options),
                "image" => await ImageAsync(options, ct),
                "config" => Config(options),
                "diagnostic" => await DiagnosticAsync(ct),
                "mcp" => await McpAsync(options, ct),
                "interface" => await InterfaceAsync(options, new OuvreurNavigateur(), ct),
                "mcp-config" => ConfigurationMcp(),
                "chat" => await ChatAsync(options, ct),
                "worker" => await WorkerAsync(options.Positionnel(0, "id"), silencieux: true, ct),
                "executer" => await WorkerAsync(options.Positionnel(0, "id"), silencieux: false, ct),
                _ => Erreur($"Commande inconnue : {args[0]}\n\n{Aide}"),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Console.Error.WriteLine("Interrompu.");
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or DemandeIntrouvableException or FormatException or IOException
            or UnauthorizedAccessException or System.Text.Json.JsonException or McpException)
        {
            return Erreur(ex.Message);
        }
    }

    private static async Task<int> LancerAsync(Arguments options, CancellationToken ct)
    {
        var usine = ContexteUsine.Creer();
        var demande = usine.Service.Lancer(options.Positionnel(0, "demande"), options.Valeur("depot"), options.Drapeau("auto"), options.Valeur("titre"), options.Valeur("projet"));
        Console.WriteLine($"Demande lancée : {demande.Id}");
        return options.Drapeau("suivre") ? await SuivreAsync(demande.Id, ct) : 0;
    }

    private static int Statut(Arguments options)
         {
             var usine = ContexteUsine.Creer();
             var demande = usine.Service.Avancement(options.Positionnel(0, "id"));
             Console.WriteLine(FormateurDemande.Resume(demande, usine.Depot, options.Drapeau("detail")));
             return 0;
         }

    private static async Task<int> SuivreAsync(string id, CancellationToken ct)
    {
        var usine = ContexteUsine.Creer();
        long dernierVu = 0;
        while (true)
        {
            var demande = usine.Service.Avancement(id);
            foreach (var evenement in demande.Evenements.Where(e => e.Numero > dernierVu))
            {
                Console.WriteLine(FormateurDemande.LigneEvenement(evenement));
                dernierVu = evenement.Numero;
            }

            if (demande.Statut != StatutDemande.EnCours)
            {
                Console.WriteLine();
                Console.WriteLine(FormateurDemande.Resume(demande, usine.Depot, detaille: false));
                return demande.Statut is StatutDemande.Echouee ? 1 : 0;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    private static int Repondre(Arguments options)
    {
        var usine = ContexteUsine.Creer();
        var demande = usine.Service.RepondreQuestions(options.Positionnel(0, "id"), options.Positionnel(1, "réponses"));
        Console.WriteLine(FormateurDemande.Resume(demande, usine.Depot, detaille: false));
        return 0;
    }

    private static async Task<int> ValiderAsync(Arguments options, CancellationToken ct)
    {
        var usine = ContexteUsine.Creer();
        var demande = await usine.Service.ValiderEtapeAsync(
            options.Positionnel(0, "id"),
            options.Positionnel(1, "étape"),
            approuver: !options.Drapeau("refuser"),
            commentaire: options.Valeur("refuser") ?? options.Valeur("commentaire"),
            ct);
        Console.WriteLine(FormateurDemande.Resume(demande, usine.Depot, detaille: false));
        return 0;
    }

    private static int Liste()
    {
        var usine = ContexteUsine.Creer();
        var demandes = usine.Service.Lister();
        if (demandes.Count == 0)
        {
            Console.WriteLine("Aucune demande.");
            return 0;
        }

        foreach (var demande in demandes)
        {
            Console.WriteLine(FormateurDemande.Ligne(demande));
        }

        return 0;
    }

    private static int ListeProjets()
    {
        Console.WriteLine(FormateurDemande.ListeProjets(ContexteUsine.Creer().Service.ListerProjets()));
        return 0;
    }

    private static async Task<int> EssayerAsync(Arguments options, CancellationToken ct)
    {
        var configuration = ChargeurConfig.Resoudre();
        var bac = configuration.Config.BacASable;
        var depot = new DepotDemandes(configuration.RepertoireDonnees, configuration.RepertoireProjets);
        var (source, volume, libelle) = EssaiApplication.CibleEssai(depot, options.Valeur("projet"), options.PositionnelOuDefaut(0, ""));
        var port = EssaiApplication.PortEssai(options.Valeur("port"), bac.Port);
        if (!await ImageBacASable.ExisteAsync(bac, ct))
        {
            return Erreur($"L'image {bac.Image} est absente : construis-la d'abord avec « usine image ».");
        }

        if (!EssaiApplication.PortLibre(port))
        {
            return Erreur($"Le port {port} est déjà pris sur ce poste : relance avec un autre port (--port 3000, ou PORT=3000 avec make essayer).");
        }

        Console.WriteLine($"{libelle} : http://localhost:{port} dès que l'application a démarré. Ctrl+C pour arrêter ; ses données restent dans le volume Docker {volume} (docker volume rm {volume} pour les effacer).");
        var demarrage = new ProcessStartInfo("docker") { UseShellExecute = false };
        foreach (var argument in EssaiApplication.ArgumentsEssai(source, volume, bac.Image, port, bac.Port, interactif: !Console.IsInputRedirected))
        {
            demarrage.ArgumentList.Add(argument);
        }

        using var docker = Process.Start(demarrage) ?? throw new InvalidOperationException("docker n'a pas pu être lancé.");
        await docker.WaitForExitAsync(CancellationToken.None);
        return ct.IsCancellationRequested ? 0 : docker.ExitCode;
    }

    private static async Task<int> AnnulerAsync(Arguments options, CancellationToken ct)
    {
        var usine = ContexteUsine.Creer();
        var demande = await usine.Service.AnnulerAsync(options.Positionnel(0, "id"), ct);
        Console.WriteLine(FormateurDemande.Resume(demande, usine.Depot, detaille: false));
        return 0;
    }

    private static async Task<int> ReprendreAsync(Arguments options, CancellationToken ct)
    {
        var usine = ContexteUsine.Creer();
        var demande = await usine.Service.ReprendreAsync(options.Positionnel(0, "id"), ct);
        Console.WriteLine(FormateurDemande.Resume(demande, usine.Depot, detaille: false));
        return 0;
    }

    private static async Task<int> ReevaluerAsync(Arguments options, CancellationToken ct)
    {
        var usine = ContexteUsine.Creer();
        var demande = await ReevaluerAsync(usine.Service, options, ct);
        Console.WriteLine($"Réévaluation lancée : {demande.Id}");
        return 0;
    }

    public static Task<Demande> ReevaluerAsync(ServiceDemandes service, Arguments options, CancellationToken ct) =>
        service.ReevaluerAsync(options.Positionnel(0, "id"), options.Drapeau("nouveaux-tests"), ct);

    private static int Rapport(Arguments options)
    {
        var usine = ContexteUsine.Creer();
        var demande = usine.Service.Charger(options.Positionnel(0, "id"));
        if (demande.Livrable is null || !File.Exists(demande.Livrable.Rapport))
        {
            return Erreur("Pas encore de rapport pour cette demande.");
        }

        if (FormateurDemande.AvertissementEchecApresLivraison(demande, usine.Depot) is { } avertissement)
        {
            Console.Error.WriteLine(avertissement);
        }

        Console.WriteLine(File.ReadAllText(demande.Livrable.Rapport));
        return 0;
    }

    private static async Task<int> ImageAsync(Arguments options, CancellationToken ct)
    {
        var config = ChargeurConfig.Resoudre().Config;
        if (!options.Drapeau("forcer") && await ImageBacASable.ExisteAsync(config.BacASable, ct))
        {
            Console.WriteLine($"L'image {config.BacASable.Image} existe déjà (--forcer pour la reconstruire).");
            if (ImageBacASable.Avertissement(config.BacASable) is { } avertissement)
            {
                Console.WriteLine(avertissement);
            }

            return 0;
        }

        var resultat = await ImageBacASable.ConstruireAsync(config.BacASable, Console.WriteLine, ct);
        Console.WriteLine(ExecuteurProcessus.Tronquer(resultat.SortieComplete, 4000));
        return resultat.Reussi ? 0 : Erreur("La construction de l'image a échoué.");
    }

    private static int Config(Arguments options)
    {
        var sousCommande = options.PositionnelOuDefaut(0, "afficher");
        switch (sousCommande)
        {
            case "chemin":
                foreach (var fichier in ChargeurConfig.Fichiers())
                {
                    Console.WriteLine(fichier);
                }

                return 0;
            case "donnees":
                Console.WriteLine(ChargeurConfig.RepertoireDonnees());
                return 0;
            case "afficher":
                var configuration = ChargeurConfig.Resoudre();
                foreach (var ligne in Emplacements(configuration))
                {
                    Console.WriteLine(ligne);
                }

                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(configuration.Config, Json.Options));
                return 0;
            default:
                return Erreur($"Sous-commande inconnue : {sousCommande}. Sous-commandes possibles : chemin, donnees, afficher.");
        }
    }

    public static string MentionTarif(ConfigUsine config, ReferenceModele reference) =>
        config.TarifPour(reference) is null ? $" (tarif inconnu : coût non calculé{(config.Limites.PlafondCoutDemande is null ? "" : ", plafond de coût inopérant")})" : "";

    public static IReadOnlyList<string> Emplacements(ConfigurationUsine configuration) =>
    [
        $"Configuration : {string.Join(" puis ", configuration.Fichiers)}, puis User Secrets{(configuration.AvecParametresInterface ? $", {ChargeurConfig.OrigineParametresInterface}" : "")} et variables d'environnement",
        $"Données       : {configuration.RepertoireDonnees}",
        .. configuration.Config.Fournisseurs
            .Where(f => !string.IsNullOrWhiteSpace(f.Value.VariableCleApi))
            .Select(f => $"{$"Clé {f.Key}",-13} : {f.Value.VariableCleApi} ({configuration.Cles.Trouver(f.Value.VariableCleApi!)?.Origine ?? "absente"})"),
    ];

    private static async Task<int> DiagnosticAsync(CancellationToken ct)
    {
        var ok = true;
        var configuration = ChargeurConfig.Resoudre();
        var config = configuration.Config;
        foreach (var ligne in Emplacements(configuration))
        {
            Console.WriteLine(ligne);
        }

        foreach (var erreur in config.Valider())
        {
            ok = false;
            Console.WriteLine($"  ✗ {erreur}");
        }

        var docker = await BacASableDocker.DockerAsync(["version", "--format", "{{.Server.Version}}"], ct, verifier: false);
        ok &= Afficher(docker.Reussi, $"Docker        : {(docker.Reussi ? docker.Sortie.Trim() : "indisponible")}");
        var image = docker.Reussi && await ImageBacASable.ExisteAsync(config.BacASable, ct);
        ok &= Afficher(image, $"Image         : {config.BacASable.Image}{(image ? "" : " absente (usine image)")}");
        if (image && ImageBacASable.Avertissement(config.BacASable) is { } avertissement)
        {
            Console.WriteLine($"  {avertissement}");
        }

        foreach (var role in Enum.GetValues<RoleAgent>())
        {
            IReadOnlyList<ReferenceModele> references;
            try
            {
                references = config.ModelesPour(role);
            }
            catch (FormatException)
            {
                ok = false;
                continue;
            }

            for (var i = 0; i < references.Count; i++)
            {
                var reference = references[i];
                var disponible = configuration.Utilisable(reference);
                var tarif = MentionTarif(config, reference);
                var libelle = i == 0 ? RolesAgent.Libelle(role) : "  secours";
                var effort = config.EffortAppliquePour(role, reference) is { } valeur ? $" (effort de raisonnement : {valeur})" : "";
                ok &= Afficher(disponible, $"{libelle,-13} : {reference}{effort}{(disponible ? "" : " — clé API absente")}{tarif}");
            }
        }

        return ok ? 0 : 1;
    }

    private static int ConfigurationMcp()
    {
        var configuration = ChargeurConfig.Resoudre();
        var (executable, prefixe) = LanceurWorkerProcessus.CommandeUsine();
        var arguments = prefixe.Append("mcp").ToList();
        var json = System.Text.Json.JsonSerializer.Serialize(executable);
        var argumentsJson = System.Text.Json.JsonSerializer.Serialize(arguments);
        var variables = VariablesMcp();
        var optionsClaude = string.Concat(variables.Select(v => $" -e {v.Key}=\"{v.Value}\""));
        var env = variables.Count == 0 ? "" : $", \"env\": {System.Text.Json.JsonSerializer.Serialize(variables)}";
        Console.WriteLine("Claude Code :");
        Console.WriteLine($"  claude mcp add usine{optionsClaude} -- \"{executable}\" {string.Join(' ', arguments.Select(a => $"\"{a}\""))}");
        Console.WriteLine();
        Console.WriteLine("Cursor (.cursor/mcp.json) ou Claude Desktop (claude_desktop_config.json) :");
        Console.WriteLine($"  {{ \"mcpServers\": {{ \"usine\": {{ \"command\": {json}, \"args\": {argumentsJson}{env} }} }} }}");
        Console.WriteLine();
        Console.WriteLine("VS Code (.vscode/mcp.json) :");
        Console.WriteLine($"  {{ \"servers\": {{ \"usine\": {{ \"type\": \"stdio\", \"command\": {json}, \"args\": {argumentsJson}{env} }} }} }}");
        Console.WriteLine();
        foreach (var ligne in Emplacements(configuration))
        {
            Console.WriteLine(ligne);
        }

        Console.WriteLine("Les clés API sont lues dans les User Secrets du projet, puis dans les paramètres de l'interface, puis dans les variables d'environnement de l'assistant, qui priment (voir « usine diagnostic »).");
        return 0;
    }

    public static Dictionary<string, string> VariablesMcp() =>
        new[] { ChargeurConfig.VariableRepertoire, ChargeurConfig.VariableFichier, DepotParametresInterface.VariableTrousseau }
            .Where(nom => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(nom)))
            .ToDictionary(nom => nom, ValeurMcp, StringComparer.Ordinal);

    private static string ValeurMcp(string nom) => nom switch
    {
        ChargeurConfig.VariableRepertoire => ChargeurConfig.RepertoireDonnees(),
        ChargeurConfig.VariableFichier => ChargeurConfig.FichierImpose()!,
        _ => Path.GetFullPath(Environment.GetEnvironmentVariable(nom)!),
    };

    private static async Task<int> McpAsync(Arguments options, CancellationToken ct)
    {
        await ServeurMcp.ExecuterAsync(ContexteUsine.Creer(), ct, options.Drapeau("auto-apres-backlog"));
        return 0;
    }

    public static Task<int> InterfaceAsync(Arguments options, IOuvreurNavigateur navigateur, CancellationToken ct) =>
        InterfaceAsync(options, navigateur, ChargeurConfig.ChercherRacine(AppContext.BaseDirectory), ct);

    public static async Task<int> InterfaceAsync(Arguments options, IOuvreurNavigateur navigateur, string? racine, CancellationToken ct)
    {
        var port = EssaiApplication.PortEssai(options.Valeur("port"), ServeurApi.PortParDefaut);
        if (!EssaiApplication.PortLibreSurCePoste(port))
        {
            return Erreur($"Le port {port} est déjà pris sur ce poste : relance avec un autre port (--port 5181, ou PORT=5181 avec make interface).");
        }

        var jeton = ServeurApi.TirerJeton();
        var adresse = $"http://localhost:{port}/?jeton={jeton}";
        var ecran = racine is null ? null : Path.Combine(racine, "front", "dist", "front", "browser");
        await using var app = ServeurApi.Construire(ContexteUsine.Creer(), port, jeton, ecran);
        await app.StartAsync(ct);
        Console.WriteLine($"Interface de l'usine : {adresse} (Ctrl+C pour arrêter).");
        if (!options.Drapeau("sans-navigateur") && !navigateur.Ouvrir(adresse))
        {
            Console.WriteLine("Ouvre cette adresse dans ton navigateur.");
        }

        await app.WaitForShutdownAsync(ct);
        return 0;
    }

    private static async Task<int> ChatAsync(Arguments options, CancellationToken ct)
    {
        var configuration = ChargeurConfig.Resoudre();
        var config = configuration.Config;
        var reference = options.Valeur("modele") is { } modele ? AnalyserOptionModele(modele) : config.ModelePour(RoleAgent.Po);
        using var client = ClientDiscussion(configuration, reference);
        var console = ConsoleDiscussion.PourLaConsole();
        var autoApresBacklog = options.Drapeau("auto-apres-backlog");
        await using var mcp = await ClientUsine.ConnecterAsync(configuration, console.EcrireErreur, ct, autoApresBacklog);
        var outils = await ClientUsine.OutilsAsync(mcp, ct);
        console.Ecrire($"Discussion avec l'usine (modèle {reference}, {outils.Count} outils). Tape /quitter (ou /exit) pour terminer ; pendant une revue, /annuler (ou /cancel) annule la demande, /passer (ou /skip) met la revue de côté et /revue (ou /review) la reprend.");
        if (autoApresBacklog)
        {
            console.Ecrire("Mode auto après le backlog : tu réponds aux questions du PO, tu valides le besoin qu'il a compris puis son backlog, puis l'usine écrit les tests, construit et livre seule ; le projet n'est publié que si la livraison réussit.");
        }

        console.Ecrire($"Configuration : {string.Join(" puis ", configuration.Fichiers)} ; données : {configuration.RepertoireDonnees}");
        var consigne = ConsigneDuChat(mcp.ServerInstructions);
        var informations = new InformationsUsine();
        var depot = new DepotDemandes(configuration.RepertoireDonnees, configuration.RepertoireProjets);
        var revues = new RevuesEnAttente();
        var fil = new FilEnDirect(depot, config.Limites, console.Ecrire, informations, revues: revues);
        fil.Demarrer();
        using var arretFil = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var suivi = fil.ExecuterAsync(arretFil.Token);
        try
        {
            var revue = new RevueGuidee(depot, revues, outils, console, informations);
            await new BoucleDiscussion(client, outils, console, consigne, config.MaxTokensSortiePour(reference), informations, revue).ExecuterAsync(ct);
        }
        finally
        {
            await arretFil.CancelAsync();
            await suivi;
        }

        return 0;
    }

    public static string ConsigneDuChat(string? instructionsServeur) =>
        Ressources.Lire("prompts/chat.md") + (string.IsNullOrWhiteSpace(instructionsServeur) ? "" : $"\n\n{instructionsServeur}");

    public static IChatClient ClientDiscussion(ConfigurationUsine configuration, ReferenceModele reference) =>
        new FabriqueClientsChat(configuration.Config, configuration.Cles).Creer(reference);

    public static IReadOnlyList<string> OptionsPermises(string commande) => commande switch
    {
        "lancer" => ["projet", "depot", "auto", "titre", "suivre"],
        "statut" => ["detail"],
        "valider" => ["refuser", "commentaire"],
        "image" => ["forcer"],
        "chat" => ["modele", "auto-apres-backlog"],
        "mcp" => ["auto-apres-backlog"],
        "interface" => ["port", "sans-navigateur"],
        "reevaluer" => ["nouveaux-tests"],
        "essayer" => ["projet", "port"],
        "suivre" or "repondre" or "liste" or "projets" or "annuler" or "reprendre" or "rapport" or "config" or "diagnostic" or "mcp-config" or "worker" or "executer" => [],
        _ => Arguments.OptionsConnues,
    };

    public static ReferenceModele AnalyserOptionModele(string valeur) =>
        valeur.Contains(',')
            ? throw new ArgumentException($"L'option --modele attend une seule référence « fournisseur:modèle », sans modèle de secours : « {valeur} ».")
            : ReferenceModele.Analyser(valeur);

    private static async Task<int> WorkerAsync(string id, bool silencieux, CancellationToken ct)
    {
        var depot = new DepotDemandes(ChargeurConfig.RepertoireDonnees(), ChargeurConfig.RepertoireProjets());
        if (!depot.Existe(id))
        {
            return Erreur($"Aucune demande avec l'identifiant « {id} ».");
        }

        using var verrou = VerrouWorker.Acquerir(depot.CheminVerrouWorker(id));
        if (verrou is null)
        {
            if (!silencieux)
            {
                Console.Error.WriteLine("Un worker traite déjà cette demande.");
            }

            return 0;
        }

        File.Delete(depot.CheminLancementWorker(id));
        Task? affichage = null;
        using var fin = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!silencieux)
        {
            affichage = AfficherJournalAsync(depot.CheminJournal(id), fin.Token);
        }

        try
        {
            await ContexteUsine.Creer().Orchestrateur.ExecuterAsync(id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SignalerEchecWorker(depot, id, ex);
        }
        finally
        {
            await fin.CancelAsync();
            if (affichage is not null)
            {
                await affichage;
            }
        }

        var demande = depot.Charger(id);
        if (!silencieux)
        {
            Console.WriteLine();
            Console.WriteLine(FormateurDemande.Resume(demande, depot, detaille: false));
        }

        return demande.Statut == StatutDemande.Echouee ? 1 : 0;
    }

    private static void SignalerEchecWorker(DepotDemandes depot, string id, Exception ex)
    {
        depot.Journaliser(id, ex.ToString());
        var demande = depot.Charger(id);
        if (demande.EstTerminale)
        {
            return;
        }

        demande.Statut = StatutDemande.Echouee;
        demande.Erreur = ex.Message;
        depot.AjouterEvenement(demande, Orchestrateur.ChefDeProjet, $"Le traitement s'est arrêté sur une erreur : {ex.Message}");
        depot.Enregistrer(demande);
    }

    private static async Task AfficherJournalAsync(string chemin, CancellationToken ct)
    {
        long position = File.Exists(chemin) ? new FileInfo(chemin).Length : 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException)
            {
            }

            if (!File.Exists(chemin))
            {
                continue;
            }

            using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (flux.Length <= position)
            {
                continue;
            }

            flux.Seek(position, SeekOrigin.Begin);
            using var lecteur = new StreamReader(flux);
            Console.Write(await lecteur.ReadToEndAsync(CancellationToken.None));
            position = flux.Position;
        }
    }

    private static bool Afficher(bool ok, string message)
    {
        Console.WriteLine($"{(ok ? "✓" : "✗")} {message}");
        return ok;
    }

    private static int Erreur(string message)
    {
        Console.Error.WriteLine($"Erreur : {message}");
        return 1;
    }
}

public sealed class Arguments
{
    private readonly List<string> positionnels = [];
    private readonly Dictionary<string, string?> nommes = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] OptionsAValeur = ["projet", "depot", "titre", "refuser", "commentaire", "modele", "port"];

    private static readonly string[] Drapeaux = ["auto", "auto-apres-backlog", "suivre", "detail", "forcer", "nouveaux-tests", "sans-navigateur"];

    public static IReadOnlyList<string> OptionsConnues { get; } = [.. OptionsAValeur, .. Drapeaux];

    public static Arguments Analyser(string[] args)
    {
        var resultat = new Arguments();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                resultat.positionnels.Add(args[i]);
                continue;
            }

            var morceaux = args[i][2..].Split('=', 2);
            var nom = morceaux[0].ToLowerInvariant();
            if (Drapeaux.Contains(nom))
            {
                if (morceaux.Length == 2)
                {
                    throw new ArgumentException($"L'option --{nom} ne prend pas de valeur.");
                }

                resultat.nommes[nom] = null;
            }
            else if (OptionsAValeur.Contains(nom))
            {
                var valeur = morceaux.Length == 2
                    ? morceaux[1]
                    : i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
                resultat.nommes[nom] = string.IsNullOrWhiteSpace(valeur)
                    ? throw new ArgumentException($"L'option --{nom} attend une valeur.")
                    : valeur;
            }
            else
            {
                throw new ArgumentException($"Option inconnue : --{nom}. Options possibles : {string.Join(", ", OptionsConnues.Select(o => "--" + o))}.");
            }
        }

        return resultat;
    }

    public void LimiterPositionnels(int maximum)
    {
        if (positionnels.Count > maximum)
        {
            throw new ArgumentException(
                $"Argument en trop : « {positionnels[maximum]} ». Mets la demande, les réponses ou le commentaire entre guillemets.");
        }
    }

    public void LimiterOptions(string commande, IReadOnlyList<string> permises)
    {
        var interdite = nommes.Keys.FirstOrDefault(nom => !permises.Contains(nom));
        if (interdite is not null)
        {
            throw new ArgumentException(permises.Count == 0
                ? $"L'option --{interdite} ne s'applique pas à la commande « {commande} », qui n'accepte aucune option."
                : $"L'option --{interdite} ne s'applique pas à la commande « {commande} ». Options possibles : {string.Join(", ", permises.Select(o => "--" + o))}.");
        }
    }

    public string Positionnel(int index, string nom) =>
        index < positionnels.Count ? positionnels[index] : throw new ArgumentException($"Argument manquant : {nom}.");

    public string PositionnelOuDefaut(int index, string defaut) => index < positionnels.Count ? positionnels[index] : defaut;

    public string? Valeur(string nom) => nommes.TryGetValue(nom, out var valeur) ? valeur : null;

    public bool Drapeau(string nom) => nommes.ContainsKey(nom);
}
