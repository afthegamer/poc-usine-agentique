namespace Usine.Core.Configuration;

public sealed record ConfigurationUsine(
    ConfigUsine Config,
    IReadOnlyList<string> Fichiers,
    string? FichierImpose,
    string RepertoireDonnees,
    string RepertoireProjets,
    ClesApi Cles,
    bool AvecParametresInterface = false)
{
    public bool Utilisable(ReferenceModele reference) =>
        Config.Fournisseurs.TryGetValue(reference.Fournisseur, out var fournisseur)
        && (fournisseur.Type == TypesFournisseur.Scripte
            || string.IsNullOrWhiteSpace(fournisseur.VariableCleApi)
            || Cles.Trouver(fournisseur.VariableCleApi) is not null);

    public bool Prete()
    {
        try
        {
            return Enum.GetValues<RoleAgent>().All(role => Utilisable(Config.ModelePour(role)));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
