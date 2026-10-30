using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Usine.Api.Dto;
using Usine.Core;
using Usine.Core.Discussion;
using Usine.Core.Etat;

namespace Usine.Api;

internal static class ActionsDemandes
{
    public static Created<DemandeDetailDto> Lancer(ContexteUsine usine, LancementDto corps)
    {
        var demande = usine.Service.Lancer(corps.Texte, depotSource: null, modeAuto: true, corps.Titre, corps.Projet, cadrageParDemandeur: true);
        return TypedResults.Created($"/api/demandes/{demande.Id}", demande.VersDetailDto());
    }

    public static DemandeDetailDto Repondre(ContexteUsine usine, string id, ReponsesDto corps)
    {
        var reponses = corps.Reponses ?? [];
        var demande = usine.Service.Charger(id);
        if (demande.Statut == StatutDemande.AttenteReponses && reponses.Count != demande.Questions.Count)
        {
            throw new ArgumentException($"La demande {id} attend une réponse par question, dans l'ordre : {demande.Questions.Count} question{(demande.Questions.Count > 1 ? "s" : "")}, {reponses.Count} réponse{(reponses.Count > 1 ? "s" : "")} reçue{(reponses.Count > 1 ? "s" : "")}.");
        }

        var numerotees = reponses.Select((reponse, i) => ($"{i + 1}", string.IsNullOrWhiteSpace(reponse) ? RevueGuidee.SansPreference : reponse.Trim()));
        return usine.Service.RepondreQuestions(id, RevueGuidee.FormaterReponses(numerotees)).VersDetailDto();
    }

    public static async Task<DemandeDetailDto> ValiderAsync(ContexteUsine usine, string id, ValidationDto corps, CancellationToken ct)
    {
        var remarques = (corps.Remarques ?? [])
            .Where(remarque => remarque is not null && !string.IsNullOrWhiteSpace(remarque.Texte))
            .Select(remarque => ((remarque.Reference ?? "").Trim(), remarque.Texte.Trim()))
            .ToList();
        var commentaire = remarques.Count == 0 ? null : RevueGuidee.FormaterRemarques(remarques);
        return (await usine.Service.ValiderEtapeAsync(id, corps.Etape ?? "", corps.Approuver, commentaire, ct)).VersDetailDto();
    }
}
