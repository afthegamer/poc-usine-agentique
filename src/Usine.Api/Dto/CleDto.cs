namespace Usine.Api.Dto;

public sealed record CleDto(string Cle, string? Service = null, string? Adresse = null)
{
    public override string ToString() => $"{nameof(CleDto)} {{ {nameof(Service)} = {Service}, {nameof(Adresse)} = {Adresse} }}";
}
