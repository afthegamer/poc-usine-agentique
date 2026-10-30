using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Usine.Core.Discussion;

namespace Usine.Api;

internal sealed class ConsoleWeb : IConsoleDiscussion
{
    public const string EvenementMessage = "message";

    public const string EvenementOutil = "outil";

    public const string EvenementErreur = "erreur";

    public const string EvenementConfirmation = "confirmation";

    public const string EvenementFin = "fin";

    public const string Terminee = "La discussion est terminée.";

    private readonly Channel<string> messages = Channel.CreateUnbounded<string>();

    private readonly Channel<SseItem<string>> evenements = Channel.CreateUnbounded<SseItem<string>>();

    private TaskCompletionSource<bool>? confirmation;

    public async Task<string?> LireAsync(CancellationToken ct, CancellationToken cession = default)
    {
        using var lecture = CancellationTokenSource.CreateLinkedTokenSource(ct, cession);
        return await messages.Reader.ReadAsync(lecture.Token);
    }

    public void Ecrire(string texte) => Publier(Genre(texte), texte);

    public async Task<string?> LireConfirmationAsync(string question, CancellationToken ct)
    {
        var attente = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref confirmation, attente);
        Publier(EvenementConfirmation, question);
        try
        {
            return await attente.Task.WaitAsync(ct) ? "oui" : "non";
        }
        finally
        {
            Interlocked.CompareExchange(ref confirmation, null, attente);
        }
    }

    public void Transmettre(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            throw new ArgumentException("Le message est vide : écris ce que tu veux dire à l'assistant.");
        }

        if (!messages.Writer.TryWrite(texte))
        {
            throw new InvalidOperationException(Terminee);
        }
    }

    public void Confirmer(bool accord)
    {
        if (Interlocked.Exchange(ref confirmation, null) is not { } attente || !attente.TrySetResult(accord))
        {
            throw new InvalidOperationException("Aucune question n'attend ta confirmation dans cette discussion.");
        }
    }

    public void FermerLesMessages() => messages.Writer.TryComplete();

    public void Terminer()
    {
        messages.Writer.TryComplete();
        Publier(EvenementFin, Terminee);
        evenements.Writer.TryComplete();
    }

    public async IAsyncEnumerable<SseItem<string>> SuivreAsync(HttpResponse reponse, CancellationToken arretServeur, [EnumeratorCancellation] CancellationToken deconnexion = default)
    {
        using var fin = CancellationTokenSource.CreateLinkedTokenSource(deconnexion, arretServeur);
        await reponse.Body.FlushAsync(fin.Token);
        while (true)
        {
            try
            {
                if (!await evenements.Reader.WaitToReadAsync(fin.Token))
                {
                    yield break;
                }
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            while (evenements.Reader.TryRead(out var evenement))
            {
                yield return evenement;
            }
        }
    }

    private void Publier(string genre, string texte) => evenements.Writer.TryWrite(new SseItem<string>(texte, genre));

    private static string Genre(string texte) =>
        texte.StartsWith("→ ", StringComparison.Ordinal) ? EvenementOutil
        : texte.StartsWith("Erreur : ", StringComparison.Ordinal) ? EvenementErreur
        : EvenementMessage;
}
