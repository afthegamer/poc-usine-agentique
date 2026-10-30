namespace Usine.Api.Dto;

public sealed record DemandeDetailDto(string Id, string Titre, string Texte, string? Projet, DateTimeOffset CreeLe, DateTimeOffset MisAJourLe, string Statut, string Etape, string EtapeAffichee, string? SousEtapeFabrication, string? Attente, IReadOnlyList<string> Questions, BesoinDto? Besoin, BacklogDto? Backlog, IReadOnlyList<EvenementDto> EvenementsDemandeur, LivraisonDto? Livraison, IReadOnlyList<string> RemarquesQa, string? Erreur);
