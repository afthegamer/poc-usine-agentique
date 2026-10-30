using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Usine.Api.Dto;
using Usine.Core.Discussion;
using Usine.Core.Etat;

namespace Usine.Api;

internal static class FluxDemandes
{
    public const string Evenement = "demande";

    public static async IAsyncEnumerable<DemandeDto> SuivreAsync(DepotDemandes depot, TimeProvider horloge, HttpResponse reponse, CancellationToken arretServeur, [EnumeratorCancellation] CancellationToken deconnexion = default)
    {
        using var fin = CancellationTokenSource.CreateLinkedTokenSource(deconnexion, arretServeur);
        await reponse.Body.FlushAsync(fin.Token);
        var vues = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        while (true)
        {
            foreach (var id in FilEnDirect.Identifiants(depot))
            {
                if (FilEnDirect.Charger(depot, id) is { } demande && (!vues.TryGetValue(id, out var misAJourLe) || misAJourLe != demande.MisAJourLe))
                {
                    vues[id] = demande.MisAJourLe;
                    yield return demande.VersDto();
                }
            }

            try
            {
                await Task.Delay(FilEnDirect.Intervalle, horloge, fin.Token);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
