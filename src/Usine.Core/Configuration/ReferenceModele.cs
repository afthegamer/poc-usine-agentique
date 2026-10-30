namespace Usine.Core.Configuration;

public sealed record ReferenceModele(string Fournisseur, string Modele)
{
    public static ReferenceModele Analyser(string valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new FormatException("Référence de modèle vide : format attendu « fournisseur:modèle ».");
        }

        var index = valeur.IndexOf(':', StringComparison.Ordinal);
        if (index <= 0 || index == valeur.Length - 1)
        {
            throw new FormatException($"Référence de modèle invalide « {valeur} » : format attendu « fournisseur:modèle ».");
        }

        var fournisseur = valeur[..index].Trim();
        var modele = valeur[(index + 1)..].Trim();
        if (fournisseur.Length == 0 || modele.Length == 0)
        {
            throw new FormatException($"Référence de modèle invalide « {valeur} » : format attendu « fournisseur:modèle ».");
        }

        return new ReferenceModele(fournisseur, modele);
    }

    public override string ToString() => $"{Fournisseur}:{Modele}";
}
