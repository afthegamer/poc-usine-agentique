namespace Usine.Api.Dto;

public sealed record IaDto(string Id, bool Connectee, string? Origine, string? Modele, IReadOnlyList<ModeleDto> Modeles, string? Service, string? Adresse);
