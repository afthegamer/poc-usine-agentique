namespace Usine.Api.Dto;

public sealed record DemandeDto(string Id, string Titre, string? Projet, string Statut, string Etape, string EtapeAffichee, string? Attente, DateTimeOffset MisAJourLe);