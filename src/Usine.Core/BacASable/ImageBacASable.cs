using Usine.Core.Configuration;

namespace Usine.Core.BacASable;

public static class ImageBacASable
{
    public static async Task<bool> ExisteAsync(ConfigBacASable config, CancellationToken ct)
    {
        var resultat = await BacASableDocker.DockerAsync(["image", "inspect", config.Image], ct, verifier: false);
        return resultat.Reussi;
    }

    public static string? Avertissement(ConfigBacASable config)
    {
        var attendue = new ConfigBacASable().Image;
        return string.Equals(config.Image, attendue, StringComparison.Ordinal)
            ? null
            : $"Attention : la configuration fixe l'image « {config.Image} », alors que cette version de l'usine construit par défaut « {attendue} ». Si elle a été construite par une version précédente, elle n'a pas les évolutions du bac à sable : reconstruis-la avec « usine image --forcer », ou retire « image » du bloc « bacASable » de la configuration puis relance « usine image ».";
    }

    public static async Task<ResultatCommande> ConstruireAsync(ConfigBacASable config, Action<string>? progression, CancellationToken ct)
    {
        var repertoire = Path.Combine(Path.GetTempPath(), $"usine-image-{Guid.NewGuid():N}");
        try
        {
            Ressources.Extraire("sandbox", repertoire);
            progression?.Invoke($"Construction de l'image {config.Image} (plusieurs minutes la première fois)…");
            return await ExecuteurProcessus.ExecuterAsync(
                "docker",
                ["build", "-t", config.Image, repertoire],
                TimeSpan.FromMinutes(40),
                ct: ct);
        }
        finally
        {
            BacASableDocker.SupprimerRepertoire(repertoire);
        }
    }
}
