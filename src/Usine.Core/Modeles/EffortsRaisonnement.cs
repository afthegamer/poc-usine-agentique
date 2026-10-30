using Microsoft.Extensions.AI;

namespace Usine.Core.Modeles;

public static class EffortsRaisonnement
{
    private static readonly Dictionary<string, ReasoningEffort> Valeurs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = ReasoningEffort.None,
        ["low"] = ReasoningEffort.Low,
        ["medium"] = ReasoningEffort.Medium,
        ["high"] = ReasoningEffort.High,
        ["extraHigh"] = ReasoningEffort.ExtraHigh,
    };

    public static ReasoningEffort? Lire(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return null;
        }

        return Valeurs.TryGetValue(valeur.Trim(), out var effort)
            ? effort
            : throw new FormatException($"effortRaisonnement « {valeur} » inconnu (attendu : {string.Join(", ", Valeurs.Keys)}).");
    }
}
