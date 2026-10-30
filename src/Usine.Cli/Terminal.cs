namespace Usine.Cli;

public interface ITerminal
{
    int Largeur { get; }

    (int Gauche, int Haut) Curseur { get; }

    bool ToucheDisponible { get; }

    void PlacerCurseur(int gauche, int haut);

    void Ecrire(string texte);

    Task<ConsoleKeyInfo> LireToucheAsync(CancellationToken ct);
}

public sealed class TerminalSysteme : ITerminal
{
    private static readonly TimeSpan AttenteTouche = TimeSpan.FromMilliseconds(20);

    public int Largeur => Console.BufferWidth;

    public (int Gauche, int Haut) Curseur => Console.GetCursorPosition();

    public bool ToucheDisponible => Console.KeyAvailable;

    public void PlacerCurseur(int gauche, int haut) => Console.SetCursorPosition(gauche, haut);

    public void Ecrire(string texte) => Console.Out.Write(texte);

    public async Task<ConsoleKeyInfo> LireToucheAsync(CancellationToken ct)
    {
        while (!Console.KeyAvailable)
        {
            await Task.Delay(AttenteTouche, ct);
        }

        return Console.ReadKey(intercept: true);
    }
}
