namespace Usine.Core.Configuration;

public sealed record ParametresInterface
{
    public string? Ia { get; init; }

    public ModelesInterface Modeles { get; init; } = new();

    public ServiceAutreInterface? Autre { get; init; }

    public OllamaInterface Ollama { get; init; } = new();

    public RolesInterface Roles { get; init; } = new();
}

public sealed record ModelesInterface
{
    public string? Gemini { get; init; }

    public string? Anthropic { get; init; }

    public string? Autre { get; init; }

    public string? Ollama { get; init; }

    public string? Pour(string ia) => ia switch
    {
        CatalogueIa.Gemini => Gemini,
        CatalogueIa.Anthropic => Anthropic,
        CatalogueIa.Autre => Autre,
        CatalogueIa.Ollama => Ollama,
        _ => null,
    };
}

public sealed record ServiceAutreInterface(string Service, string Adresse);

public sealed record OllamaInterface
{
    public string Adresse { get; init; } = CatalogueIa.AdresseOllamaParDefaut;
}

public sealed record RolesInterface
{
    public string? Po { get; init; }

    public string? QaAmont { get; init; }

    public string? Dev { get; init; }

    public string? Qa { get; init; }

    public string? Pour(RoleAgent role) => role switch
    {
        RoleAgent.Po => Po,
        RoleAgent.QaAmont => QaAmont,
        RoleAgent.Dev => Dev,
        RoleAgent.Qa => Qa,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };
}
