namespace Usine.Core;

internal static class Texte
{
    internal static string Couper(string texte, int max) => texte.Length <= max ? texte : texte[..max] + "…";
}
