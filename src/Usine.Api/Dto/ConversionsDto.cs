using Usine.Core.Configuration;
using Usine.Core.Discussion;
using Usine.Core.Etat;
using Usine.Core.Orchestration;
using Usine.Core.Services;

namespace Usine.Api.Dto;

public static class ConversionsDto
{
    public static ProjetDto VersDto(this ProjetUsine projet) =>
        new(projet.Nom, TitreLisible(projet.Demandes.FirstOrDefault()?.Backlog?.Titre, projet.Nom), projet.DerniereMiseAJour);

    public static DemandeDto VersDto(this Demande demande)
    {
        var (etapeAffichee, _, attente) = Affichage(demande);
        return new(demande.Id, TitreLisible(demande.Backlog?.Titre, demande.Titre), demande.Projet, demande.Statut.ToString(), demande.Etape.ToString(), etapeAffichee, attente, demande.MisAJourLe);
    }

    public static DemandeDetailDto VersDetailDto(this Demande demande)
    {
        var (etapeAffichee, sousEtapeFabrication, attente) = Affichage(demande);
        return new(
            demande.Id,
            TitreLisible(demande.Backlog?.Titre, demande.Titre),
            demande.Texte,
            demande.Projet,
            demande.CreeLe,
            demande.MisAJourLe,
            demande.Statut.ToString(),
            demande.Etape.ToString(),
            etapeAffichee,
            sousEtapeFabrication,
            attente,
            [.. demande.Questions],
            demande.Besoin is { } besoin ? new BesoinDto(besoin.Titre, besoin.Resume) : null,
            demande.Backlog is { } backlog ? backlog.VersDto() : null,
            [.. demande.Evenements.Where(evenement => evenement.Acteur == "Demandeur").Select(evenement => new EvenementDto(evenement.Date, evenement.Message))],
            demande.Livrable is { } livrable ? new LivraisonDto(livrable.Succes, demande.PublicationProjet?.Integree == true) : null,
            [.. demande.Evaluation?.Remarques ?? []],
            demande.Erreur);
    }

    public static (string EtapeAffichee, string? SousEtapeFabrication, string? Attente) Affichage(Demande demande)
    {
        var sousEtapeFabrication = demande.Etape switch
        {
            EtapeDemande.TestsAcceptation or EtapeDemande.ValidationTests => "preparation-verifications",
            EtapeDemande.Construction or EtapeDemande.ArbitrageTests => "fabrication",
            EtapeDemande.Verification or EtapeDemande.Evaluation or EtapeDemande.Livraison or EtapeDemande.ValidationLivraison => "verifications",
            _ => null,
        };
        var etapeAffichee = demande.Etape switch
        {
            EtapeDemande.Cadrage => demande.BesoinValide ? "plan" : "comprendre",
            EtapeDemande.Questions or EtapeDemande.ValidationBesoin => "comprendre",
            EtapeDemande.ValidationBacklog => "plan",
            EtapeDemande.Terminee => "pret",
            _ => "fabrication",
        };
        var attente = demande.Statut switch
        {
            StatutDemande.AttenteReponses => "reponses",
            StatutDemande.AttenteValidation => ServiceDemandes.EtapeAValider(demande.Etape),
            _ => null,
        };
        return (etapeAffichee, sousEtapeFabrication, attente);
    }

    public static ModeleDto VersDto(this ModeleCatalogue modele) => new(modele.Id, modele.Nom, null, modele.ParDefaut);

    public static ModeleDto VersDto(this ModeleOllama modele) => new(modele.Id, modele.Nom, modele.Prepare, false);

    public static RolesDto VersDto(this RolesInterface roles) => new(Choix(roles.Po), Choix(roles.QaAmont), Choix(roles.Dev), Choix(roles.Qa));

    public static RolesInterface VersRoles(this RolesDto roles) => new() { Po = Reglage(roles.Po), QaAmont = Reglage(roles.QaAmont), Dev = Reglage(roles.Dev), Qa = Reglage(roles.Qa) };

    private static ChoixDto? Choix(string? reglage) =>
        reglage?.IndexOf(':', StringComparison.Ordinal) is int index and > 0 ? new(reglage[..index], reglage[(index + 1)..]) : null;

    private static string? Reglage(ChoixDto? choix) => choix is null ? null : $"{choix.Ia}:{choix.Modele}";

    private static BacklogDto VersDto(this Backlog backlog) =>
        new(backlog.Titre, [.. backlog.Stories.Select(story => new StoryDto(story.Id, story.Titre, story.Description, [.. story.Criteres.Select(critere => new CritereDto(critere.Id, RevueGuidee.SansCalcul().Replace(critere.Texte, "")))]))]);

    private static string TitreLisible(string? titreBacklog, string repli) =>
        string.IsNullOrWhiteSpace(titreBacklog) ? repli : titreBacklog;
}