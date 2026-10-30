using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Usine.Core.Configuration;
using Usine.Core.Etat;
using Usine.Core.Orchestration;

namespace Usine.Core.Discussion;

public sealed class InformationsUsine
{
    private readonly ConcurrentQueue<string> file = new();

    public void Ajouter(string information) => file.Enqueue(information);

    public IReadOnlyList<string> Prendre()
    {
        var informations = new List<string>();
        while (file.TryDequeue(out var information))
        {
            informations.Add(information);
        }

        return informations;
    }
}

public sealed class FilEnDirect(DepotDemandes depot, ConfigLimites limites, Action<string> ecrire, InformationsUsine informations, TimeProvider? temps = null, RevuesEnAttente? revues = null)
{
    public const string Separateur = "------------------------------------------------------------";

    public const string AucuneDemande = "Aucune demande en cours : celles que tu lanceras ici défileront en direct.";

    public static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan IntervalleAvancement = TimeSpan.FromSeconds(60);

    private readonly TimeProvider horloge = temps ?? TimeProvider.System;

    private readonly Dictionary<string, Suivi> suivies = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Ecartee> ecartees = new(StringComparer.Ordinal);

    private bool incidentSignale;

    public async Task ExecuterAsync(CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await Task.Delay(Intervalle, horloge, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                Tour();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!incidentSignale)
                {
                    incidentSignale = true;
                    ecrire($"Fil en direct : erreur inattendue ({ex.GetType().Name} : {ex.Message.ReplaceLineEndings(" ")}), le suivi continue.");
                }
            }
        }
    }

    public void Demarrer()
    {
        foreach (var id in Identifiants(depot))
        {
            var dateEtat = DateEtat(id);
            var demande = Charger(depot, id);
            if (demande is null || demande.EstTerminale)
            {
                ecartees[id] = new Ecartee(dateEtat, LecteurJournal.Taille(depot.CheminJournal(id)), demande is not null);
                continue;
            }

            var suivi = Suivre(id, LecteurJournal.Taille(depot.CheminJournal(id)));
            ecrire($"En cours : {demande.Id}  {demande.Titre}  ({demande.Statut}, étape {demande.Etape}){(demande.Projet is { } projet ? $"  projet {projet}" : "")}");
            Observer(suivi, demande);
        }

        if (suivies.Count == 0)
        {
            ecrire(AucuneDemande);
        }
    }

    public void Tour()
    {
        foreach (var id in Identifiants(depot))
        {
            if (suivies.ContainsKey(id))
            {
                continue;
            }

            if (!ecartees.TryGetValue(id, out var ecartee))
            {
                Suivre(id, 0);
                continue;
            }

            var dateEtat = DateEtat(id);
            if (dateEtat == ecartee.DateEtat && ecartee.Lue)
            {
                continue;
            }

            if (Charger(depot, id) is not { } demande)
            {
                continue;
            }

            ecartees[id] = ecartee with { DateEtat = dateEtat, Lue = true };
            if (!demande.EstTerminale)
            {
                Suivre(id, ecartee.TailleJournal);
            }
        }

        foreach (var suivi in suivies.Values)
        {
            foreach (var ligne in suivi.Journal.Lire())
            {
                ecrire(ligne);
            }

            if (Charger(depot, suivi.Id) is { } demande)
            {
                Observer(suivi, demande);
            }
        }
    }

    public string Avancement(Demande demande)
    {
        var parties = new List<string> { $"{demande.Statut}, étape {demande.Etape}" };
        if (demande.Etape == EtapeDemande.Construction)
        {
            var passe = demande.NaturePasse switch
            {
                NaturePasse.BloquantsQa => Fraction("correction des bloquants du QA", demande.CorrectionsEvaluation, limites.MaxCorrectionsEvaluation),
                NaturePasse.EcartsSonde => Fraction("correction des écarts de la sonde", demande.CorrectionsSignaux, limites.MaxCorrectionsSignaux),
                _ => Fraction("passe", demande.Corrections + 1, limites.MaxCorrections + 1),
            };
            var relance = demande.RelancesContexteNeuf > 0 ? $", {Fraction("relance à contexte neuf", demande.RelancesContexteNeuf, limites.MaxRelancesRole)}" : "";
            parties.Add($"{passe}{relance}, tours {demande.ToursPasseConstruction}/{limites.MaxIterationsAgent} ({Pourcentage(demande.ToursPasseConstruction, limites.MaxIterationsAgent)} %)");
        }

        if (demande.Etape == EtapeDemande.Evaluation && demande.Evaluation is { ReverificationDemandee: true })
        {
            parties.Add($"revérification du QA après la {Fraction("correction des bloquants", demande.CorrectionsEvaluation, limites.MaxCorrectionsEvaluation)}");
        }

        parties.Add(Meilleur(demande) is (var reussis, var total)
            ? $"tests {reussis}/{total} ({Pourcentage(reussis, total)} %)"
            : "tests pas encore vérifiés");
        var cout = Consignes.FormaterCout(demande.CoutTotal, demande.CoutConnu);
        parties.Add(cout.StartsWith("coût", StringComparison.Ordinal) ? cout : $"coût {cout}");
        return $"{Prefixe(demande.Id)} Avancement : {string.Join(" ; ", parties)}";
    }

    public static IReadOnlyList<string>? Interpellation(Demande demande)
    {
        var entete = $"Demande {demande.Id} ({demande.Titre}{(demande.Projet is { } projet ? $", projet {projet}" : "")})";
        return demande.Statut switch
        {
            StatutDemande.AttenteReponses => [$"{entete} : le PO a besoin de tes réponses. Je te pose ses questions une par une."],
            StatutDemande.AttenteValidation => [$"{entete} : {AnnonceValidation(demande.Etape)}"],
            StatutDemande.Terminee =>
            [
                $"{entete} : livrée.",
                $"Tests : {DerniereVerification(demande)}.",
                $"Coût : {Consignes.FormaterCout(demande.CoutTotal, demande.CoutConnu)}.",
                .. demande.Livrable is { } livrable ? [$"Livrable : {livrable.RepertoireCode}"] : Array.Empty<string>(),
                .. demande.PublicationProjet is { } publication ? [publication.Message] : Array.Empty<string>(),
            ],
            StatutDemande.Echouee => [$"{entete} : échec à l'étape {demande.Etape} : {demande.Erreur}"],
            StatutDemande.Annulee => [$"{entete} : annulée."],
            _ => null,
        };
    }

    private Suivi Suivre(string id, long position)
    {
        var suivi = new Suivi(id, new LecteurJournal(depot.CheminJournal(id), Suffixe(id), position));
        suivies[id] = suivi;
        return suivi;
    }

    private void Observer(Suivi suivi, Demande demande)
    {
        var cle = (demande.Statut, demande.Etape, demande.Evaluation?.ReverificationDemandee == true);
        var meilleur = Meilleur(demande);
        var maintenant = horloge.GetUtcNow();
        var nouvelleEtape = suivi.Cle != cle;
        if (nouvelleEtape || suivi.Meilleur != meilleur
            || (demande.MisAJourLe != suivi.MisAJourLe && maintenant - suivi.DernierAvancement >= IntervalleAvancement))
        {
            ecrire(Avancement(demande));
            suivi.DernierAvancement = maintenant;
            suivi.MisAJourLe = demande.MisAJourLe;
        }

        if (nouvelleEtape && Interpellation(demande) is { } lignes)
        {
            if (demande.Statut == StatutDemande.Annulee)
            {
                ecrire(lignes[0]);
            }
            else
            {
                ecrire(Separateur);
                foreach (var ligne in lignes)
                {
                    ecrire(ligne);
                }

                ecrire(Separateur);
            }

            informations.Ajouter(string.Join("\n", lignes));
            if (RevueGuidee.AttendLeDemandeur(demande))
            {
                revues?.Ajouter(demande.Id);
            }
        }

        suivi.Cle = cle;
        suivi.Meilleur = meilleur;
    }

    internal static IEnumerable<string> Identifiants(DepotDemandes depot)
    {
        try
        {
            return Directory.EnumerateDirectories(depot.RepertoireDemandes)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(DepotDemandes.EstIdValide)
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static Demande? Charger(DepotDemandes depot, string id)
    {
        try
        {
            return depot.Charger(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DemandeIntrouvableException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private DateTime DateEtat(string id)
    {
        try
        {
            return File.GetLastWriteTimeUtc(depot.CheminEtat(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private string Prefixe(string id) => string.Create(CultureInfo.InvariantCulture, $"{horloge.GetLocalNow():HH:mm:ss} {Suffixe(id)}");

    internal static string Suffixe(string id) => id.Length > 4 ? id[^4..] : id;

    private static (int Reussis, int Total)? Meilleur(Demande demande) =>
        demande.Verifications.MaxBy(v => v.Reussis) is { } meilleure ? (meilleure.Reussis, meilleure.Total) : null;

    private static int Pourcentage(int valeur, int total) => total > 0 ? valeur * 100 / total : 0;

    private static string Fraction(string libelle, int valeur, int total) => $"{libelle} {valeur}/{total} ({Pourcentage(valeur, total)} %)";

    private static string AnnonceValidation(EtapeDemande etape) => etape switch
    {
        EtapeDemande.ValidationBesoin => "besoin compris par le PO à valider. Je te le présente.",
        EtapeDemande.ValidationBacklog => "backlog à valider. Je te le présente story par story.",
        EtapeDemande.ValidationTests => "tests à valider. Je te les présente story par story.",
        EtapeDemande.ValidationLivraison => "livraison à valider. Je te la présente.",
        _ => $"{etape} à valider.",
    };

    internal static string DerniereVerification(Demande demande) => demande.Verifications.LastOrDefault() switch
    {
        null => "aucune vérification",
        { Succes: true } derniere => $"{derniere.Reussis}/{derniere.Total} réussis",
        var derniere => $"{derniere.Reussis}/{derniere.Total} réussis, en échec",
    };

    private sealed record Ecartee(DateTime DateEtat, long TailleJournal, bool Lue);

    private sealed class Suivi(string id, LecteurJournal journal)
    {
        public string Id { get; } = id;

        public LecteurJournal Journal { get; } = journal;

        public (StatutDemande, EtapeDemande, bool)? Cle { get; set; }

        public (int Reussis, int Total)? Meilleur { get; set; }

        public DateTimeOffset DernierAvancement { get; set; }

        public DateTimeOffset MisAJourLe { get; set; }
    }
}
