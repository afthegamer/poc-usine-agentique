using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Hosting;
using Usine.Api.Dto;
using Usine.Core;
using Usine.Core.Configuration;
using Usine.Core.Discussion;
using Usine.Core.Orchestration;

namespace Usine.Api;

internal sealed class Discussions(ContexteUsine usine, Func<ConfigurationUsine> lireConfiguration, IFabriqueDiscussions fabrique, IHostApplicationLifetime vie, TimeProvider? temps) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Discussion> ouvertes = new(StringComparer.Ordinal);

    public async Task<Created<DiscussionDto>> OuvrirAsync(LancementDiscussionDto? corps, CancellationToken ct)
    {
        var projet = corps?.Projet;
        if (projet is not null)
        {
            Projets.Chemin(usine.Depot, projet);
        }

        var demande = corps?.Demande is { } identifiant ? usine.Service.Charger(identifiant).VersDto() : null;
        var configuration = lireConfiguration();
        var reference = configuration.Config.ModelePour(RoleAgent.Po);
        var informations = new InformationsUsine();
        if (projet is not null)
        {
            informations.Ajouter($"La discussion porte sur le projet « {projet} » : un ajout à ce projet se lance avec projet={projet}.");
        }

        if (demande is not null)
        {
            informations.Ajouter($"La discussion porte sur la demande « {demande.Titre} » : tes outils la désignent par id={demande.Id}.");
        }

        var outillage = await fabrique.CreerAsync(configuration, reference, ct);
        var console = new ConsoleWeb();
        var arret = CancellationTokenSource.CreateLinkedTokenSource(vie.ApplicationStopping);
        var tache = Task.Run(() => ExecuterAsync(outillage, console, configuration.Config.MaxTokensSortiePour(reference), informations, arret.Token), CancellationToken.None);
        var id = Guid.NewGuid().ToString("N");
        ouvertes[id] = new Discussion(console, arret, tache);
        return TypedResults.Created($"/api/discussions/{id}", new DiscussionDto(id));
    }

    public ConsoleWeb Trouver(string id) =>
        ouvertes.TryGetValue(id, out var discussion) ? discussion.Console : throw new DiscussionIntrouvableException(id);

    public NoContent Transmettre(string id, MessageDto corps)
    {
        Trouver(id).Transmettre(corps.Texte);
        return TypedResults.NoContent();
    }

    public NoContent Confirmer(string id, ConfirmationDto corps)
    {
        Trouver(id).Confirmer(corps.Accord);
        return TypedResults.NoContent();
    }

    public async Task<NoContent> FermerAsync(string id)
    {
        if (!ouvertes.TryRemove(id, out var discussion))
        {
            throw new DiscussionIntrouvableException(id);
        }

        await discussion.ArreterAsync();
        return TypedResults.NoContent();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in ouvertes.Keys)
        {
            if (ouvertes.TryRemove(id, out var discussion))
            {
                await discussion.ArreterAsync();
            }
        }
    }

    private async Task ExecuterAsync(OutillageDiscussion outillage, ConsoleWeb console, int maxTokensSortie, InformationsUsine informations, CancellationToken ct)
    {
        try
        {
            await using (outillage)
            {
                var consigne = Ressources.Lire("prompts/chat-web.md")
                    + (string.IsNullOrWhiteSpace(outillage.Instructions) ? "" : $"\n\n{outillage.Instructions}");
                var fil = new FilEnDirect(usine.Depot, usine.Config.Limites, _ => { }, informations, temps);
                fil.Demarrer();
                using var arretFil = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var suivi = fil.ExecuterAsync(arretFil.Token);
                try
                {
                    await new BoucleDiscussion(outillage.Client, outillage.Outils, console, consigne, maxTokensSortie, informations).ExecuterAsync(ct);
                }
                finally
                {
                    console.FermerLesMessages();
                    await arretFil.CancelAsync();
                    await suivi;
                }
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                console.Ecrire($"Erreur : {ex.Message}");
            }
        }
        finally
        {
            console.Terminer();
        }
    }

    private sealed class Discussion(ConsoleWeb console, CancellationTokenSource arret, Task tache)
    {
        public ConsoleWeb Console { get; } = console;

        public async Task ArreterAsync()
        {
            await arret.CancelAsync();
            await tache;
            arret.Dispose();
        }
    }
}

internal sealed class DiscussionIntrouvableException(string id) : Exception($"Aucune discussion avec l'identifiant « {id} ».");
