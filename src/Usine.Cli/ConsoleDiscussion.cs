using System.Text;
using Usine.Core.Discussion;

namespace Usine.Cli;

public sealed class ConsoleDiscussion : IConsoleDiscussion
{
    private const string Invite = "> ";

    private readonly Lock verrou = new();

    private readonly TextReader lecteur;

    private readonly TextWriter ecrivain;

    private readonly ITerminal? terminal;

    private readonly StringBuilder saisie = new();

    private string? inviteEnCours;

    private Task<string?>? ligneEnAttente;

    private Task<ConsoleKeyInfo>? toucheEnAttente;

    public ConsoleDiscussion(TextReader? entree = null, TextWriter? sortie = null)
    {
        lecteur = entree ?? Console.In;
        ecrivain = sortie ?? Console.Out;
    }

    public ConsoleDiscussion(ITerminal terminal)
        : this(TextReader.Null, TextWriter.Null)
    {
        this.terminal = terminal;
    }

    public static ConsoleDiscussion PourLaConsole() =>
        Console.IsInputRedirected || Console.IsOutputRedirected ? new ConsoleDiscussion() : new ConsoleDiscussion(new TerminalSysteme());

    public Task<string?> LireAsync(CancellationToken ct, CancellationToken cession = default) => LireLigneAsync(Invite, cession, ct);

    public void Ecrire(string texte) => EcrireAuDessus(texte, erreur: false);

    public void EcrireErreur(string texte) => EcrireAuDessus(texte, erreur: true);

    public async Task<string?> LireConfirmationAsync(string question, CancellationToken ct) =>
        (await LireLigneAsync($"{question} [o/N] ", CancellationToken.None, ct))?.Trim();

    private async Task<string?> LireLigneAsync(string invite, CancellationToken cession, CancellationToken ct)
    {
        if (terminal is not null)
        {
            return await LireTouchesAsync(terminal, invite, cession, ct);
        }

        if (ligneEnAttente is null)
        {
            cession.ThrowIfCancellationRequested();
            ligneEnAttente = Task.Run(lecteur.ReadLine);
        }

        lock (verrou)
        {
            ecrivain.Write(invite);
        }

        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cession.Register(() => signal.TrySetResult()))
        {
            await Task.WhenAny(ligneEnAttente, signal.Task).WaitAsync(ct);
        }

        if (!ligneEnAttente.IsCompleted)
        {
            lock (verrou)
            {
                ecrivain.WriteLine();
            }

            throw new OperationCanceledException(cession);
        }

        var ligne = ligneEnAttente;
        ligneEnAttente = null;
        return await ligne;
    }

    private async Task<string?> LireTouchesAsync(ITerminal console, string invite, CancellationToken cession, CancellationToken ct)
    {
        await IgnorerToucheesHorsLectureAsync(console, ct);
        if (!console.ToucheDisponible)
        {
            cession.ThrowIfCancellationRequested();
        }

        lock (verrou)
        {
            var coupure = invite.LastIndexOf('\n');
            if (coupure >= 0)
            {
                console.Ecrire(invite[..coupure].TrimEnd('\r').ReplaceLineEndings() + Environment.NewLine);
            }

            inviteEnCours = invite[(coupure + 1)..];
            saisie.Clear();
            console.Ecrire(inviteEnCours);
        }

        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var abonnement = cession.Register(() => signal.TrySetResult());
        try
        {
            while (true)
            {
                if (toucheEnAttente is null || toucheEnAttente.IsCanceled || toucheEnAttente.IsFaulted)
                {
                    toucheEnAttente = console.LireToucheAsync(ct);
                }

                bool vide;
                lock (verrou)
                {
                    vide = saisie.Length == 0;
                }

                if (vide)
                {
                    await Task.WhenAny(toucheEnAttente, signal.Task);
                    if (!toucheEnAttente.IsCompleted && !console.ToucheDisponible)
                    {
                        lock (verrou)
                        {
                            Effacer(console);
                        }

                        throw new OperationCanceledException(cession);
                    }
                }

                var touche = await toucheEnAttente;
                toucheEnAttente = null;
                lock (verrou)
                {
                    switch (touche.Key)
                    {
                        case ConsoleKey.Enter:
                            var ligne = saisie.ToString();
                            inviteEnCours = null;
                            saisie.Clear();
                            console.Ecrire(Environment.NewLine);
                            return ligne;
                        case ConsoleKey.Backspace:
                            if (saisie.Length > 0)
                            {
                                Effacer(console);
                                saisie.Length--;
                                console.Ecrire(inviteEnCours + saisie);
                            }

                            break;
                        case ConsoleKey.Escape:
                            Effacer(console);
                            saisie.Clear();
                            console.Ecrire(inviteEnCours ?? "");
                            break;
                        default:
                            if (touche.KeyChar != '\0' && !char.IsControl(touche.KeyChar))
                            {
                                saisie.Append(touche.KeyChar);
                                console.Ecrire(touche.KeyChar.ToString());
                            }

                            break;
                    }
                }
            }
        }
        finally
        {
            lock (verrou)
            {
                inviteEnCours = null;
                saisie.Clear();
            }
        }
    }

    private async Task IgnorerToucheesHorsLectureAsync(ITerminal console, CancellationToken ct)
    {
        while (true)
        {
            if (toucheEnAttente is { IsCompleted: true })
            {
                toucheEnAttente = null;
            }

            if (!console.ToucheDisponible)
            {
                return;
            }

            if (toucheEnAttente is not null)
            {
                await Task.WhenAny(toucheEnAttente);
            }
            else
            {
                await console.LireToucheAsync(ct);
            }
        }
    }

    private void EcrireAuDessus(string texte, bool erreur)
    {
        lock (verrou)
        {
            if (terminal is null)
            {
                (erreur ? Console.Error : ecrivain).WriteLine(texte);
                return;
            }

            var lectureEnCours = inviteEnCours is not null;
            if (lectureEnCours)
            {
                Effacer(terminal);
            }

            if (erreur)
            {
                Console.Error.WriteLine(texte);
            }
            else
            {
                terminal.Ecrire(texte.ReplaceLineEndings() + Environment.NewLine);
            }

            if (lectureEnCours)
            {
                terminal.Ecrire(inviteEnCours + saisie);
            }
        }
    }

    private void Effacer(ITerminal console)
    {
        var longueur = (inviteEnCours?.Length ?? 0) + saisie.Length;
        var largeur = Math.Max(1, console.Largeur);
        var (gauche, haut) = console.Curseur;
        var debut = (haut * largeur) + gauche - longueur;
        var ligne = Math.Max(0, (debut + largeur - 1) / largeur);
        console.PlacerCurseur(0, ligne);
        console.Ecrire(new string(' ', longueur));
        console.PlacerCurseur(0, ligne);
    }
}
