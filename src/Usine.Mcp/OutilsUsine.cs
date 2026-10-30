using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Usine.Core;
using Usine.Core.Etat;
using Usine.Core.Services;

namespace Usine.Mcp;

public sealed record OptionsServeur(bool AutoApresBacklog);

[McpServerToolType]
public sealed class OutilsUsine(ContexteUsine usine, OptionsServeur? options = null)
{
    [McpServerTool(Name = "lancer_demande", Title = "Lancer une demande", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Lance une demande à l'usine agentique : un site, une application ou une nouvelle fonctionnalité à produire. L'usine enchaîne PO, QA amont, Dev et QA dans un bac à sable Docker, et s'arrête pour tes validations. Une application nouvelle terminée avec succès devient un projet de l'usine (dossier projets-generes/<nom> à la racine du dépôt de l'usine) ; pour la faire évoluer, relance avec projet=<nom> : ce même dossier est mis à jour à la fin. Renvoie l'identifiant de la demande ; suis-la ensuite avec voir_avancement.")]
    public string LancerDemande(
        [Description("La demande en langage naturel, aussi précise que possible.")] string demande,
        [Description("Facultatif : chemin absolu d'un dépôt git extérieur à l'usine à faire évoluer ; ce dépôt n'est jamais modifié, le résultat reste dans le livrable de la demande. Laisse vide pour une application nouvelle ou un projet de l'usine (paramètre projet).")] string? depot = null,
        [Description("Facultatif : true pour un traitement sans questions ni validations humaines (mode démonstration ou mesure).")] bool mode_auto = false,
        [Description("Facultatif : titre court de la demande.")] string? titre = null,
        [Description("Facultatif : nom d'un projet de l'usine à faire évoluer (donné par lister_projets). Son dossier est mis à jour en avance rapide quand la demande se termine avec succès. Une seule demande à la fois par projet. Ne le combine pas avec depot.")] string? projet = null) =>
        Executer(() =>
        {
            var apresBacklog = options?.AutoApresBacklog == true;
            if (mode_auto && apresBacklog)
            {
                throw new ArgumentException("Ce serveur (make start-auto) lance chaque demande en mode auto après le backlog : mode_auto n'y est pas possible.", nameof(mode_auto));
            }

            var lancee = usine.Service.Lancer(demande, depot, mode_auto || apresBacklog, titre, projet, cadrageParDemandeur: apresBacklog);
            return $"Demande lancée : **{lancee.Id}**\n\n{FormateurDemande.Resume(lancee, usine.Depot, detaille: false)}";
        });

    [McpServerTool(Name = "voir_avancement", Title = "Voir l'avancement", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Donne l'état d'une demande : étape, action attendue de ta part, questions du PO, besoin, backlog ou tests à valider, résultat des vérifications, coût, derniers événements. Relance automatiquement le traitement d'une demande en cours s'il s'était arrêté ; une demande échouée n'est pas relancée.")]
    public string VoirAvancement(
        [Description("Identifiant de la demande.")] string id,
        [Description("Facultatif : true pour le détail complet (backlog, tests, les 200 derniers événements ; pendant la validation des tests, le contenu du contrat et des fichiers de tests à relire).")] bool detaille = false) =>
        Executer(() => FormateurDemande.Resume(usine.Service.Avancement(id), usine.Depot, detaille));

    [McpServerTool(Name = "repondre_questions", Title = "Répondre aux questions du PO", ReadOnly = false, Destructive = false, OpenWorld = true)]
    [Description("Transmet au PO les réponses à ses questions ; le cadrage reprend ensuite automatiquement.")]
    public string RepondreQuestions(
        [Description("Identifiant de la demande.")] string id,
        [Description("Les réponses, idéalement numérotées comme les questions.")] string reponses) =>
        Executer(() => FormateurDemande.Resume(usine.Service.RepondreQuestions(id, reponses), usine.Depot, detaille: false));

    [McpServerTool(Name = "valider_etape", Title = "Valider ou refuser une étape", ReadOnly = false, Destructive = true, OpenWorld = true)]
    [Description("Approuve ou refuse l'étape en attente : « besoin » (le besoin tel que le PO l'a compris, avant toute user story), « backlog », « tests » ou « livraison ». Un refus exige un commentaire qui dit quoi changer ; l'étape est alors refaite. Approuver la livraison termine la demande et supprime son bac à sable (le livrable exporté est conservé). Pour qu'un défaut constaté sur la livraison devienne un test d'acceptation qui protège aussi les corrections suivantes, dépose avant le refus un ou plusieurs nouveaux fichiers de tests Playwright (*.spec.ts, avec leurs fichiers d'exemple s'il en faut, un PDF par exemple) dans le sous-dossier tests du dossier des tests d'acceptation de la demande (chemin donné par voir_avancement en mode détaillé, rubrique « Tests d'acceptation », ligne « Dossier ») : au refus de la livraison, l'usine les contrôle comme des tests déposés (chargement, contrôle de types TypeScript, nombre maximum de tests ajoutés, sans compter ceux déjà verrouillés, aucun skip ni fixme, pas de mode serial), puis les verrouille avec les tests déjà approuvés et le Dev les reçoit avec le commentaire. S'ils ne passent pas ce contrôle, le refus n'est pas enregistré : le motif est renvoyé et consigné dans la chronologie, la demande attend toujours la validation de la livraison ; corrige ou retire ces fichiers, puis refuse de nouveau. Ne modifie ni ne supprime les fichiers déjà verrouillés : la vérification suivante échouerait, livraison en échec. Refuser la livraison relance le Dev avec le commentaire, même si le nombre maximum de corrections était atteint : les compteurs de corrections repartent de zéro, puis la vérification et l'évaluation par le QA reprennent.")]
    public Task<string> ValiderEtape(
        [Description("Identifiant de la demande.")] string id,
        [Description("L'étape à valider : besoin, backlog, tests ou livraison.")] string etape,
        [Description("true pour approuver, false pour refuser.")] bool approuver,
        [Description("Commentaire. Obligatoire en cas de refus : il est alors transmis au rôle relancé (PO, QA amont ou Dev). À l'approbation, aucun rôle ne le lit : il reste une note pour le demandeur, consignée dans la chronologie de la demande. Pour qu'une précision sur le besoin, le backlog ou les tests soit prise en compte, refuse l'étape avec cette précision.")] string? commentaire = null,
        CancellationToken ct = default) =>
        ExecuterAsync(async () => FormateurDemande.Resume(await usine.Service.ValiderEtapeAsync(id, etape, approuver, commentaire, ct), usine.Depot, detaille: false));

    [McpServerTool(Name = "lister_demandes", Title = "Lister les demandes", ReadOnly = true, OpenWorld = false)]
    [Description("Liste les demandes de l'usine, de la plus récente à la plus ancienne.")]
    public string ListerDemandes() => Executer(() =>
    {
        var demandes = usine.Service.Lister();
        if (demandes.Count == 0)
        {
            return "Aucune demande.";
        }

        var texte = new StringBuilder("Identifiant              Statut            Étape                 Coût    Titre\n");
        foreach (var demande in demandes)
        {
            texte.AppendLine(FormateurDemande.Ligne(demande));
        }

        return texte.ToString();
    });

    [McpServerTool(Name = "lister_projets", Title = "Lister les projets", ReadOnly = true, OpenWorld = false)]
    [Description("Liste les projets de l'usine : nom (à passer en paramètre projet de lancer_demande pour les faire évoluer), chemin du dossier, demandes qui les ont créés et fait évoluer, date de la dernière mise à jour.")]
    public string ListerProjets() => Executer(() => FormateurDemande.ListeProjets(usine.Service.ListerProjets()));

    [McpServerTool(Name = "annuler_demande", Title = "Annuler une demande", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Arrête une demande en cours, ou abandonne une demande échouée, et supprime son bac à sable : une demande annulée ne peut plus être reprise. Le livrable déjà exporté est conservé.")]
    public Task<string> AnnulerDemande([Description("Identifiant de la demande.")] string id, CancellationToken ct = default) =>
        ExecuterAsync(async () => FormateurDemande.Resume(await usine.Service.AnnulerAsync(id, ct), usine.Depot, detaille: false));

    [McpServerTool(Name = "reprendre_demande", Title = "Reprendre une demande échouée", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Reprend une demande échouée à l'étape où elle a échoué, sans refaire les étapes déjà passées : le backlog, les tests d'acceptation et le code déjà produits sont conservés. À n'appeler qu'à la demande du demandeur, une fois la cause de l'échec corrigée (Docker relancé, clé API…). Une clé API remplacée n'est prise en compte qu'après le redémarrage de l'assistant ; sinon le demandeur reprend en ligne de commande depuis un nouveau terminal : usine reprendre <id>. Suis ensuite la demande avec voir_avancement.")]
    public Task<string> ReprendreDemande([Description("Identifiant de la demande.")] string id, CancellationToken ct = default) =>
        ExecuterAsync(async () => FormateurDemande.Resume(await usine.Service.ReprendreAsync(id, ct), usine.Depot, detaille: false));

    [McpServerTool(Name = "recuperer_livrable", Title = "Récupérer le livrable", ReadOnly = true, OpenWorld = false)]
    [Description("Donne l'emplacement du code produit et renvoie le rapport de la demande (tests, coûts, chronologie, comment lancer l'application).")]
    public string RecupererLivrable([Description("Identifiant de la demande.")] string id) => Executer(() =>
    {
        var demande = usine.Service.Charger(id);
        var partiel = usine.Depot.RepertoireTravailPartiel(id);
        if (demande.Livrable is null || !File.Exists(demande.Livrable.Rapport))
        {
            var entete = demande.EstTerminale
                ? $"Aucun livrable pour {id} (statut : {demande.Statut}).{(Directory.Exists(partiel) ? $" Travail partiel conservé : {partiel}" : "")}"
                : $"Pas encore de livrable pour {id}.";
            return $"{entete}\n\n{FormateurDemande.Resume(demande, usine.Depot, detaille: false)}";
        }

        var avertissement = FormateurDemande.AvertissementEchecApresLivraison(demande, usine.Depot) is { } echec ? $"{echec}\n\n" : "";
        return $"{avertissement}Code : {demande.Livrable.RepertoireCode}\nRapport : {demande.Livrable.Rapport}\n\n{File.ReadAllText(demande.Livrable.Rapport)}";
    });

    private static bool EstErreurAttendue(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or DemandeIntrouvableException or IOException
            or UnauthorizedAccessException or System.Text.Json.JsonException;

    private static string Executer(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (EstErreurAttendue(ex))
        {
            throw new McpException(ex.Message);
        }
    }

    private static async Task<string> ExecuterAsync(Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (EstErreurAttendue(ex))
        {
            throw new McpException(ex.Message);
        }
    }
}
