namespace Usine.Api.Dto;

public sealed record StoryDto(string Id, string Titre, string Description, IReadOnlyList<CritereDto> Criteres);
