using Microsoft.Extensions.Configuration;

namespace Usine.Core.Configuration;

public sealed record CleApi(string Valeur, string Origine)
{
    public override string ToString() => $"{nameof(CleApi)} {{ {nameof(Origine)} = {Origine} }}";
}

public sealed class ClesApi(IConfigurationRoot sources, IReadOnlyList<string> origines)
{
    public const string OrigineUtilisateurWindows = "variable d'environnement de l'utilisateur Windows";

    public static ClesApi Environnement() =>
        new(new ConfigurationBuilder().AddEnvironmentVariables().Build(), [ChargeurConfig.OrigineEnvironnement]);

    public CleApi? Trouver(string nom)
    {
        var fournisseurs = sources.Providers.ToList();
        for (var i = fournisseurs.Count - 1; i >= 0; i--)
        {
            if (fournisseurs[i].TryGet(nom, out var valeur) && !string.IsNullOrWhiteSpace(valeur))
            {
                return new CleApi(valeur, origines[i]);
            }
        }

        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(nom, EnvironmentVariableTarget.User) is { } utilisateur && !string.IsNullOrWhiteSpace(utilisateur))
        {
            return new CleApi(utilisateur, OrigineUtilisateurWindows);
        }

        return null;
    }
}
