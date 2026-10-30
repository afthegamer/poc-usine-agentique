namespace Usine.Api.Dto;

public sealed record BacklogDto(string Titre, IReadOnlyList<StoryDto> Stories);
