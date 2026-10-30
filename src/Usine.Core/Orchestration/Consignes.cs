using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Usine.Core.Configuration;
using Usine.Core.Etat;
using Usine.Core.Oracle;

namespace Usine.Core.Orchestration;

public sealed partial class Consignes(ConfigUsine config)
{
    public const string EvaluationAnterieure =
        "Cette évaluation porte sur la version précédente : le Dev a été relancé pour corriger les bloquants ci-dessous, et la version livrée, vérifiée par l'oracle, n'a pas été réévaluée par le QA.";

    public const string SansAvisQa = "Livraison sans avis du QA.";

    public const string ApplicationArreteeAuRejeu = "l'application ne démarre pas";

    public const string PointsSecondaires = "Points secondaires relevés par le QA : corrige-les s'ils sont simples et sans risque ; en tout cas, ne les aggrave pas. Si une remarque contredit un test d'acceptation verrouillé, ignore-la : les tests font foi.";

    private const string ConsigneBloquantsQa = "Les tests d'acceptation passent déjà : ils ne détectent pas ces problèmes. Corrige chacun d'eux dans le code, ou explique dans le résumé de terminer_construction pourquoi tu le contestes, puis relance lancer_tests_acceptation pour vérifier que rien n'est cassé.";

    public const string ConsigneScriptsQa = "Pendant cette correction, lancer_tests_acceptation rejoue aussi chaque script de reproduction après les tests d'acceptation, application démarrée, et te dit s'il reproduit encore le défaut, s'il passe ou s'il est en erreur : tu n'as pas à le lancer toi-même.";

    public const string RepriseContexteNeuf = "Une de tes sessions précédentes sur cette passe a été arrêtée par la limite de contexte, avant sa conclusion. Le code déjà écrit est dans /workspace : relis seulement ce dont tu as besoin, sans tout refaire, puis termine le travail.";

    public const string RetourAvantRelance = "Le retour « À corriger en priorité » ci-dessus date d'avant la session arrêtée : commence par lancer_tests_acceptation pour connaître l'état actuel, puis poursuis.";

    public const int BudgetRetourDev = 8000;

    public const string PromptAvisNotion = "Tu donnes un avis indépendant sur le sens d'une notion d'une demande de logiciel. Lis la demande, la phrase citée et l'exemple, puis réponds à la question en appelant donner_avis : true si l'exemple fait partie de la notion au sens de la phrase citée, false sinon. Tranche d'après ce que dit la phrase, lue dans la demande, sans supposer de règle qu'elle ne donne pas.";

    public const string MemeEchec = "Même échec qu'à la passe précédente, sur les mêmes tests, avec la même erreur : ta piste ne mène pas au but. Change d'approche : autre méthode, autre bibliothèque, ou relis le test et la sortie du serveur ci-dessous.";

    internal const string ViolationModeStrict = "strict mode violation";

    private const int MaxLignesSignaux = 8;

    private const int LongueurSignal = 400;

    private const string RegleSaisiePo = "   - La demande fait saisir un nombre : écris dans les hypothèses les saisies acceptées (signe, décimales, espaces autour ; virgule décimale acceptée comme le point ou refusée) et les saisies refusées avec un message (vide, lettres, nombre suivi de lettres comme « 12abc »), et que le résultat est vide quand le message s'affiche.";

    private const string RegleArrondiPo = "   - La demande fixe un arrondi : écris dans les hypothèses la règle (arrondi au plus proche de la valeur exacte, un zéro s'affiche sans signe) et le format du résultat (nombre de décimales, séparateur décimal). Ne choisis pas d'exemple pile au milieu ni proche de zéro : l'usine vérifie elle-même ces cas sur l'application.";

    private const string ConsigneHypothesesApresBacklog = " Le demandeur ne relira que le résumé et les stories : tout choix qui change ce que l'utilisateur voit (arrondi, format d'un résultat, saisies acceptées ou refusées, message affiché) est un critère avec un exemple vérifiable, jamais une simple hypothèse ; les hypothèses ne gardent que ce qui ne se voit pas.";

    private const string ConsigneSaisieDev = "- Une saisie de nombre se valide en entier, après retrait des espaces de bord : expression régulière sur toute la chaîne, ou conversion stricte qui échoue s'il reste des caractères. Jamais `parseFloat`, `parseInt` ou `float()` seuls : « 12abc », « 0x10 », « 12 34 » et « 1e400 » sont refusés comme une saisie invalide, sauf si le contrat les autorise. Une virgule décimale est acceptée comme un point ou refusée, jamais tronquée.";

    private const string ConsigneArrondiDev = "- Arrondi : un calcul en virgule flottante tombe parfois juste sous le milieu (1.7499999999999991 au lieu de 1.75). Ramène d'abord le résultat à 12 chiffres significatifs, puis arrondis au plus proche. JavaScript : `function arrondir(x, d) { const m = 10 ** d; const r = Math.round(Math.abs(Number((x * m).toPrecision(12)))) / m; return x < 0 && r !== 0 ? -r : r; }`, puis `toFixed(d)`. Python : `Decimal(repr(round(x, 12))).quantize(Decimal(1).scaleb(-d), ROUND_HALF_UP) + 0`. C# : `Math.Round(decimal.Parse(x.ToString(\"G12\", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture), d, MidpointRounding.AwayFromZero)`, puis `ToString(\"F\" + d, CultureInfo.InvariantCulture)`. Jamais `toFixed`, `round()` ni `(decimal)x` directement sur le résultat flottant. Un zéro s'affiche sans signe.";

    private const string ConsigneContestationDev = "8. Personne ne relit tes résumés dans cette demande : un test que tu estimes faux ne se signale pas par terminer_construction. Quand les seuls tests encore rouges, deux au plus, contredisent CONTRAT.md, le backlog ou la demande, ou que l'oracle les a trouvés dépendants de l'état laissé par les autres tests, appelle `contester_tests` avec leurs titres exacts, ton motif et ta preuve (la ligne du test et celle du contrat, du backlog ou de la demande qu'elle contredit, ou le constat de l'oracle) : le QA amont réécrit ces tests ou maintient son refus, puis l'oracle vérifie ton code. Une seule contestation par demande : corrige d'abord tout ce qui relève de ton code.";

    private ConfigLimites Limites => config.Limites;

    public string PromptPoBesoin(Demande demande) => Remplacer(Ressources.Lire("prompts/po-besoin.md"), new Dictionary<string, string>
    {
        ["CONTEXTE_DEPOT"] = ContexteDepotPo(demande),
        ["CONSIGNE_EXISTANT"] = demande.EstProjetExistant
            ? "Explore d'abord le projet (écrans, fonctions et données existants) pour comprendre ce que la demande ajoute ou change."
            : "C'est une application nouvelle.",
        ["CONSIGNE_OUTILS"] = demande.EstProjetExistant
            ? "Pour explorer : lister_fichiers, lire_fichier, rechercher, executer_commande (exploration uniquement)."
            : "Tu n'as aucun outil d'exploration : le répertoire est vide, il n'y a rien à regarder ni à installer. Tes seuls outils sont poser_questions et soumettre_besoin.",
        ["MAX_QUESTIONS"] = Limites.MaxQuestions is { } maximum ? $", {maximum.ToString(CultureInfo.InvariantCulture)} au plus par série" : "",
    });

    public string PromptPo(Demande demande) => Remplacer(Ressources.Lire("prompts/po.md"), new Dictionary<string, string>
    {
        ["CONTEXTE_DEPOT"] = ContexteDepotPo(demande),
        ["CONSIGNE_EXISTANT"] = demande.EstProjetExistant
            ? $"{(demande.NouveauxTests ? "Choisis" : "Explore d'abord le projet (structure, stack, conventions, commandes de construction et de démarrage, tests existants) pour que le backlog s'intègre au code existant, puis choisis")} mode = « existant ». Une exigence de non-régression du besoin (« tout ce qui existe déjà doit continuer à fonctionner », « sans casser l'existant ») donne **toujours au moins un critère dédié**, observable dans l'API et à l'écran : les fonctions existantes principales{(demande.NouveauxTests ? "" : " lues dans le projet")} (routes, codes de réponse, boutons, filtres) fonctionnent toujours, regroupées pour rester sous le maximum de critères, et les données déjà enregistrées par la version actuelle restent lisibles."
            : "Choisis mode = « nouveau ».",
        ["CONSIGNE_OUTILS"] = demande.EstProjetExistant && !demande.NouveauxTests
            ? "Pour explorer : lister_fichiers, lire_fichier, rechercher, executer_commande (exploration uniquement)."
            : $"Tu n'as aucun outil d'exploration{(demande.EstProjetExistant || demande.NouveauxTests ? "" : " : le répertoire est vide, il n'y a rien à regarder ni à installer")}. Tes seuls outils sont soumettre_backlog.",
        ["PORT"] = config.BacASable.Port.ToString(CultureInfo.InvariantCulture),
        ["MAX_STORIES"] = Limites.MaxStories.ToString(CultureInfo.InvariantCulture),
        ["MAX_CRITERES"] = Limites.MaxCriteres.ToString(CultureInfo.InvariantCulture),
        ["REGLES_SAISIE"] = ReglesSaisiePo(demande),
        ["CONSIGNE_HYPOTHESES"] = demande.CadrageParDemandeur ? ConsigneHypothesesApresBacklog : "",
    });

    public string MessagePo(Demande demande)
    {
        var texte = new StringBuilder();
        texte.AppendLine("## Demande").AppendLine().AppendLine(demande.Texte);
        foreach (var echange in demande.Echanges)
        {
            texte.AppendLine().AppendLine("## Questions posées").AppendLine();
            foreach (var question in echange.Questions)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {question}");
            }

            texte.AppendLine().AppendLine("## Réponses du demandeur").AppendLine().AppendLine(echange.Reponses);
        }

        if (demande.Besoin is { } besoin && demande.BesoinValide)
        {
            texte.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## Besoin validé par le demandeur : {besoin.Titre}").AppendLine();
            texte.AppendLine(besoin.Resume).AppendLine();
            texte.AppendLine("Tes stories et leurs critères couvrent tout ce besoin, sans en sortir. Le titre et le résumé du backlog seront ceux-ci.");
        }
        else if (demande.Besoin is { } refuse && demande.CommentairesBesoin.Count > 0)
        {
            texte.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## Besoin précédent, refusé : {refuse.Titre}").AppendLine();
            texte.AppendLine(refuse.Resume).AppendLine();
            texte.AppendLine("## Commentaires du demandeur à prendre en compte").AppendLine();
            foreach (var commentaire in demande.CommentairesBesoin)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {commentaire}");
            }
        }

        if (demande.CommentairesBacklog.Count > 0 && demande.Backlog is not null)
        {
            texte.AppendLine().AppendLine("## Backlog précédent, refusé").AppendLine().AppendLine(FormaterBacklog(demande.Backlog));
            texte.AppendLine("## Commentaires du validateur à prendre en compte").AppendLine();
            foreach (var commentaire in demande.CommentairesBacklog)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {commentaire}");
            }
        }

        return texte.ToString();
    }

    public string PromptQaAmont(Demande demande, bool nonRegression) => Remplacer(Ressources.Lire("prompts/qa-amont.md"), new Dictionary<string, string>
    {
        ["MAX_TESTS"] = Limites.MaxTests.ToString(CultureInfo.InvariantCulture),
        ["CONSIGNE_EXISTANT"] = demande.EstProjetExistant && !demande.NouveauxTests
            ? "- Le projet existant est dans /workspace (lecture seule). Lis-le pour connaître les routes et libellés existants, et réutilise-les quand le backlog en parle."
            : "",
        ["CONSIGNE_NON_REGRESSION"] = nonRegression
            ? "- Des tests de non-régression existent dans /acceptance/tests/non-regression : ce sont les tests d'acceptation des demandes précédentes sur ce projet, titres préfixés « [NR ». Ne les modifie pas, ne les supprime pas, n'écris rien dans ce dossier : ils s'exécuteront avec les tiens. Lis-les pour réutiliser les libellés et routes existants. Ils ne couvrent aucun critère du backlog actuel et ne comptent pas dans le maximum de tests : écris tes propres tests pour chaque critère."
            : "",
    });

    public string MessageQaAmont(Demande demande, string? motifNonAdoption)
    {
        var texte = new StringBuilder();
        texte.AppendLine("## Demande d'origine").AppendLine().AppendLine(demande.Texte).AppendLine();
        var backlog = Exiger(demande.Backlog, "backlog");
        texte.AppendLine(FormaterBacklog(backlog));
        if (demande.CalculsCorriges.Count > 0)
        {
            texte.AppendLine("## Calculs du backlog corrigés par l'usine").AppendLine();
            foreach (var correction in demande.CalculsCorriges)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {correction}");
            }

            texte.AppendLine();
        }

        if (demande.NouveauxTests)
        {
            texte.AppendLine("## Application déjà livrée").AppendLine();
            texte.AppendLine(CultureInfo.InvariantCulture, $"Tes tests réévaluent une application déjà livrée : /acceptance/CONTRAT.md décrit son interface. Garde ses routes, libellés et structures, et complète-le seulement pour tes nouveaux tests.{(backlog.Notions.Count > 0 ? " Écris-y la règle de chaque notion du backlog sous son identifiant, dans une ligne nouvelle, même si d'anciennes notions y figurent déjà : les lignes du contrat d'origine ne comptent pas." : "")}").AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(motifNonAdoption))
        {
            texte.AppendLine("## Tests déjà présents dans /acceptance/tests, non adoptés").AppendLine();
            texte.AppendLine(CultureInfo.InvariantCulture, $"Des tests étaient déjà là au début de cette étape. Ils n'ont pas été adoptés, pour ce motif : {motifNonAdoption}");
            texte.AppendLine().AppendLine("Corrige ce point sans réécrire ce qui est déjà correct, puis appelle soumettre_tests.").AppendLine();
        }

        if (demande.CommentairesTests.Count > 0)
        {
            texte.AppendLine("## Tests déjà écrits (dans /acceptance/tests), refusés : commentaires du validateur").AppendLine();
            foreach (var commentaire in demande.CommentairesTests)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {commentaire}");
            }
        }

        return texte.ToString();
    }

    public string PromptDev(Demande demande) => Remplacer(Ressources.Lire("prompts/dev.md"), new Dictionary<string, string>
    {
        ["PORT"] = config.BacASable.Port.ToString(CultureInfo.InvariantCulture),
        ["CONTEXTE_DEPOT"] = demande.EstProjetExistant
            ? "- Le projet existant est dans /workspace, sur une branche dédiée. Respecte sa stack et ses conventions, fais des changements minimaux, ne casse pas l'existant. Si usine-build.sh et usine-start.sh n'existent pas, crée-les à partir des commandes du projet. Si ton changement touche la forme des données enregistrées (nouvelle colonne, nouveau champ, nouvelle table), l'application doit, à son démarrage, mettre à niveau les données créées par la version précédente sans les perdre (par exemple ajouter la colonne si elle manque) : l'usine le vérifie en démarrant ta version sur les données de la version de départ."
            : "- C'est une application nouvelle : /workspace est à toi.",
        ["CONSIGNE_NON_REGRESSION"] = demande.Tests is { } tests && tests.Titres.Any(NonRegression.EstTitre)
            ? "- Les tests dont le titre contient « [NR » sont dans /acceptance/tests/non-regression : ce sont les tests d'acceptation des livraisons précédentes sur ce projet, et ils doivent continuer à passer. Un échec « [NR … » signale un comportement existant que tu as cassé et qu'il faut rétablir, pas un critère à implémenter : seuls les titres sans « NR » correspondent aux critères du backlog actuel, même quand ils portent le même identifiant."
            : "",
        ["CONSIGNES_CALCUL"] = Conditionnelles(demande, ConsigneSaisieDev, ConsigneArrondiDev),
        ["CONSIGNE_CONTESTATION"] = demande.ModeAuto ? ConsigneContestationDev : "",
    });

    public string MessageDev(Demande demande)
    {
        var texte = new StringBuilder();
        texte.AppendLine("## Demande d'origine").AppendLine().AppendLine(demande.Texte).AppendLine();
        texte.AppendLine(FormaterBacklog(Exiger(demande.Backlog, "backlog"), avecNotions: false));
        var tests = Exiger(demande.Tests, "tests d'acceptation");
        texte.AppendLine(CultureInfo.InvariantCulture, $"## Tests d'acceptation ({tests.Titres.Count} tests, dans /acceptance)").AppendLine();
        foreach (var titre in tests.Titres)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- {titre}");
        }

        if (demande.ContestationTests is { Reecrits: { } reecrits } contestation)
        {
            texte.AppendLine().AppendLine("## Contestation des tests").AppendLine();
            texte.AppendLine(CultureInfo.InvariantCulture, $"{(contestation.ParLUsine ? "L'usine a soumis d'office au QA amont ces tests au repère ambigu" : "Tu as contesté")} : {string.Join(" ; ", contestation.Titres)}. {(reecrits ? "Le QA amont les a réécrits, puis ils ont été verrouillés de nouveau : relis-les dans /acceptance" : "Le QA amont les maintient")}. {contestation.Decision}");
            texte.AppendLine(contestation.ParLUsine
                ? "La seule contestation de cette demande est faite : les tests d'acceptation actuels sont la référence."
                : "Ta seule contestation de cette demande est faite : les tests d'acceptation actuels sont la référence.");
        }

        if (!string.IsNullOrWhiteSpace(demande.RetourPourDev))
        {
            texte.AppendLine().AppendLine("## À corriger en priorité").AppendLine().AppendLine(demande.RetourPourDev);
            texte.AppendLine().AppendLine("Le code est toujours dans /workspace : pars de là.");
            if (demande.ToursPasseConstruction > 0 && demande.RelancesContexteNeuf == 0)
            {
                texte.AppendLine().AppendLine("## Reprise").AppendLine().AppendLine("Cette correction a déjà été commencée, puis interrompue avant sa vérification. Le retour ci-dessus décrit l'état d'avant la session interrompue : /workspace contient peut-être des changements faits depuis, et ce retour n'est peut-être plus à jour. Commence par lancer_tests_acceptation pour connaître l'état actuel, puis poursuis sans tout refaire.");
            }
        }
        else if (demande.RelancesContexteNeuf == 0 && demande.Couts.Any(c => c.Role == RolesAgent.Libelle(RoleAgent.Dev)))
        {
            texte.AppendLine().AppendLine("## Reprise").AppendLine().AppendLine("Cette construction a déjà été commencée, puis interrompue avant sa vérification. Ce qui a déjà pu être écrit dans /workspace est conservé : regarde d'abord l'état actuel des fichiers, puis poursuis sans tout refaire.");
        }

        if (demande.RelancesContexteNeuf > 0)
        {
            texte.AppendLine().AppendLine("## Reprise").AppendLine().AppendLine(RepriseContexteNeuf);
            if (!string.IsNullOrWhiteSpace(demande.RetourPourDev))
            {
                texte.AppendLine(RetourAvantRelance);
            }
        }

        return texte.ToString();
    }

    public static string MessageArbitrage(Demande demande)
    {
        var contestation = Exiger(demande.ContestationTests, "contestation des tests");
        var texte = new StringBuilder();
        texte.AppendLine("## Demande d'origine").AppendLine().AppendLine(demande.Texte).AppendLine();
        texte.AppendLine(FormaterBacklog(Exiger(demande.Backlog, "backlog")));
        var auteur = contestation.ParLUsine ? "de l'usine" : "du Dev";
        texte.AppendLine(CultureInfo.InvariantCulture, $"## Contestation {auteur}").AppendLine();
        texte.AppendLine(contestation.ParLUsine
            ? "Le Dev a construit l'application. Ces tests d'acceptation verrouillés sont les seuls encore rouges ; il ne les a pas contestés, et l'usine te les soumet d'office :"
            : "Le Dev a construit l'application. Ces tests d'acceptation verrouillés sont les seuls encore rouges, et il les conteste :");
        foreach (var titre in contestation.Titres)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- {titre}");
        }

        texte.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Motif {auteur} : {contestation.Motif}");
        texte.AppendLine(CultureInfo.InvariantCulture, $"Preuve {auteur} : {contestation.Preuve}");
        foreach (var titre in contestation.Dependants)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"Constat de l'oracle : « {titre} » réussit seul sur une instance neuve de l'application, démarrée avec les mêmes données, et échoue dans la suite complète : il dépend de l'état laissé par les autres tests.");
        }

        texte.AppendLine().AppendLine("## Dernier résultat des tests d'acceptation").AppendLine().AppendLine(contestation.Echec);
        texte.AppendLine("## Ton rôle : arbitrer").AppendLine();
        texte.AppendLine("Le contrat reste la référence. Un test contesté n'est faux que si une application conforme à CONTRAT.md, au backlog et à la demande ne peut pas le faire passer de façon fiable en même temps que tous les autres tests, sur la même instance et sans remise à zéro des données, ou s'il enfreint une de tes règles d'écriture. Une exigence que le contrat ne précise pas se corrige en la complétant dans CONTRAT.md, pas en retirant la vérification.");
        texte.AppendLine("- Si un test contesté est faux : réécris seulement les tests contestés, et CONTRAT.md si besoin, sans modifier un fichier de tests qui ne contient aucun test contesté, ni changer le titre des autres tests ou l'identifiant de critère ou de frontière ([F1]…) d'un test réécrit. Chaque test d'un fichier de tests que tu modifies est de nouveau contrôlé contre une application vide (page blanche, réponses 200 sans contenu) : il doit y échouer. Appelle ensuite soumettre_tests avec un résumé de ce que tu as changé et pourquoi.");
        texte.AppendLine("- Sinon, ne modifie rien et appelle maintenir_tests avec ta justification : elle sera transmise au Dev.");
        return texte.ToString();
    }

    public string PromptQa() => PromptAvecCommandeScript("prompts/qa.md");

    public string MessageQa(Demande demande, string? contrat)
    {
        var texte = new StringBuilder();
        texte.AppendLine("## Demande d'origine").AppendLine().AppendLine(demande.Texte).AppendLine();
        texte.AppendLine(FormaterBacklog(Exiger(demande.Backlog, "backlog"), avecNotions: false));
        if (!string.IsNullOrWhiteSpace(contrat))
        {
            texte.AppendLine("## Contrat (/acceptance/CONTRAT.md), établi avec les tests d'acceptation").AppendLine().AppendLine(Couper(contrat.Trim(), 6000));
        }

        return texte.ToString();
    }

    public string PromptQaReverification() => PromptAvecCommandeScript("prompts/qa-reverification.md");

    public string MessageQaReverification(Demande demande, string? contrat, Evaluation evaluation, IReadOnlyList<(Bloquant Bloquant, string Script, string Chemin)> clos, string? diff)
    {
        var texte = new StringBuilder(MessageQa(demande, contrat));
        texte.AppendLine().AppendLine("## Ton évaluation précédente, sur la version d'avant la correction").AppendLine();
        texte.AppendLine(CultureInfo.InvariantCulture, $"Synthèse : {evaluation.Synthese}");
        if (evaluation.Controles is { } controles)
        {
            texte.AppendLine().AppendLine("Liste de contrôle :");
            foreach (var ligne in LignesControles(controles))
            {
                texte.AppendLine(ligne);
            }
        }

        if (evaluation.Remarques.Count > 0)
        {
            texte.AppendLine().AppendLine("Remarques :");
            foreach (var remarque in evaluation.Remarques)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {remarque}");
            }
        }

        texte.AppendLine().AppendLine("## Bloquants clos par la correction du Dev").AppendLine();
        foreach (var (bloquant, script, chemin) in clos)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- Comportement attendu : {bloquant.Attendu}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"  Constat avant la correction : {bloquant.Constat}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"  Script de reproduction, qui passe maintenant, déposé en {chemin} ; il se rejoue tel quel, dans les conditions de l'usine, avec `{Orchestrateur.CommandeScriptQa(chemin, config.BacASable.Port)}` :");
            texte.AppendLine("```").AppendLine(script).AppendLine("```");
        }

        texte.AppendLine().AppendLine("## Correction du Dev (git diff depuis la version évaluée par le QA)").AppendLine();
        texte.AppendLine(diff switch
        {
            null => "Diff indisponible.",
            { Length: 0 } => "Aucune modification du code suivi par git.",
            _ => $"```diff\n{diff}\n```",
        });
        return texte.ToString();
    }

    private static IEnumerable<string> LignesControles(ControlesSoumis controles)
    {
        foreach (var (point, controle) in controles.Points())
        {
            yield return $"- {point} : {controle.Statut}{(string.IsNullOrWhiteSpace(controle.Explication) ? "" : $" — {controle.Explication}")}";
            if (controle is ControleFrontieres frontieres)
            {
                foreach (var essai in frontieres.Essais)
                {
                    yield return $"  - essai « {essai.Valeur} » : attendu « {essai.Attendu} », obtenu « {essai.Obtenu} »{(string.IsNullOrWhiteSpace(essai.Suite) ? "" : $", suite : {essai.Suite}")}";
                }
            }

            if (controle is ControleDoubleSoumission { PremiereEntree.Length: > 0, SecondeEntree.Length: > 0 } doubleSoumission)
            {
                yield return $"  - entrées : « {doubleSoumission.PremiereEntree} » puis « {doubleSoumission.SecondeEntree} »";
            }
        }
    }

    public static string RetourBloquantsQa(string introduction, Evaluation evaluation, string dossierScripts, IReadOnlyDictionary<int, string>? sorties = null)
    {
        var texte = new StringBuilder();
        texte.AppendLine(introduction).Append(ListeBloquantsQa(evaluation, dossierScripts, sorties)).AppendLine(ConsigneBloquantsQa);
        if (evaluation.Bloquants.Exists(b => b.ARejouer))
        {
            texte.AppendLine(ConsigneScriptsQa);
        }

        texte.Append(PointsSecondairesQa(evaluation));
        return texte.ToString().TrimEnd();
    }

    public static string ListeBloquantsQa(Evaluation evaluation, string dossierScripts, IReadOnlyDictionary<int, string>? sorties = null)
    {
        var texte = new StringBuilder();
        for (var i = 0; i < evaluation.Bloquants.Count; i++)
        {
            var bloquant = evaluation.Bloquants[i];
            if (bloquant.Corrige is not null || (sorties is not null && !sorties.ContainsKey(i)))
            {
                continue;
            }

            if (bloquant.Copie is null)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {bloquant.Texte}");
                continue;
            }

            texte.AppendLine(CultureInfo.InvariantCulture, $"- Bloquant n°{i + 1} : comportement attendu : {bloquant.Attendu}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"  Constat du QA : {bloquant.Constat}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"  Script de reproduction à faire passer : {dossierScripts}/{Path.GetFileName(bloquant.Copie)}. Il se termine avec le code 0 quand le comportement attendu est respecté, avec le code 1 quand il ne l'est pas ; tout autre code, une exception non attrapée ou un délai dépassé est une erreur du script. Après ta correction et des tests d'acceptation verts, l'usine le rejoue sur ta version, application démarrée, depuis sa propre copie (le modifier ne change rien) : le bloquant n'est clos que si le script passe deux fois de suite.");
            if (sorties is not null && sorties.TryGetValue(i, out var sortie))
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"  {sortie}");
            }
        }

        return texte.ToString();
    }

    public static string SortieReproduite(string sortie) => $"Sortie de ce script sur ta dernière version, qui reproduit encore le défaut : {Couper(sortie.Trim(), 1500)}";

    public static string SortieEnErreur(string motif, string sortie) => $"Ce script s'est terminé en erreur sur ta dernière version ({motif}), sans reproduire le défaut ni passer : {Couper(sortie.Trim(), 1500)}";

    public static string Regression(ScriptNonRegression script) =>
        $"Régression : le comportement attendu du bloquant n°{script.Numero.ToString(CultureInfo.InvariantCulture)} de la demande {script.Demande} n'est plus respecté : {script.Attendu}";

    public static string RetourRegressions(IReadOnlyList<(ScriptNonRegression Script, string Sortie)> regressions, string dossierScripts)
    {
        var texte = new StringBuilder();
        texte.AppendLine("Les scripts de reproduction des bloquants corrigés par les demandes précédentes du projet protègent ses évolutions. Rejoués sur ta version, application démarrée, ceux-ci ne passent plus :");
        foreach (var (script, sortie) in regressions)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- {Regression(script)}");
            texte.AppendLine(CultureInfo.InvariantCulture, $"  Script à faire passer : {dossierScripts}/{Path.GetFileName(script.Copie)}. Il se termine avec le code 0 quand le comportement attendu est respecté, avec le code 1 quand il ne l'est pas ; l'usine le rejoue après les tests d'acceptation verts, depuis sa propre copie (le modifier ne change rien).{(sortie.Trim() is { Length: > 0 } texteSortie ? $" Sa sortie : {Couper(texteSortie, 600)}" : "")}");
        }

        texte.Append("Rétablis ces comportements sans casser les tests d'acceptation.");
        return texte.ToString();
    }

    public static string RejeuImpossible(string sortie) => $"Rejeu impossible sur ta dernière version, l'application ne démarre pas : {Couper(sortie.Trim(), 1500)}";

    public static string PointsSecondairesQa(Evaluation evaluation)
    {
        var remarques = evaluation.Remarques.Concat(evaluation.Reverifications.SelectMany(r => r.Remarques)).ToList();
        if (remarques.Count == 0)
        {
            return "";
        }

        var texte = new StringBuilder();
        texte.AppendLine().AppendLine(PointsSecondaires);
        foreach (var remarque in remarques)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- {remarque}");
        }

        return texte.ToString();
    }

    public static string EtatBloquant(Bloquant bloquant, string libelle) => bloquant switch
    {
        { Corrige: true } => $"Bloquant corrigé : {bloquant.Attendu}",
        { Corrige: false, ErreurRejeu: ApplicationArreteeAuRejeu } => $"Bloquant non résolu, script non rejoué ({ApplicationArreteeAuRejeu}) : {bloquant.Texte}",
        { Corrige: false, ErreurRejeu: { } erreur } => $"Bloquant non résolu, rejeu de son script en erreur ({erreur}) : {bloquant.Texte}",
        { Corrige: false } => $"Bloquant non résolu : {bloquant.Texte}",
        _ => $"{libelle}{bloquant.Texte}",
    } + (bloquant.ReleveALaReverification ? " (relevé à la revérification)" : "");

    public static IEnumerable<string> LignesReverification(Evaluation evaluation) => evaluation.Reverifications.SelectMany(r => (IEnumerable<string>)
    [
        r switch
        {
            { Rendue: false } => $"Revérification du QA après la correction : sans avis ({r.Synthese}).",
            { Bloquants: 0 } => $"Revérification du QA après la correction : sans bloquant. {r.Synthese}",
            _ => $"Revérification du QA après la correction : {r.Bloquants.ToString(CultureInfo.InvariantCulture)} bloquant(s) nouveau(x). {r.Synthese}",
        } + (string.IsNullOrWhiteSpace(r.NonTeste) ? "" : $" Non testé : {r.NonTeste}"),
        .. r.Remarques.Select(remarque => $"Remarque de la revérification : {remarque}"),
    ]);

    public static string FormaterBacklog(Backlog backlog, bool avecNotions = true)
    {
        var texte = new StringBuilder();
        texte.AppendLine(CultureInfo.InvariantCulture, $"## Backlog : {backlog.Titre}").AppendLine();
        texte.AppendLine(backlog.Resume).AppendLine();
        texte.AppendLine(CultureInfo.InvariantCulture, $"**Mode** : {backlog.Mode}  ");
        texte.AppendLine(CultureInfo.InvariantCulture, $"**Stack** : {backlog.Stack.Choix} — {backlog.Stack.Justification}").AppendLine();
        foreach (var story in backlog.Stories)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"### {story.Id} — {story.Titre}").AppendLine();
            if (!string.IsNullOrWhiteSpace(story.Description))
            {
                texte.AppendLine(story.Description).AppendLine();
            }

            foreach (var critere in story.Criteres)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- **{critere.Id}** : {critere.Texte}");
            }

            texte.AppendLine();
        }

        if (avecNotions && backlog.Notions.Count == 0 && !string.IsNullOrWhiteSpace(backlog.SansNotions))
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"**Notions de la demande** : aucune. {backlog.SansNotions.Trim()}").AppendLine();
        }

        AjouterListe(texte, "Notions de la demande", avecNotions ? [.. backlog.Notions.Select(DecrireNotion)] : []);
        AjouterListe(texte, "Hors périmètre", backlog.HorsPerimetre);
        AjouterListe(texte, "Hypothèses", backlog.Hypotheses);
        return texte.ToString();
    }

    public static string DecrireNotion(Notion notion) => $"{notion.Id} « {notion.Nom} » (d'après « {notion.Phrase} »)";

    public static string MessageAvisNotion(string demande, string notion, string phrase, string exemple) =>
        $"## Demande\n\n{demande}\n\n## Phrase citée\n\n« {phrase} »\n\n## Exemple\n\n{exemple}\n\n## Question\n\nCet exemple fait-il partie de « {notion} » au sens de la phrase citée ?\n";

    public static string FormaterResultat(ResultatOracle resultat, int maxEchecs = 15, int budget = int.MaxValue)
    {
        var texte = new StringBuilder();
        if (resultat.Succes)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"SUCCÈS : {resultat.Reussis}/{resultat.Total} tests d'acceptation réussis{DontInstables(resultat)}.");
            AjouterInstables(texte, resultat);
            if (resultat.Signaux.Count > 0)
            {
                texte.AppendLine("Écarts relevés par la sonde (les tests passent, mais ce sont des défauts à corriger) :");
                foreach (var signal in resultat.Signaux.Take(MaxLignesSignaux))
                {
                    texte.AppendLine(CultureInfo.InvariantCulture, $"- {Couper(signal.Message, LongueurSignal)}");
                }

                if (resultat.Signaux.Count > MaxLignesSignaux)
                {
                    texte.AppendLine(CultureInfo.InvariantCulture, $"- … et {resultat.Signaux.Count - MaxLignesSignaux} autre(s) écart(s).");
                }
            }

            return texte.ToString();
        }

        texte.AppendLine(CultureInfo.InvariantCulture, $"ÉCHEC à l'étape « {resultat.EtapeEchec} » : {resultat.Reussis}/{resultat.Total} tests réussis{DontInstables(resultat)}.");
        if (!resultat.EmpreinteValide)
        {
            texte.AppendLine("Les tests d'acceptation ont été modifiés : c'est interdit.");
        }

        AjouterInstables(texte, resultat);
        if (resultat.Dependants.Count > 0)
        {
            texte.AppendLine("Tests dépendants de l'état laissé par les autres tests : ils échouent dans la suite complète, mais réussissent seuls sur une instance neuve de l'application, démarrée avec les mêmes données. Ce n'est pas une régression : ne vide pas les données, ne supprime pas la base au démarrage et ne vide ni ne masque l'affichage pour les satisfaire.");
            foreach (var titre in resultat.Dependants)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"- {titre}");
            }
        }

        foreach (var titre in resultat.EchouentSeuls)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"« {titre} » : rejoué seul sur une instance neuve, il échoue aussi : son échec ne vient pas de l'ordre des tests.");
        }

        var echecs = resultat.Echecs.Take(maxEchecs).ToList();
        var resumes = echecs.Select(e => $"- {e.Titre} : {Couper(PremiereLigne(e.Erreur), 300)}{Environment.NewLine}").ToList();
        var autres = resultat.Echecs.Count > maxEchecs ? $"- … et {resultat.Echecs.Count - maxEchecs} autres échecs.{Environment.NewLine}" : "";
        var journal = string.IsNullOrWhiteSpace(resultat.Journal) ? "" : $"{Environment.NewLine}{Couper(resultat.Journal, 6000)}{Environment.NewLine}";
        var details = echecs.Select(DetaillerEchec).ToList();
        var toutDetaille = texte.Length + details.Sum(d => d.Length) + autres.Length + journal.Length <= budget;
        var reserve = resumes.Sum(r => r.Length) + autres.Length + NoteEchecsResumes(echecs.Count).Length + journal.Length;
        var echecsResumes = 0;
        for (var i = 0; i < echecs.Count; i++)
        {
            reserve -= resumes[i].Length;
            if (toutDetaille || i == 0 || (echecsResumes == 0 && texte.Length + details[i].Length + reserve <= budget))
            {
                texte.Append(details[i]);
            }
            else
            {
                texte.Append(resumes[i]);
                echecsResumes++;
            }
        }

        texte.Append(autres);
        if (echecsResumes > 0)
        {
            texte.Append(NoteEchecsResumes(echecsResumes));
        }

        texte.Append(journal);
        return texte.ToString();
    }

    public static string FormaterRetourDev(ResultatOracle resultat, Demande demande, int budget = int.MaxValue)
    {
        var ambigus = TestsAuRepereAmbigu(resultat);
        var recours = !demande.ModeAuto
            ? " ; si tu l'estimes faux, dis-le dans le résumé de terminer_construction"
            : demande.ContestationTests is null ? " ; conteste ce test avec contester_tests" : "";
        var ligne = ambigus.Count == 0
            ? ""
            : $"Repère ambigu (« {ViolationModeStrict} ») dans {string.Join(" ; ", ambigus)} : le repère du test désigne plusieurs éléments à la fois. Ne modifie pas l'interface exigée pour lui{recours}.{Environment.NewLine}";
        return ligne + FormaterResultat(resultat, budget: budget - ligne.Length);
    }

    internal static List<string> TestsAuRepereAmbigu(ResultatOracle resultat) =>
        [.. resultat.Echecs.Where(e => !NonRegression.EstTitre(e.Titre) && e.Erreur.Contains(ViolationModeStrict, StringComparison.Ordinal)).Select(e => e.Titre).Distinct(StringComparer.Ordinal)];

    internal static string PremiereLigne(string erreur) => erreur.Trim().Split('\n', 2)[0].TrimEnd('\r');

    public static string DontInstables(ResultatOracle resultat, bool titres = false) => resultat.Instables.Count switch
    {
        0 => "",
        var nombre => $", dont {nombre.ToString(CultureInfo.InvariantCulture)} instable{(nombre > 1 ? "s" : "")}{(titres ? $" (échec non reproductible sur le même code, compté réussi : {string.Join(" ; ", resultat.Instables)})" : "")}",
    };

    private static void AjouterInstables(StringBuilder texte, ResultatOracle resultat)
    {
        if (resultat.Instables.Count == 0)
        {
            return;
        }

        texte.AppendLine("Tests instables, comptés réussis : ils ont réussi et échoué sur le même code, sans changement. C'est un échec non reproductible : ne modifie pas le code pour eux.");
        foreach (var titre in resultat.Instables)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- {titre}");
        }
    }

    public static string MessageSignaux(IReadOnlyList<SignalOracle> signaux, IReadOnlyCollection<string> dejaSignales)
    {
        const string Entete = "La sonde de l'usine a rejoué l'application avec d'autres saisies et requêtes que celles des tests d'acceptation, qui passent tous. Ces écarts sont des défauts, même si aucun test ne les voit :";
        const string Conclusion = "Corrige la cause pour toutes les saisies, tous les champs et toutes les routes du même genre, pas seulement l'exemple cité, sans casser les tests. Si un écart est voulu par la demande ou par CONTRAT.md, garde le comportement et explique-le dans le résumé de terminer_construction. lancer_tests_acceptation rejoue maintenant la sonde et affiche les écarts restants.";
        var retenus = signaux.Take(MaxLignesSignaux).ToList();
        var liste = new StringBuilder();
        foreach (var (titre, toujoursPresents) in new[] { ("Toujours présents après ta dernière correction :", true), ("Nouveaux :", false) })
        {
            var groupe = retenus.Where(s => dejaSignales.Contains(s.Cle, StringComparer.Ordinal) == toujoursPresents).ToList();
            if (groupe.Count > 0)
            {
                liste.AppendLine(titre);
                foreach (var signal in groupe)
                {
                    liste.AppendLine(CultureInfo.InvariantCulture, $"- {Couper(signal.Message, LongueurSignal)}");
                }
            }
        }

        if (signaux.Count > MaxLignesSignaux)
        {
            liste.AppendLine(CultureInfo.InvariantCulture, $"- … et {signaux.Count - MaxLignesSignaux} autre(s) écart(s).");
        }

        return $"{Entete}{Environment.NewLine}{liste.ToString().TrimEnd()}{Environment.NewLine}{Conclusion}";
    }

    public static string FormaterCout(decimal cout, bool connu) => (connu, cout) switch
    {
        (true, _) => $"{cout.ToString("0.000", CultureInfo.InvariantCulture)} $",
        (false, 0m) => "coût inconnu, faute de tarif ou de consommation renvoyée par le modèle",
        _ => $"au moins {cout.ToString("0.000", CultureInfo.InvariantCulture)} $, faute de tarif ou de consommation renvoyée pour une partie des appels",
    };

    public static string? EchecAvantLesTests(ResultatOracle? resultat) => resultat?.EtapeEchec switch
    {
        "build" => "l'application livrée ne se construit pas (étape « build »)",
        "demarrage" => "l'application livrée ne démarre pas (étape « demarrage »)",
        _ => null,
    };

    private static string ContexteDepotPo(Demande demande) => demande switch
    {
        { NouveauxTests: true } => "",
        { EstProjetExistant: true } => " Le répertoire /workspace contient le projet existant à faire évoluer (clone du dépôt sur une branche dédiée).",
        _ => " Le répertoire /workspace ne contient rien : c'est une application nouvelle, le répertoire est vide.",
    };

    private static string ReglesSaisiePo(Demande demande)
    {
        var regles = Conditionnelles(demande, RegleSaisiePo, RegleArrondiPo);
        return demande.CadrageParDemandeur ? regles.Replace("écris dans les hypothèses", "écris dans les critères", StringComparison.Ordinal) : regles;
    }

    private static string Conditionnelles(Demande demande, string saisie, string arrondi)
    {
        var texte = Orchestrateur.SansAccents(string.Join('\n', demande.TextesDuBesoin)).ToLowerInvariant();
        var lignes = new List<string>();
        if (Orchestrateur.Phrase().Matches(texte).Any(p => NomDeNombre().IsMatch(p.Value) && VerbeDeSaisie().IsMatch(p.Value)))
        {
            lignes.Add(saisie);
        }

        if (MotArrondi().IsMatch(texte))
        {
            lignes.Add(arrondi);
        }

        return string.Join('\n', lignes);
    }

    private static void AjouterListe(StringBuilder texte, string titre, IReadOnlyCollection<string> elements)
    {
        if (elements.Count == 0)
        {
            return;
        }

        texte.AppendLine(CultureInfo.InvariantCulture, $"**{titre}** :");
        foreach (var element in elements)
        {
            texte.AppendLine(CultureInfo.InvariantCulture, $"- {element}");
        }

        texte.AppendLine();
    }

    private static string DetaillerEchec(EchecTest echec)
    {
        var texte = new StringBuilder();
        texte.AppendLine(CultureInfo.InvariantCulture, $"- {echec.Titre} : {Couper(echec.Erreur, 900)}");
        if (!string.IsNullOrWhiteSpace(echec.Page))
        {
            texte.AppendLine("  Ce que la page affichait au moment de l'échec (instantané d'accessibilité de Playwright) :");
            foreach (var ligne in Couper(echec.Page, 2000).Split('\n'))
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"    {ligne.TrimEnd('\r')}");
            }

            if (TexteMasque(echec.Erreur, echec.Page) is { } masque)
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"  Le texte « {masque} » est dans le DOM de cet élément mais masqué (absent de l'instantané d'accessibilité) : toContainText et toHaveText lisent aussi le texte caché (display:none, hidden, visibility). Ne crée pas l'élément (ou vide son texte) quand il ne doit pas apparaître, au lieu de le masquer.");
            }
        }

        if (!string.IsNullOrWhiteSpace(echec.Console))
        {
            texte.AppendLine("  Erreurs JavaScript et messages d'erreur de la console du navigateur pendant ce test :");
            foreach (var ligne in Couper(echec.Console, 2000).Split('\n'))
            {
                texte.AppendLine(CultureInfo.InvariantCulture, $"    {ligne.TrimEnd('\r')}");
            }
        }

        return texte.ToString();
    }

    private static string? TexteMasque(string erreur, string page)
    {
        if (!AssertionTexteNegative().IsMatch(erreur)
            || page.EndsWith(LecteurRapportPlaywright.FinPageCoupee, StringComparison.Ordinal)
            || AttenduNegatif().Match(erreur) is not { Success: true } attendu
            || RecuTexte().Match(erreur) is not { Success: true } recu)
        {
            return null;
        }

        var texte = Orchestrateur.SansBlancsMultiples(Desechapper(attendu.Groups["texte"].Value));
        return texte.Length > 0
            && Orchestrateur.SansBlancsMultiples(Desechapper(recu.Groups["texte"].Value)).Contains(texte, StringComparison.OrdinalIgnoreCase)
            && !Orchestrateur.SansBlancsMultiples(page).Contains(texte, StringComparison.OrdinalIgnoreCase)
                ? texte
                : null;
    }

    private static string Desechapper(string texte) => CaractereEchappe().Replace(texte, "$1");

    private static string NoteEchecsResumes(int nombre) =>
        $"Faute de place, {nombre.ToString(CultureInfo.InvariantCulture)} échec(s) ci-dessus sont réduits à leur titre et à la première ligne de l'erreur : lance lancer_tests_acceptation pour voir leur détail (erreur complète, page affichée, console du navigateur).{Environment.NewLine}";

    private static string Couper(string texte, int max) => texte.Length <= max ? texte : texte[..max] + " […]";

    private static T Exiger<T>(T? valeur, string nom) where T : class =>
        valeur ?? throw new InvalidOperationException($"Étape impossible : {nom} absent.");

    private string PromptAvecCommandeScript(string ressource) => Remplacer(Ressources.Lire(ressource), new Dictionary<string, string>
    {
        ["PORT"] = config.BacASable.Port.ToString(CultureInfo.InvariantCulture),
        ["COMMANDE_SCRIPT"] = Orchestrateur.CommandeScriptQa("/tmp/script.js", config.BacASable.Port),
    });

    private static string Remplacer(string modele, IReadOnlyDictionary<string, string> valeurs)
    {
        var resultat = modele;
        foreach (var (cle, valeur) in valeurs)
        {
            resultat = resultat.Replace("{{" + cle + "}}", valeur, StringComparison.Ordinal);
        }

        return resultat;
    }

    [GeneratedRegex(@"\.not\.to(?:Contain|Have)Text\(")]
    private static partial Regex AssertionTexteNegative();

    [GeneratedRegex(@"^Expected(?: substring| string)?: not ""(?<texte>(?:[^""\\]|\\.)*)""", RegexOptions.Multiline)]
    private static partial Regex AttenduNegatif();

    [GeneratedRegex(@"^Received(?: string)?: ""(?<texte>(?:[^""\\]|\\.)*)""", RegexOptions.Multiline)]
    private static partial Regex RecuTexte();

    [GeneratedRegex(@"\\([""\\])")]
    private static partial Regex CaractereEchappe();

    [GeneratedRegex(@"\b(?:nombres?|numeri\w*|chiffres?|valeurs?|montants?|prix|quantites?|sommes?|ages?|poids)\b")]
    private static partial Regex NomDeNombre();

    [GeneratedRegex(@"\b(?:sais\w*|tap(?:e|es|ez|er|ent)|entr(?:er|ez|ees?))\b")]
    private static partial Regex VerbeDeSaisie();

    [GeneratedRegex(@"arrondi|decimales?\b|chiffres? apres la virgule|centimes?\b")]
    private static partial Regex MotArrondi();
}
