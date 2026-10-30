namespace Usine.Api.Dto;

public sealed record ValidationDto(string Etape, bool Approuver, IReadOnlyList<RemarqueDto>? Remarques = null);
