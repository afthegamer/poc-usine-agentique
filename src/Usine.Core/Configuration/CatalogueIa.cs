namespace Usine.Core.Configuration;

public sealed record IaCatalogue(string Id, string Nom, bool AvecCle);

public sealed record ModeleCatalogue(string Id, string Nom, bool ParDefaut);

public sealed record ServiceCatalogue(string Id, string Nom, string? Adresse);

public sealed record ModeleOllama(string Id, string Nom, bool Prepare);

public static class CatalogueIa
{
    public const string Gemini = "gemini";

    public const string Anthropic = "anthropic";

    public const string Autre = "autre";

    public const string Ollama = "ollama";

    public const string ServiceAdresse = "adresse";

    public const string AdresseOllamaParDefaut = "http://localhost:11434";

    public const int ContextePreparation = 32768;

    public const int SortiePreparation = 2048;

    public const int MaxTokensSortieOllama = 2048;

    public const int MaxTokensContexteOllama = 24000;

    private const string Latest = ":latest";

    private const string SuffixePrepare = "-usine";

    private static readonly IReadOnlyList<ModeleCatalogue> ModelesGemini =
    [
        new("gemini-3.8-flash", "Gemini 3.8 Flash", ParDefaut: true),
        new("gemini-3.1-pro-preview", "Gemini 3.1 Pro", ParDefaut: false),
    ];

    private static readonly IReadOnlyList<ModeleCatalogue> ModelesAnthropic =
    [
        new("claude-haiku-5-5", "Claude Haiku 5.5", ParDefaut: false),
        new("claude-sonnet-5-5", "Claude Sonnet 5.5", ParDefaut: true),
        new("claude-opus-5-5", "Claude Opus 5.5", ParDefaut: false),
    ];

    public static IReadOnlyList<IaCatalogue> Ias { get; } =
    [
        new(Gemini, "Gemini", AvecCle: true),
        new(Anthropic, "Anthropic", AvecCle: true),
        new(Autre, "Autre service", AvecCle: true),
        new(Ollama, "Ollama", AvecCle: false),
    ];

    public static IReadOnlyList<ServiceCatalogue> Services { get; } =
    [
        new("mistral", "Mistral", "https://api.mistral.ai/v1"),
        new("groq", "Groq", "https://api.groq.com/openai/v1"),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1"),
        new(ServiceAdresse, "Autre adresse", null),
    ];

    public static IReadOnlyList<ModeleCatalogue> ModelesProposes(string ia) => ia switch
    {
        Gemini => ModelesGemini,
        Anthropic => ModelesAnthropic,
        _ => [],
    };

    public static string? AdresseService(string? saisie) =>
        Uri.TryCreate(saisie, UriKind.Absolute, out var adresse)
        && (adresse.Scheme == Uri.UriSchemeHttps || (adresse.Scheme == Uri.UriSchemeHttp && adresse.Host is "localhost" or "127.0.0.1" or "[::1]"))
        && adresse.UserInfo.Length == 0
        && adresse.Query.Length == 0
        && adresse.Fragment.Length == 0
            ? adresse.AbsoluteUri.TrimEnd('/')
            : null;

    public static string? AdresseOllama(string? saisie) =>
        Uri.TryCreate(saisie, UriKind.Absolute, out var adresse)
        && (adresse.Scheme == Uri.UriSchemeHttp || adresse.Scheme == Uri.UriSchemeHttps)
        && adresse.UserInfo.Length == 0
        && adresse.PathAndQuery == "/"
        && adresse.Fragment.Length == 0
            ? adresse.GetLeftPart(UriPartial.Authority)
            : null;

    public static IReadOnlyList<ModeleOllama> ModelesOllama(IReadOnlyList<string> noms) =>
        noms.Where(nom => !SansLatest(nom).EndsWith(SuffixePrepare, StringComparison.Ordinal))
            .Select(nom => new ModeleOllama(nom, SansLatest(nom), noms.Any(autre => SansLatest(autre) == NomPrepare(nom))))
            .ToList();

    public static string NomPrepare(string modele) => $"{SansLatest(modele).Replace(':', '-')}{SuffixePrepare}";

    private static string SansLatest(string nom) => nom.EndsWith(Latest, StringComparison.Ordinal) ? nom[..^Latest.Length] : nom;
}
