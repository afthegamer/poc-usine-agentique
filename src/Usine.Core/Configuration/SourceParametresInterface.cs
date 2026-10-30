using System.Globalization;

namespace Usine.Core.Configuration;

public static class SourceParametresInterface
{
    public static string VariableCle(string ia) => $"USINE_CLE_{ia.ToUpperInvariant()}";

    public static Dictionary<string, string?> Entrees(ParametresInterface parametres, IReadOnlyCollection<string> iasEnregistrees, IReadOnlyDictionary<string, string> cles)
    {
        var entrees = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AjouterCle(entrees, iasEnregistrees, cles, CatalogueIa.Gemini, TypesFournisseur.Gemini);
        AjouterCle(entrees, iasEnregistrees, cles, CatalogueIa.Anthropic, TypesFournisseur.Anthropic);
        if (AjouterCle(entrees, iasEnregistrees, cles, CatalogueIa.Autre, TypesFournisseur.OpenAiCompatible) && parametres.Autre is { } autre)
        {
            entrees[$"fournisseurs:{CatalogueIa.Autre}:urlBase"] = autre.Adresse;
        }

        var reglages = Enum.GetValues<RoleAgent>().Select(parametres.Roles.Pour).OfType<string>();
        if (parametres.Ia == CatalogueIa.Ollama || reglages.Any(reglage => reglage.StartsWith($"{CatalogueIa.Ollama}:", StringComparison.Ordinal)))
        {
            entrees[$"fournisseurs:{CatalogueIa.Ollama}:type"] = TypesFournisseur.OpenAiCompatible;
            entrees[$"fournisseurs:{CatalogueIa.Ollama}:urlBase"] = $"{parametres.Ollama.Adresse}/v1";
            entrees[$"fournisseurs:{CatalogueIa.Ollama}:maxTokensSortie"] = CatalogueIa.MaxTokensSortieOllama.ToString(CultureInfo.InvariantCulture);
            entrees[$"fournisseurs:{CatalogueIa.Ollama}:maxTokensContexte"] = CatalogueIa.MaxTokensContexteOllama.ToString(CultureInfo.InvariantCulture);
        }

        if (parametres.Ia is { } ia)
        {
            var general = parametres.Modeles.Pour(ia) is { } modele ? Reference(ia, modele) : null;
            foreach (var role in Enum.GetValues<RoleAgent>())
            {
                var reference = parametres.Roles.Pour(role) is { } reglage ? ReferenceEffective(reglage) : general;
                if (reference is not null)
                {
                    entrees[$"roles:{role}"] = reference;
                }
            }
        }

        return entrees;
    }

    private static bool AjouterCle(Dictionary<string, string?> entrees, IReadOnlyCollection<string> iasEnregistrees, IReadOnlyDictionary<string, string> cles, string ia, string type)
    {
        if (!iasEnregistrees.Contains(ia))
        {
            return false;
        }

        entrees[$"fournisseurs:{ia}:type"] = type;
        entrees[$"fournisseurs:{ia}:variableCleApi"] = VariableCle(ia);
        if (cles.TryGetValue(ia, out var cle))
        {
            entrees[VariableCle(ia)] = cle;
        }

        return true;
    }

    private static string ReferenceEffective(string reglage)
    {
        var index = reglage.IndexOf(':', StringComparison.Ordinal);
        return index > 0 ? Reference(reglage[..index], reglage[(index + 1)..]) : reglage;
    }

    private static string Reference(string ia, string modele) =>
        $"{ia}:{(ia == CatalogueIa.Ollama ? CatalogueIa.NomPrepare(modele) : modele)}";
}
