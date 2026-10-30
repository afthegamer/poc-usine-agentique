namespace Usine.Api.Dto;

public sealed record ParametresDto(bool Pret, string? Ia, IReadOnlyList<IaDto> Ias, RolesDto Roles);
