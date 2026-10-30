namespace Usine.Core.Configuration;

public sealed class ConfigUsine
{
    public Dictionary<string, ConfigFournisseur> Fournisseurs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ConfigRoles Roles { get; set; } = new();

    public ConfigEffortsRaisonnement EffortsRaisonnement { get; set; } = new();

    public ConfigBacASable BacASable { get; set; } = new();

    public ConfigLimites Limites { get; set; } = new();

    public string? Donnees { get; set; }

    public string Projets { get; set; } = "projets-generes";

    public ReferenceModele ModelePour(RoleAgent role) => ModelesPour(role)[0];

    public IReadOnlyList<ReferenceModele> ModelesPour(RoleAgent role)
    {
        var valeur = role switch
        {
            RoleAgent.Po => Roles.Po,
            RoleAgent.QaAmont => Roles.QaAmont,
            RoleAgent.Dev => Roles.Dev,
            RoleAgent.Qa => Roles.Qa,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
        var references = (valeur ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ReferenceModele.Analyser)
            .ToList();
        return references.Count > 0
            ? references
            : throw new FormatException($"Aucun modèle configuré pour le rôle {role} : format attendu « fournisseur:modèle », éventuellement suivi de modèles de secours séparés par des virgules.");
    }

    public TarifModele? TarifPour(ReferenceModele reference) =>
        Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur)
        && fournisseur.Tarifs.TryGetValue(reference.Modele, out var tarif)
            ? tarif
            : null;

    public int? TokensEntreeParMinutePour(ReferenceModele reference) =>
        Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur)
        && fournisseur.TokensEntreeParMinute.TryGetValue(reference.Modele, out var limite)
            ? limite
            : null;

    public int MaxTokensSortiePour(ReferenceModele reference) =>
        Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur) && fournisseur.MaxTokensSortie is > 0
            ? fournisseur.MaxTokensSortie.Value
            : Limites.MaxTokensSortie;

    public int MaxTokensContextePour(ReferenceModele reference) =>
        Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur) && fournisseur.MaxTokensContexte is > 0
            ? fournisseur.MaxTokensContexte.Value
            : Limites.MaxTokensContexte;

    public string? EffortAppliquePour(RoleAgent role, ReferenceModele reference) =>
        Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur) && TypesFournisseur.AppliquentEffort.Contains(fournisseur.Type)
            ? EffortsRaisonnement.Pour(role)
            : null;

    public IReadOnlyList<string> Valider()
    {
        var erreurs = new List<string>();
        foreach (var role in Enum.GetValues<RoleAgent>())
        {
            IReadOnlyList<ReferenceModele> references;
            try
            {
                references = ModelesPour(role);
            }
            catch (FormatException ex)
            {
                erreurs.Add($"Rôle {role} : {ex.Message}");
                continue;
            }

            foreach (var reference in references)
            {
                if (!Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur))
                {
                    erreurs.Add($"Rôle {role} : le fournisseur « {reference.Fournisseur} » n'est pas déclaré dans « fournisseurs ».");
                }
                else if (!TypesFournisseur.Connus.Contains(fournisseur.Type))
                {
                    erreurs.Add($"Fournisseur « {reference.Fournisseur} » : type « {fournisseur.Type} » inconnu (attendu : {string.Join(", ", TypesFournisseur.Connus)}).");
                }
                else if (ChampManquant(fournisseur) is { } champ)
                {
                    erreurs.Add($"Fournisseur « {reference.Fournisseur} » : « {champ} » est obligatoire pour le type « {fournisseur.Type} ».");
                }
            }
        }

        foreach (var (nom, fournisseur) in Fournisseurs)
        {
            try
            {
                Modeles.EffortsRaisonnement.Lire(fournisseur.EffortRaisonnement);
            }
            catch (FormatException ex)
            {
                erreurs.Add($"Fournisseur « {nom} » : {ex.Message}");
            }

            if (fournisseur.DelaiAppelSecondes is < 1)
            {
                erreurs.Add($"Fournisseur « {nom} » : delaiAppelSecondes doit être supérieur à 0.");
            }

            foreach (var (modele, limite) in fournisseur.TokensEntreeParMinute)
            {
                if (limite < 1)
                {
                    erreurs.Add($"Fournisseur « {nom} » : tokensEntreeParMinute de « {modele} » doit être supérieur à 0.");
                }
            }

            foreach (var (modele, tarif) in fournisseur.Tarifs)
            {
                if (tarif is { EntreeParMillion: < 0 } or { SortieParMillion: < 0 } or { EntreeCacheParMillion: < 0 })
                {
                    erreurs.Add($"Fournisseur « {nom} » : les tarifs de « {modele} » (entreeParMillion, sortieParMillion, entreeCacheParMillion) doivent être supérieurs ou égaux à 0.");
                }
                else if (tarif is { EntreeCacheParMillion: { } cache } && cache > tarif.EntreeParMillion)
                {
                    erreurs.Add($"Fournisseur « {nom} » : entreeCacheParMillion de « {modele} » doit être inférieur ou égal à entreeParMillion, car une entrée servie par le cache ne coûte pas plus qu'une entrée normale.");
                }
            }
        }

        foreach (var role in Enum.GetValues<RoleAgent>())
        {
            try
            {
                Modeles.EffortsRaisonnement.Lire(EffortsRaisonnement.Pour(role));
            }
            catch (FormatException ex)
            {
                erreurs.Add($"effortsRaisonnement, rôle {role} : {ex.Message}");
            }
        }

        if (Limites.MaxIterationsAgent < 1)
        {
            erreurs.Add("limites.maxIterationsAgent doit être supérieur à 0.");
        }

        if (Limites.MaxIterationsReverification < 1)
        {
            erreurs.Add("limites.maxIterationsReverification doit être supérieur à 0.");
        }

        if (Limites.MaxTokensContexte < 0)
        {
            erreurs.Add("limites.maxTokensContexte doit être supérieur ou égal à 0 (0 : sans limite).");
        }

        if (Limites.MaxCorrectionsSignaux < 0)
        {
            erreurs.Add("limites.maxCorrectionsSignaux doit être supérieur ou égal à 0 (0 : écarts seulement signalés).");
        }

        if (Limites.MaxRelancesRole < 0)
        {
            erreurs.Add("limites.maxRelancesRole doit être supérieur ou égal à 0 (0 : sans relance).");
        }

        if (Limites.MaxRefusOutil < 0)
        {
            erreurs.Add("limites.maxRefusOutil doit être supérieur ou égal à 0 (0 : désactivé).");
        }

        if (Limites.PlafondCoutDemande is <= 0)
        {
            erreurs.Add("limites.plafondCoutDemande doit être supérieur à 0.");
        }

        if (Limites.MaxQuestions is < 1)
        {
            erreurs.Add("limites.maxQuestions doit être supérieur à 0.");
        }

        if (Limites.MaxStories < 1)
        {
            erreurs.Add("limites.maxStories doit être supérieur à 0.");
        }

        if (Limites.MaxCriteres < 1)
        {
            erreurs.Add("limites.maxCriteres doit être supérieur à 0.");
        }

        if (Limites.MaxTests < 1)
        {
            erreurs.Add("limites.maxTests doit être supérieur à 0.");
        }

        if (Limites.MaxTokensSortie < 1)
        {
            erreurs.Add("limites.maxTokensSortie doit être supérieur à 0.");
        }

        if (Limites.DelaiBuildSecondes < 5)
        {
            erreurs.Add("limites.delaiBuildSecondes doit être supérieur ou égal à 5.");
        }

        if (Limites.DelaiDemarrageSecondes < 5)
        {
            erreurs.Add("limites.delaiDemarrageSecondes doit être supérieur ou égal à 5.");
        }

        if (Limites.DelaiTestsSecondes < 5)
        {
            erreurs.Add("limites.delaiTestsSecondes doit être supérieur ou égal à 5.");
        }

        if (Limites.TailleMaxSortieOutil < 200)
        {
            erreurs.Add("limites.tailleMaxSortieOutil doit être supérieur ou égal à 200.");
        }

        return erreurs;
    }

    public void ExigerValide()
    {
        var erreurs = Valider();
        if (erreurs.Count > 0)
        {
            throw new InvalidOperationException($"Configuration invalide : {string.Join(" ", erreurs)} Corrige le fichier de configuration, puis vérifie-le avec « usine diagnostic ».");
        }
    }

    private static string? ChampManquant(ConfigFournisseur fournisseur) => fournisseur.Type.ToLowerInvariant() switch
    {
        TypesFournisseur.Anthropic or TypesFournisseur.Gemini when string.IsNullOrWhiteSpace(fournisseur.VariableCleApi) => "variableCleApi",
        TypesFournisseur.OpenAiCompatible when string.IsNullOrWhiteSpace(fournisseur.UrlBase) => "urlBase",
        TypesFournisseur.Scripte when string.IsNullOrWhiteSpace(fournisseur.Scenario) => "scenario",
        _ => null,
    };
}

public sealed class ConfigFournisseur
{
    public string Type { get; set; } = "";

    public string? UrlBase { get; set; }

    public string? VariableCleApi { get; set; }

    public string? Scenario { get; set; }

    public int? MaxTokensSortie { get; set; }

    public int? MaxTokensContexte { get; set; }

    public int? RequetesParMinute { get; set; }

    public Dictionary<string, int> TokensEntreeParMinute { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int? DelaiAppelSecondes { get; set; }

    public string? EffortRaisonnement { get; set; }

    public Dictionary<string, TarifModele> Tarifs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TarifModele
{
    public decimal EntreeParMillion { get; set; }

    public decimal SortieParMillion { get; set; }

    public decimal? EntreeCacheParMillion { get; set; }
}

public sealed class ConfigRoles
{
    public string Po { get; set; } = "anthropic:claude-opus-5";

    public string QaAmont { get; set; } = "anthropic:claude-opus-5";

    public string Dev { get; set; } = "anthropic:claude-opus-5";

    public string Qa { get; set; } = "anthropic:claude-opus-5";
}

public sealed class ConfigEffortsRaisonnement
{
    public string? Po { get; set; }

    public string? QaAmont { get; set; }

    public string? Dev { get; set; }

    public string? Qa { get; set; }

    public string? Pour(RoleAgent role) => (role switch
    {
        RoleAgent.Po => Po,
        RoleAgent.QaAmont => QaAmont,
        RoleAgent.Dev => Dev,
        RoleAgent.Qa => Qa,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    }) is { } valeur && !string.IsNullOrWhiteSpace(valeur) ? valeur.Trim() : null;
}

public sealed class ConfigBacASable
{
    public string Image { get; set; } = "usine-sandbox:10";

    public double Cpus { get; set; } = 4;

    public string Memoire { get; set; } = "6g";

    public int Port { get; set; } = 8080;
}

public sealed class ConfigLimites
{
    public int MaxIterationsAgent { get; set; } = 80;

    public int MaxCorrections { get; set; } = 3;

    public int MaxCorrectionsEvaluation { get; set; } = 1;

    public int MaxIterationsReverification { get; set; } = 30;

    public bool? Sondes { get; set; }

    public int MaxCorrectionsSignaux { get; set; } = 2;

    public bool EvaluationQa { get; set; } = true;

    public decimal? PlafondCoutDemande { get; set; }

    public int? MaxQuestions { get; set; }

    public int MaxStories { get; set; } = 6;

    public int MaxCriteres { get; set; } = 20;

    public int MaxTests { get; set; } = 30;

    public int DelaiCommandeSecondes { get; set; } = 600;

    public int DelaiBuildSecondes { get; set; } = 900;

    public int DelaiDemarrageSecondes { get; set; } = 180;

    public int DelaiTestsSecondes { get; set; } = 600;

    public int TailleMaxSortieOutil { get; set; } = 12000;

    public int MaxTokensSortie { get; set; } = 16000;

    public int MaxTokensContexte { get; set; }

    public int MaxRelancesRole { get; set; } = 1;

    public int MaxRefusOutil { get; set; } = 6;
}

public static class TypesFournisseur
{
    public const string OpenAiCompatible = "openai-compatible";

    public const string Anthropic = "anthropic";

    public const string Gemini = "gemini";

    public const string Scripte = "scripte";

    public static IReadOnlySet<string> Connus { get; } =
        new HashSet<string>([OpenAiCompatible, Anthropic, Gemini, Scripte], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> AppliquentEffort { get; } =
        new HashSet<string>([OpenAiCompatible, Gemini], StringComparer.OrdinalIgnoreCase);
}
