using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usine.Core.Etat;

public enum EtapeDemande
{
    Cadrage,
    Questions,
    ValidationBesoin,
    ValidationBacklog,
    TestsAcceptation,
    ValidationTests,
    Construction,
    ArbitrageTests,
    Verification,
    Evaluation,
    Livraison,
    ValidationLivraison,
    Terminee,
}

public enum StatutDemande
{
    EnCours,
    AttenteReponses,
    AttenteValidation,
    Terminee,
    Echouee,
    Annulee,
}

public enum NaturePasse
{
    Ordinaire,
    BloquantsQa,
    EcartsSonde,
}

public sealed class Demande
{
    public required string Id { get; init; }

    public required string Texte { get; init; }

    public string Titre { get; set; } = "";

    public bool TitreImpose { get; init; }

    public string? DepotSource { get; init; }

    public string? ReevaluationDe { get; set; }

    public bool NouveauxTests { get; set; }

    public string? Projet { get; set; }

    public PublicationProjet? PublicationProjet { get; set; }

    public bool ModeAuto { get; init; }

    public bool CadrageParDemandeur { get; init; }

    public DateTimeOffset CreeLe { get; init; }

    public DateTimeOffset MisAJourLe { get; set; }

    public EtapeDemande Etape { get; set; } = EtapeDemande.Cadrage;

    public StatutDemande Statut { get; set; } = StatutDemande.EnCours;

    public List<string> Questions { get; set; } = [];

    public List<EchangeQuestions> Echanges { get; set; } = [];

    public Backlog? Backlog { get; set; }

    public List<string> CommentairesBacklog { get; set; } = [];

    public Besoin? Besoin { get; set; }

    public bool BesoinValide { get; set; }

    public List<string> CommentairesBesoin { get; set; } = [];

    public List<string> CalculsCorriges { get; set; } = [];

    public List<string> CommentairesTests { get; set; } = [];

    public List<string> CommentairesLivraison { get; set; } = [];

    public string? CommentaireApprobationLivraison { get; set; }

    public Dictionary<int, List<string>> TestsAjoutesAuxRefus { get; set; } = new();

    public TestsAcceptation? Tests { get; set; }

    public List<VoteNotion> VotesNotions { get; set; } = [];

    public List<string>? PdfDeposes { get; set; }

    public int Corrections { get; set; }

    public int CorrectionsEvaluation { get; set; }

    public int CorrectionsSignaux { get; set; }

    public bool DernierVertRestaure { get; set; }

    public bool RestaurationLancee { get; set; }

    public bool VersionRefuseeRestauree { get; set; }

    public List<string> EmpreintesCodeRefuse { get; set; } = [];

    public bool LivraisonIdentiqueRefusee { get; set; }

    public List<string> DependancesNonImporteesSignalees { get; set; } = [];

    public List<string> RestesDebogageSignales { get; set; } = [];

    public List<string> SignauxSignales { get; set; } = [];

    public List<string> TestsInstables { get; set; } = [];

    public List<ResultatsVersion> ResultatsParVersion { get; set; } = [];

    public ContestationTests? ContestationTests { get; set; }

    public bool DernierVertNonEvalue { get; set; }

    public int? MeilleureTentative { get; set; }

    public int VerificationsAvantTestsAjoutes { get; set; }

    public string? RetourPourDev { get; set; }

    public int ToursPasseConstruction { get; set; }

    public NaturePasse NaturePasse { get; set; }

    public bool BloquantsQaDansLaPasse { get; set; }

    public string? EmpreinteDebutPasse { get; set; }

    public int RelancesContexteNeuf { get; set; }

    public List<ResultatOracle> Verifications { get; set; } = [];

    public Evaluation? Evaluation { get; set; }

    public Evaluation? EvaluationDernierVert { get; set; }

    public bool EvaluationAnterieureAuDernierVert { get; set; }

    public bool EvaluationSansAvis { get; set; }

    public List<CoutRole> Couts { get; set; } = [];

    public List<Evenement> Evenements { get; set; } = [];

    public Livrable? Livrable { get; set; }

    public string? Erreur { get; set; }

    [JsonIgnore]
    public decimal CoutTotal => Couts.Sum(c => c.Cout);

    [JsonIgnore]
    public bool CoutConnu => Couts.All(c => c.CoutConnu);

    [JsonIgnore]
    public bool EstTerminale => Statut is StatutDemande.Terminee or StatutDemande.Echouee or StatutDemande.Annulee;

    [JsonIgnore]
    public bool EstProjetExistant => !string.IsNullOrWhiteSpace(DepotSource);

    [JsonIgnore]
    public bool CadrageHumain => !ModeAuto || CadrageParDemandeur;

    [JsonIgnore]
    public IEnumerable<string> TextesDuBesoin => Echanges.Select(e => e.Reponses).Prepend(Texte);
}

public sealed class EchangeQuestions
{
    public List<string> Questions { get; set; } = [];

    public string Reponses { get; set; } = "";
}

public sealed class Besoin
{
    [Description("Titre court de la demande (quelques mots).")]
    public string Titre { get; set; } = "";

    [Description("Le besoin tel que tu l'as compris, reformulé en détail, sans solution technique.")]
    public string Resume { get; set; } = "";
}

public sealed class Backlog
{
    [Description("Titre court de la demande (quelques mots).")]
    public string Titre { get; set; } = "";

    [Description("Résumé de ce qui sera livré, en 2 à 4 phrases.")]
    public string Resume { get; set; } = "";

    [Description("« nouveau » pour une application créée de zéro, « existant » pour une évolution d'un projet existant.")]
    public string Mode { get; set; } = "nouveau";

    [Description("Choix technique et sa justification.")]
    public ChoixStack Stack { get; set; } = new();

    [Description("User stories, chacune avec ses critères d'acceptation vérifiables en boîte noire.")]
    public List<Story> Stories { get; set; } = [];

    [Description("Ce qui est explicitement exclu du périmètre.")]
    public List<string> HorsPerimetre { get; set; } = [];

    [Description("Hypothèses prises faute d'information.")]
    public List<string> Hypotheses { get; set; } = [];

    [Description("Au plus 3 notions de la demande qui ont une frontière ou une portée (un mot, un nombre, chaque utilisateur, le dernier envoi, une limite chiffrée…), les plus risquées d'abord. Vide s'il n'y en a aucune.")]
    public List<Notion> Notions { get; set; } = [];

    [Description("S'il n'y a aucune notion : la phrase qui le dit. Vide sinon.")]
    public string SansNotions { get; set; } = "";
}

public sealed class Notion
{
    [Description("Identifiant de la notion : N1, N2 ou N3.")]
    public string Id { get; set; } = "";

    [Description("Nom court de la notion, par exemple « un mot ».")]
    public string Nom { get; set; } = "";

    [Description("Phrase de la demande d'où vient la notion, recopiée telle quelle.")]
    public string Phrase { get; set; } = "";
}

public sealed class ChoixStack
{
    [Description("Technologies retenues, par exemple « HTML/CSS statique servi par Node » ou « API .NET 10 + Angular + SQLite ».")]
    public string Choix { get; set; } = "";

    [Description("Pourquoi ce choix convient à la demande.")]
    public string Justification { get; set; } = "";
}

public sealed class Story
{
    [Description("Identifiant de la story : S1, S2, S3…")]
    public string Id { get; set; } = "";

    [Description("Titre de la story.")]
    public string Titre { get; set; } = "";

    [Description("En tant que…, je veux…, afin de…")]
    public string Description { get; set; } = "";

    [Description("Critères d'acceptation, identifiants S1-C1, S1-C2…")]
    public List<Critere> Criteres { get; set; } = [];
}

public sealed class Critere
{
    [Description("Identifiant du critère : <idStory>-C<n>, par exemple S1-C1.")]
    public string Id { get; set; } = "";

    [Description("Critère au format Étant donné… Quand… Alors…, observable depuis le navigateur ou par HTTP.")]
    public string Texte { get; set; } = "";
}

public sealed class TestsAcceptation
{
    public string Resume { get; set; } = "";

    public List<string> Fichiers { get; set; } = [];

    public List<string> Titres { get; set; } = [];

    public string Empreinte { get; set; } = "";

    public ResultatOracle? ExecutionInitiale { get; set; }

    public List<TestFrontiere> Frontieres { get; set; } = [];
}

public sealed class TestFrontiere
{
    [Description("Identifiant du test de frontière, tel qu'il figure entre crochets dans son titre : F1, F2 ou F3.")]
    public string Test { get; set; } = "";

    [Description("Phrase de la demande ou du backlog d'où vient la notion que ce test éprouve, recopiée telle quelle.")]
    public string Phrase { get; set; } = "";

    [Description("Identifiant de la notion du backlog que ce test éprouve (N1, N2 ou N3) ; vide pour une autre notion.")]
    public string Notion { get; set; } = "";

    [Description("Pour une notion du backlog : un exemple discriminant, cas concret court où les lectures possibles de la notion divergent (par exemple une valeur à la limite, ou une partie contre le tout). Le cas seul, sans ton verdict, ni ta règle, ni justification : les avis le jugent tel quel.")]
    public string Exemple { get; set; } = "";

    [Description("Pour une notion du backlog : ton verdict pour cet exemple selon la règle écrite dans CONTRAT.md, true s'il fait partie de la notion, false sinon.")]
    public bool? Inclus { get; set; }
}

public sealed class VoteNotion
{
    public string NomNotion { get; set; } = "";

    public string Phrase { get; set; } = "";

    public string Exemple { get; set; } = "";

    public bool Inclus { get; set; }

    public int Contraires { get; set; }

    public string EmpreinteRegleEtTest { get; set; } = "";
}

public sealed class ResultatOracle
{
    public DateTimeOffset Date { get; set; }

    public bool Succes { get; set; }

    public string? EtapeEchec { get; set; }

    public int Total { get; set; }

    public int Reussis { get; set; }

    public int Echoues { get; set; }

    public List<EchecTest> Echecs { get; set; } = [];

    [JsonIgnore]
    public List<string> TitresReussis { get; set; } = [];

    [JsonIgnore]
    public List<string> TitresSautes { get; set; } = [];

    public string? Journal { get; set; }

    public bool EmpreinteValide { get; set; } = true;

    public double DureeSecondes { get; set; }

    public List<SignalOracle> Signaux { get; set; } = [];

    public List<string> Instables { get; set; } = [];

    public List<string> Dependants { get; set; } = [];

    public List<string> EchouentSeuls { get; set; } = [];
}

public sealed class ContestationTests
{
    public List<string> Titres { get; set; } = [];

    public string Motif { get; set; } = "";

    public string Preuve { get; set; } = "";

    public List<string> Dependants { get; set; } = [];

    public string Echec { get; set; } = "";

    public bool ParLUsine { get; set; }

    public bool? Reecrits { get; set; }

    public string Decision { get; set; } = "";
}

public sealed class ResultatsVersion
{
    public string EmpreinteCode { get; set; } = "";

    public bool Essai { get; set; }

    public List<string> Reussis { get; set; } = [];

    public List<string> Echoues { get; set; } = [];
}

public sealed class SignalOracle
{
    public string Cle { get; set; } = "";

    public string Message { get; set; } = "";
}

public sealed class EchecTest
{
    public string Titre { get; set; } = "";

    public string Erreur { get; set; } = "";

    public string? Page { get; set; }

    public string? Console { get; set; }
}

public sealed class Evaluation
{
    [JsonConverter(typeof(ConvertisseurBloquants))]
    public List<Bloquant> Bloquants { get; set; } = [];

    public List<string> Remarques { get; set; } = [];

    public string Synthese { get; set; } = "";

    public ControlesSoumis? Controles { get; set; }

    public string NonTeste { get; set; } = "";

    public bool TransmiseAuDev { get; set; }

    public bool ReverificationDemandee { get; set; }

    public List<Reverification> Reverifications { get; set; } = [];
}

public sealed class Reverification
{
    public bool Rendue { get; set; }

    public int Bloquants { get; set; }

    public List<string> Remarques { get; set; } = [];

    public string Synthese { get; set; } = "";

    public ControlesSoumis? Controles { get; set; }

    public string NonTeste { get; set; } = "";
}

public sealed class Bloquant
{
    public string Attendu { get; set; } = "";

    public string Constat { get; set; } = "";

    public string? Script { get; set; }

    public string? Copie { get; set; }

    public bool? Corrige { get; set; }

    public string? ErreurRejeu { get; set; }

    public bool ReleveALaReverification { get; set; }

    [JsonIgnore]
    public string Texte => Attendu.Length == 0 ? Constat : $"{Attendu} (constaté : {Constat})";

    [JsonIgnore]
    public bool ARejouer => Copie is not null && Corrige is null;
}

public sealed class EvaluationSoumise
{
    [Description("Problèmes qui empêchent de considérer la demande comme satisfaite (critère non respecté, plantage, page cassée), chacun avec son script de reproduction. Liste vide si aucun.")]
    public List<BloquantSoumis> Bloquants { get; set; } = [];

    [Description("Problèmes non bloquants constatés : ni compliment, ni apparence ou ergonomie dont la demande ne parle pas.")]
    public List<string> Remarques { get; set; } = [];

    [Description("Ce que tu as testé, sans qualificatifs, en 2 à 4 phrases.")]
    public string Synthese { get; set; } = "";

    [Description("Statut de chaque point de la liste de contrôle de ta consigne.")]
    public ControlesSoumis Controles { get; set; } = new();

    [Description("Ce que tu n'as pas pu ou pas eu le temps de tester en dehors de la liste de contrôle, sans qualificatifs ; « rien » sinon. Les points non faits de la liste de contrôle y sont ajoutés par l'usine.")]
    public string NonTeste { get; set; } = "";
}

public sealed class ControlesSoumis
{
    [Description("Deux contextes de navigateur ouverts en même temps, avec des entrées différentes.")]
    public ControleSoumis DeuxContextes { get; set; } = new();

    [Description("Deux onglets d'un même contexte, quand l'état est propre à un document ou à une vue.")]
    public ControleSoumis DeuxOnglets { get; set; } = new();

    [Description("Parcours principal refait avec une deuxième entrée valide.")]
    public ControleSoumis DeuxiemeEntree { get; set; } = new();

    [Description("Frontière de chaque notion que nomme la demande, avec une valeur incluse dans une autre ou partielle ; « fait » exige au moins un essai.")]
    public ControleFrontieres Frontieres { get; set; } = new();

    [Description("Double soumission rapide de deux entrées valides différentes, l'une juste après l'autre.")]
    public ControleDoubleSoumission DoubleSoumission { get; set; } = new();

    [Description("Vraie cause de chaque message affiché à l'utilisateur, d'information comme d'erreur, et de chaque branche d'erreur visible dans le code lu.")]
    public ControleSoumis CausesDesMessages { get; set; } = new();

    public IReadOnlyList<(string Point, ControleSoumis Controle)> Points() =>
    [
        ("deux contextes avec des entrées différentes", DeuxContextes),
        ("deux onglets", DeuxOnglets),
        ("deuxième entrée valide", DeuxiemeEntree),
        ("frontière des notions de la demande", Frontieres),
        ("double soumission rapide", DoubleSoumission),
        ("vraie cause de chaque message vu", CausesDesMessages),
    ];
}

public class ControleSoumis
{
    [Description("« fait », « non fait » ou « sans objet ».")]
    public string Statut { get; set; } = "";

    [Description("Fait : ce que tu as observé, en une phrase. Non fait ou sans objet : la raison.")]
    public string Explication { get; set; } = "";
}

public sealed class ControleFrontieres : ControleSoumis
{
    [Description("Chaque valeur essayée à une frontière, avec ce que la demande fait attendre et ce que l'application a donné.")]
    [JsonPropertyOrder(1)]
    public List<EssaiFrontiere> Essais { get; set; } = [];
}

public sealed class EssaiFrontiere
{
    [Description("Valeur essayée, incluse dans une autre ou partielle, recopiée telle quelle de ta commande.")]
    public string Valeur { get; set; } = "";

    [Description("Ce qu'il faut obtenir d'après la demande seule, pas d'après l'application, écrit dans les mêmes termes que l'obtenu.")]
    public string Attendu { get; set; } = "";

    [Description("Ce que l'application a affiché ou renvoyé, recopié tel quel de ta commande ou de sa sortie.")]
    public string Obtenu { get; set; } = "";

    [Description("« bloquant n°k » ou « remarque n°k » de cette évaluation qui relève l'écart entre l'attendu et l'obtenu ; vide quand ils concordent.")]
    public string Suite { get; set; } = "";
}

public sealed class ControleDoubleSoumission : ControleSoumis
{
    [Description("Fait : la première des deux entrées valides envoyées. Sinon : vide.")]
    [JsonPropertyOrder(1)]
    public string PremiereEntree { get; set; } = "";

    [Description("Fait : la seconde entrée valide, différente de la première, envoyée juste après elle. Sinon : vide.")]
    [JsonPropertyOrder(1)]
    public string SecondeEntree { get; set; } = "";
}

public sealed class BloquantSoumis
{
    [Description("Comportement attendu, formulé comme une règle d'usage tirée de la demande ou du backlog (par exemple « chaque fenêtre ouverte cherche dans le document qu'elle affiche »), sans parler du code.")]
    public string Attendu { get; set; } = "";

    [Description("Ce que tu as fait et ce que tu as observé.")]
    public string Constat { get; set; } = "";

    [Description("Chemin du script de reproduction que tu as écrit sous /tmp : un .js exécuté par Node (Playwright disponible) ou un .sh exécuté par bash (avec curl par exemple), autonome, qui lit l'adresse de l'application dans USINE_URL, attend l'état attendu sans pause fixe, et se termine avec le code 0 quand le comportement attendu est respecté, avec le code 1 quand il ne l'est pas ; tout autre code, une exception non attrapée ou un délai dépassé est une erreur du script.")]
    public string Script { get; set; } = "";
}

public sealed class ConvertisseurBloquants : JsonConverter<List<Bloquant>>
{
    public override List<Bloquant> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Liste de bloquants attendue.");
        }

        var bloquants = new List<Bloquant>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            bloquants.Add(reader.TokenType == JsonTokenType.String
                ? new Bloquant { Constat = reader.GetString() ?? "" }
                : JsonSerializer.Deserialize<Bloquant>(ref reader, options) ?? throw new JsonException("Bloquant illisible."));
        }

        return bloquants;
    }

    public override void Write(Utf8JsonWriter writer, List<Bloquant> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

public sealed class CoutRole
{
    public string Role { get; set; } = "";

    public string Modele { get; set; } = "";

    public long TokensEntree { get; set; }

    public long? TokensCache { get; set; }

    public long TokensSortie { get; set; }

    public decimal Cout { get; set; }

    public bool CoutConnu { get; set; } = true;

    public int Appels { get; set; }

    public int AppelsAvis { get; set; }
}

public sealed class Evenement
{
    public long Numero { get; set; }

    public DateTimeOffset Date { get; set; }

    public string Acteur { get; set; } = "";

    public string Message { get; set; } = "";
}

public sealed class PublicationProjet
{
    public bool Integree { get; set; }

    public DateTimeOffset Date { get; set; }

    public string Message { get; set; } = "";
}

public sealed class Livrable
{
    public string RepertoireCode { get; set; } = "";

    public string Rapport { get; set; } = "";

    public bool Succes { get; set; }

    public string? EmpreinteCode { get; set; }
}
