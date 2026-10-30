using System.Security.Cryptography;
using System.Text.Json;

namespace Usine.Core.Etat;

public sealed class DepotDemandes
{
    private const int MaxEvenements = 200;

    private const UnixFileMode DroitsPourLeConteneur =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private readonly TimeProvider temps;

    public DepotDemandes(string racine, string repertoireProjets, TimeProvider? temps = null)
    {
        Racine = Path.GetFullPath(racine);
        RepertoireProjets = Path.GetFullPath(repertoireProjets);
        this.temps = temps ?? TimeProvider.System;
        Directory.CreateDirectory(RepertoireDemandes);
    }

    public string Racine { get; }

    public string RepertoireDemandes => Path.Combine(Racine, "demandes");

    public string RepertoireProjets { get; }

    public string Repertoire(string id) => Path.Combine(RepertoireDemandes, VerifierId(id));

    public string CheminEtat(string id) => Path.Combine(Repertoire(id), "etat.json");

    public string CheminJournal(string id) => Path.Combine(Repertoire(id), "journal.log");

    public string CheminAnnulation(string id) => Path.Combine(Repertoire(id), "annulation.demandee");

    public string CheminVerrouWorker(string id) => Path.Combine(Repertoire(id), "worker.pid");

    public string CheminLancementWorker(string id) => Path.Combine(Repertoire(id), "worker.lancement");

    public string RepertoireAcceptation(string id) => Path.Combine(Repertoire(id), "acceptance");

    public string RepertoireLivrable(string id) => Path.Combine(Repertoire(id), "livrable");

    public string RepertoireTravailPartiel(string id) => Path.Combine(RepertoireLivrable(id), "code-partiel");

    public string RepertoireConversations(string id) => Path.Combine(Repertoire(id), "conversations");

    public Demande Creer(string texte, string? depotSource, bool modeAuto, string? titre, string? projet = null, bool cadrageParDemandeur = false)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            throw new ArgumentException("La demande est vide.", nameof(texte));
        }

        var maintenant = temps.GetUtcNow();
        var id = $"{maintenant:yyyyMMdd-HHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()}";
        var demande = new Demande
        {
            Id = id,
            Texte = texte.Trim(),
            Titre = string.IsNullOrWhiteSpace(titre) ? Resumer(texte) : titre.Trim(),
            TitreImpose = !string.IsNullOrWhiteSpace(titre),
            DepotSource = string.IsNullOrWhiteSpace(depotSource) ? null : Path.GetFullPath(depotSource),
            Projet = projet,
            ModeAuto = modeAuto,
            CadrageParDemandeur = modeAuto && cadrageParDemandeur,
            CreeLe = maintenant,
            MisAJourLe = maintenant,
        };
        Directory.CreateDirectory(Repertoire(id));
        CreerRepertoireAcceptation(RepertoireAcceptation(id));
        AjouterEvenement(demande, "Usine", "Demande créée.");
        Enregistrer(demande);
        return demande;
    }

    public bool Existe(string id) => EstIdValide(id) && File.Exists(CheminEtat(id));

    public Demande Charger(string id)
    {
        var chemin = CheminEtat(id);
        if (!File.Exists(chemin))
        {
            throw new DemandeIntrouvableException(id);
        }

        return JsonSerializer.Deserialize<Demande>(LireAvecReessais(chemin), Json.Options)
            ?? throw new InvalidOperationException($"L'état de la demande {id} est illisible.");
    }

    public void Enregistrer(Demande demande)
    {
        demande.MisAJourLe = temps.GetUtcNow();
        if (demande.Evenements.Count > MaxEvenements)
        {
            demande.Evenements.RemoveRange(0, demande.Evenements.Count - MaxEvenements);
        }

        var chemin = CheminEtat(demande.Id);
        var temporaire = $"{chemin}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaire, JsonSerializer.Serialize(demande, Json.Options));
            for (var essai = 1; ; essai++)
            {
                try
                {
                    File.Move(temporaire, chemin, overwrite: true);
                    return;
                }
                catch (Exception ex) when (EstConflitDAcces(ex) && essai < 10)
                {
                    Thread.Sleep(50 * essai);
                }
            }
        }
        finally
        {
            File.Delete(temporaire);
        }
    }

    public IReadOnlyList<Demande> Lister()
    {
        if (!Directory.Exists(RepertoireDemandes))
        {
            return [];
        }

        var demandes = new List<Demande>();
        foreach (var repertoire in Directory.EnumerateDirectories(RepertoireDemandes))
        {
            var id = Path.GetFileName(repertoire);
            if (Existe(id))
            {
                demandes.Add(Charger(id));
            }
        }

        return demandes.OrderByDescending(d => d.CreeLe).ToList();
    }

    public void AjouterEvenement(Demande demande, string acteur, string message)
    {
        var numero = demande.Evenements.Count == 0 ? 1 : demande.Evenements[^1].Numero + 1;
        var evenement = new Evenement { Numero = numero, Date = temps.GetUtcNow(), Acteur = acteur, Message = message };
        demande.Evenements.Add(evenement);
        Journaliser(demande.Id, $"[{acteur}] {message}");
    }

    public void Journaliser(string id, string ligne)
    {
        var chemin = CheminJournal(id);
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        for (var essai = 1; ; essai++)
        {
            try
            {
                File.AppendAllText(chemin, $"{temps.GetUtcNow().ToLocalTime():yyyy-MM-dd HH:mm:ss} {ligne}{Environment.NewLine}");
                return;
            }
            catch (Exception ex) when (EstConflitDAcces(ex) && essai < 10)
            {
                Thread.Sleep(20 * essai);
            }
        }
    }

    public bool AnnulationDemandee(string id) => File.Exists(CheminAnnulation(id));

    public void DemanderAnnulation(string id) => File.WriteAllText(CheminAnnulation(id), temps.GetUtcNow().ToString("O"));

    public void RetirerAnnulation(string id) => File.Delete(CheminAnnulation(id));

    public static void CreerRepertoireAcceptation(string repertoire)
    {
        var tests = Path.Combine(repertoire, "tests");
        Directory.CreateDirectory(tests);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(repertoire, DroitsPourLeConteneur);
            File.SetUnixFileMode(tests, DroitsPourLeConteneur);
        }
    }

    public static bool EstIdValide(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 64
        && id.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c) || c is '-' or '_');

    private static string VerifierId(string id) =>
        EstIdValide(id) ? id : throw new ArgumentException($"Identifiant de demande invalide : « {id} ».", nameof(id));

    private static string LireAvecReessais(string chemin)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                return File.ReadAllText(chemin);
            }
            catch (Exception ex) when (EstConflitDAcces(ex) && essai < 10)
            {
                Thread.Sleep(50 * essai);
            }
        }
    }

    private static bool EstConflitDAcces(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException && ex is not FileNotFoundException and not DirectoryNotFoundException);

    private static string Resumer(string texte)
    {
        var ligne = texte.Trim().Split('\n', 2)[0].Trim();
        return ligne.Length <= 60 ? ligne : ligne[..57].TrimEnd() + "…";
    }
}

public sealed class DemandeIntrouvableException(string id) : Exception($"Aucune demande avec l'identifiant « {id} ».");
