using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Usine.Core.BacASable;
using Usine.Core.Configuration;
using static Usine.Core.Texte;

namespace Usine.Core.Agents;

public sealed partial class OutilsAgent(IBacASable bac, ConfigLimites limites)
{
    public const string FichierEcrit = "Fichier écrit : ";

    public const string EnteteCodeDeSortie = "code de sortie : ";

    internal const string PrefixeRefusReecriture = "Refusé : ta commande réécrit en entier";

    internal const string PrefixeRefusSuppression = "Refusé : ta commande supprime ou renomme";

    internal const string PrefixeRefusDossierInterne = "Refusé : ta commande nomme /opt/acceptance-runner";

    private const int TailleMaxFichierRemplacement = 400_000;

    private const int MaxLignesLecture = 2000;

    private const int LargeurMoyenneLigneListing = 40;

    private const int ReserveTroncatureListing = 200;

    private const int LongueurPrefixeDeListing = 9;

    private const int LongueurPiedDeListing = 30;

    private const int MaxEntreesListe = 500;

    private const int CodeEcrasementRefuse = 6;

    private const string RaisonEcrasementRefuse = "(tu ne l'as pas lu en entier lors d'un tour précédent, ou ta lecture a été retirée de ton historique pour faire de la place)";

    private const string ConseilEcrasementRefuse = "Pour corriger des points précis, utilise remplacer_dans_fichier ; pour le réécrire vraiment en entier, relis-le d'abord en entier avec lire_fichier, en un seul tour s'il faut plusieurs lectures (plusieurs appels dans la même réponse), puis écris-le au tour suivant.";

    private const string RefusDossierInterne = PrefixeRefusDossierInterne + " ou /tmp/acc, réservés au lanceur des tests et aux sondes de l'usine, ou un autre dossier de /opt que /opt/echantillons : la commande n'a pas été lancée. Les tests d'acceptation se lisent dans /acceptance. Ce que l'usine contrôle est dit dans tes consignes et dans la description de tes outils ; de /opt, seuls /opt/echantillons et l'affectation NODE_PATH=/opt/acceptance-runner/node_modules sont à toi.";

    private const string RefusInstallation = "Refusé : ton rôle n'installe ni ne télécharge aucun paquet (npx, npm install, yarn add, pnpm add, pip install…) : la commande n'a pas été lancée. Playwright et ses types sont déjà fournis, rien n'est à installer :";

    private const string RefusParcoursDeLaRacine = "Refusé : ta commande parcourt toute l'arborescence depuis la racine / (find /, grep -r sur /, ls -R /, du /…), ce qui dure souvent plus que le délai d'une commande : elle n'a pas été lancée. Ce qui concerne ton rôle est dans";

    private const string NomDossierDuLanceur = "acc";

    private const string DossierDuLanceur = $"/tmp/{NomDossierDuLanceur}";

    private const string RefusDossierDuLanceur = $"Erreur : {DossierDuLanceur} est réservé au lanceur des tests de l'usine ; les tests d'acceptation se lisent dans /acceptance.";

    private static readonly string[] RacinesLecture = ["/workspace", "/acceptance", "/tmp", "/opt/echantillons"];

    private static readonly string[] RepertoiresExclusDuListage = ["node_modules", ".git", "obj", ".angular", "__pycache__", ".venv"];

    private static readonly string[] RepertoiresExclusDeLaRecherche = ["node_modules", ".git", "obj", ".angular"];

    private static readonly string[] RepertoiresExclusDeLaSuppression = [.. RepertoiresExclusDuListage, "bin", "dist", "build", "coverage", "venv", ".pytest_cache", ".next", ".nuxt", ".cache", "test-results", "playwright-report"];

    private static readonly string[] FichiersRegeneres = ["package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "packages.lock.json", "poetry.lock", "Pipfile.lock", "uv.lock", "Cargo.lock", "composer.lock", "Gemfile.lock", "go.sum"];

    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly string[] MarqueursDeLUsine = [ExecuteurAgent.ContenuRetire, ExecuteurAgent.SortieRetiree];

    private readonly Dictionary<string, List<Vu>> vus = new(StringComparer.Ordinal);

    private int LignesParDefaut => Math.Clamp(limites.TailleMaxSortieOutil / LargeurMoyenneLigneListing, 1, MaxLignesLecture);

    public IReadOnlyList<AIFunction> Communs(string repertoireTravail, string? racineEcriture, string? conseilInstallation = null, IReadOnlyList<string>? dossiersDeRecherche = null)
    {
        var outils = new List<AIFunction>
        {
            ExecuterCommande(repertoireTravail, conseilInstallation, dossiersDeRecherche),
            LireFichier(repertoireTravail),
            ListerFichiers(repertoireTravail),
            Rechercher(repertoireTravail),
        };
        if (racineEcriture is not null)
        {
            outils.Add(EcrireFichier(repertoireTravail, racineEcriture));
            outils.Add(RemplacerDansFichier(repertoireTravail, racineEcriture));
        }

        return outils;
    }

    public AIFunction ExecuterCommande(string repertoireTravail, string? conseilInstallation = null, IReadOnlyList<string>? dossiersDeRecherche = null) => AIFunctionFactory.Create(
        async (
            AIFunctionArguments arguments,
            [Description("Commande bash à exécuter.")] string commande,
            [Description("Délai maximum en secondes (facultatif).")] int? delai_secondes = null,
            CancellationToken ct = default) =>
        {
            if (DossierInterne().IsMatch(NodePathDuLanceur().Replace(commande, "")))
            {
                return RefusDossierInterne;
            }

            if (dossiersDeRecherche is not null && ParcoursDeLaRacine().IsMatch(commande))
            {
                return $"{RefusParcoursDeLaRacine} {Enumerer(dossiersDeRecherche)} : cherche là.";
            }

            if (conseilInstallation is not null && Installation().IsMatch(commande))
            {
                return $"{RefusInstallation} {conseilInstallation}";
            }

            var historique = HistoriqueDe(arguments);
            var cibles = CiblesReecrites(repertoireTravail, commande);
            var inconnues = cibles.Where(cible => !ConnuEnEntier(cible, historique)).ToList();
            if (inconnues.Count > 0)
            {
                var verification = await bac.ExecuterAsync(string.Concat(inconnues.Select(cible => $"{ConditionEcrasementRefuse(cible)} && {{ echo {Quoter(cible)}; {MesureDe(Quoter(cible))}; }}; ")) + "true", TimeSpan.FromSeconds(30), repertoireTravail, ct: ct);
                if (FichiersMesures(verification.Sortie).FirstOrDefault() is { } refusee)
                {
                    return $"{PrefixeRefusReecriture} {refusee.Chemin}, qui existe déjà, dépasse {limites.TailleMaxSortieOutil.ToString(CultureInfo.InvariantCulture)} octets, et dont tu n'as pas sous les yeux tout le contenu actuel {RaisonEcrasementRefuse} : le réécrire de mémoire ferait disparaître ce qui marche, la commande n'a pas été lancée. {ConseilLectures(refusee.Chemin, historique, refusee.Mesure)}";
                }
            }

            var suppressions = CiblesSupprimees(repertoireTravail, commande);
            if (suppressions.Count > 0)
            {
                var verification = await bac.ExecuterAsync(VerificationSuppression(suppressions), TimeSpan.FromSeconds(30), repertoireTravail, ct: ct);
                if (FichiersMesures(verification.Sortie).FirstOrDefault(fichier => !EstRegenere(fichier.Chemin) && !ConnuEnEntier(fichier.Chemin, historique)) is { } supprimee)
                {
                    return $"{PrefixeRefusSuppression} {supprimee.Chemin}, qui existe, dépasse {limites.TailleMaxSortieOutil.ToString(CultureInfo.InvariantCulture)} octets, et dont tu n'as pas sous les yeux tout le contenu actuel {RaisonEcrasementRefuse} : le supprimer pour le recréer de mémoire ferait disparaître ce qui marche, la commande n'a pas été lancée. {ConseilLectures(supprimee.Chemin, historique, supprimee.Mesure)}";
                }
            }

            var delai = TimeSpan.FromSeconds(Math.Clamp(delai_secondes ?? limites.DelaiCommandeSecondes, 5, limites.DelaiBuildSecondes));
            var resultat = await bac.ExecuterAsync(commande, delai, repertoireTravail, ct: ct);
            if (resultat.Reussi)
            {
                foreach (var cible in cibles)
                {
                    vus[cible] = [VuEnEntier(historique)];
                }
            }

            var entete = resultat switch
            {
                { DelaiDepasse: true } => $"{EnteteCodeDeSortie}délai de {delai.TotalSeconds} s dépassé, commande interrompue (elle bloquait : serveur au premier plan ou programme interactif ?)",
                { Code: 137 or 143 } => $"{EnteteCodeDeSortie}{resultat.Code.ToString(CultureInfo.InvariantCulture)} (la commande a été tuée par un signal, par exemple par un pkill -f dont le motif correspond aussi à sa propre ligne de commande : elle s'est alors tuée elle-même)",
                _ => $"{EnteteCodeDeSortie}{resultat.Code.ToString(CultureInfo.InvariantCulture)}",
            };
            return $"{entete}\n{ExecuteurProcessus.Tronquer(resultat.SortieComplete, limites.TailleMaxSortieOutil)}";
        },
        "executer_commande",
        $"Exécute une commande bash dans le conteneur Linux (répertoire courant : {repertoireTravail}). Outils disponibles : node 24, {(conseilInstallation is null ? "npm, npx, " : "")}dotnet 10, python3, git, curl, sqlite3. Renvoie le code de sortie et la sortie (tronquée si longue). N'utilise pas de commande interactive ni de serveur au premier plan.");

    public AIFunction LireFichier(string repertoireTravail) => AIFunctionFactory.Create(
        async (
            AIFunctionArguments arguments,
            [Description("Chemin du fichier (relatif au répertoire courant ou absolu).")] string chemin,
            [Description("Première ligne à lire, à partir de 1 (facultatif).")] int? ligne_debut = null,
            [Description("Nombre de lignes à lire (facultatif).")] int? nombre_lignes = null,
            CancellationToken ct = default) =>
        {
            var absolu = Resoudre(repertoireTravail, chemin);
            if (RefusHorsLecture(absolu, "lecture autorisée") is { } refus)
            {
                return refus;
            }

            var debut = Math.Max(1, ligne_debut ?? 1);
            var nombre = Math.Clamp(nombre_lignes ?? LignesParDefaut, 1, MaxLignesLecture);
            var commande = $"test -d {Quoter(absolu)} && {{ echo 'Répertoire, pas un fichier : {Echapper(absolu)} ; utilise lister_fichiers.'; exit 1; }}; "
                + $"test -f {Quoter(absolu)} || {{ echo 'Fichier introuvable : {Echapper(absolu)}'; exit 1; }}; "
                + $"awk -v d={debut} -v n={nombre} 'NR>=d && NR<d+n {{ printf \"%6d  %s\\n\", NR, $0 }}' {Quoter(absolu)}; "
                + $"total=$(awk 'END{{print NR}}' {Quoter(absolu)}); echo \"[fichier de $total lignes]\"";
            var resultat = await bac.ExecuterAsync(commande, TimeSpan.FromSeconds(30), repertoireTravail, ct: ct);
            var listing = TronquerListing(resultat.SortieComplete, debut, limites.TailleMaxSortieOutil);
            if (resultat.Reussi && LignesVues(listing, debut) is { } fin)
            {
                var historique = HistoriqueDe(arguments);
                Noter(absolu, new Vu(historique, historique?.Tour ?? 0, debut, fin, (historique?.Tour ?? 0) + 1));
            }

            return listing;
        },
        "lire_fichier",
        $"Lit un fichier texte avec les numéros de ligne ({LignesParDefaut} lignes par défaut, {MaxLignesLecture} au plus). Une sortie trop longue est coupée à la fin d'une ligne, avec les numéros des lignes omises : relis-les avec ligne_debut et nombre_lignes.");

    public AIFunction ListerFichiers(string repertoireTravail) => AIFunctionFactory.Create(
        async (
            [Description("Répertoire à lister (facultatif, répertoire courant par défaut).")] string? chemin = null,
            [Description("Profondeur maximale (facultatif, 3 par défaut).")] int? profondeur = null,
            CancellationToken ct = default) =>
        {
            var absolu = Resoudre(repertoireTravail, string.IsNullOrWhiteSpace(chemin) ? "." : chemin);
            if (RefusHorsLecture(absolu, "listage autorisé") is { } refus)
            {
                return refus;
            }

            var niveau = Math.Clamp(profondeur ?? 3, 1, 8);
            var elagage = string.Join(" -o ", SaufNomDeBase(absolu, RepertoiresExclusDuListage).Select(nom => $"-name {nom}"))
                + (EstSous(DossierDuLanceur, [absolu]) ? $" -o -path {Quoter(DossierDuLanceur)}" : "");
            var commande = $"find {Quoter(absolu)} -maxdepth {niveau} "
                + $"\\( {elagage} \\) -prune -o -print "
                + $"| sort | head -{(MaxEntreesListe + 1).ToString(CultureInfo.InvariantCulture)}";
            var resultat = await bac.ExecuterAsync(commande, TimeSpan.FromSeconds(30), repertoireTravail, ct: ct);
            var entrees = resultat.Sortie.Split('\n');
            var marqueur = "";
            if (entrees.Length > MaxEntreesListe)
            {
                resultat = resultat with { Sortie = string.Join('\n', entrees[..MaxEntreesListe]) };
                marqueur = $"\n[… liste coupée à {MaxEntreesListe.ToString(CultureInfo.InvariantCulture)} entrées : précise le chemin ou réduis la profondeur …]";
            }

            return resultat.Sortie.Length > 0
                ? ExecuteurProcessus.Tronquer(resultat.SortieComplete + marqueur, limites.TailleMaxSortieOutil)
                : resultat.Erreurs.Length > 0 ? $"Erreur : {ExecuteurProcessus.Tronquer(resultat.Erreurs, 2000)}" : "(vide)";
        },
        "lister_fichiers",
        "Liste les fichiers et répertoires (sans node_modules, .git, obj).");

    public AIFunction Rechercher(string repertoireTravail) => AIFunctionFactory.Create(
        async (
            [Description("Texte ou expression régulière à chercher.")] string motif,
            [Description("Répertoire où chercher (facultatif, répertoire courant par défaut).")] string? chemin = null,
            CancellationToken ct = default) =>
        {
            var absolu = Resoudre(repertoireTravail, string.IsNullOrWhiteSpace(chemin) ? "." : chemin);
            if (RefusHorsLecture(absolu, "recherche autorisée") is { } refus)
            {
                return refus;
            }

            var exclusions = string.Concat(SaufNomDeBase(absolu, RepertoiresExclusDeLaRecherche).Select(nom => $"--exclude-dir={nom} "))
                + (EstSous(DossierDuLanceur, [absolu]) ? $"--exclude-dir={NomDossierDuLanceur} " : "");
            var commande = $"test -e {Quoter(absolu)} || {{ echo 'Chemin introuvable : {Echapper(absolu)}' >&2; exit 2; }}; "
                + $"grep -rnIEs {exclusions}"
                + $"-e {Quoter(motif)} {Quoter(absolu)} | head -200";
            var resultat = await bac.ExecuterAsync(commande, TimeSpan.FromSeconds(60), repertoireTravail, ct: ct);
            return resultat.Sortie.Length > 0
                ? ExecuteurProcessus.Tronquer(resultat.Sortie, limites.TailleMaxSortieOutil)
                : resultat.Erreurs.Length > 0 ? $"Erreur : {ExecuteurProcessus.Tronquer(resultat.Erreurs, 2000)}" : "Aucun résultat.";
        },
        "rechercher",
        "Cherche un motif dans le contenu des fichiers (grep récursif, 200 résultats au maximum), pas dans leurs noms : pour trouver un fichier, utilise lister_fichiers.");

    public AIFunction EcrireFichier(string repertoireTravail, string racineEcriture) => AIFunctionFactory.Create(
        async (
            AIFunctionArguments arguments,
            [Description("Chemin du fichier (relatif au répertoire courant ou absolu).")] string chemin,
            [Description("Contenu complet du fichier.")] string contenu,
            CancellationToken ct = default) =>
        {
            var absolu = Resoudre(repertoireTravail, chemin);
            if (!EstSous(absolu, [racineEcriture]))
            {
                return $"Erreur : tu ne peux écrire que sous {racineEcriture}.";
            }

            if (RefusMarqueurDeLUsine("contenu", contenu, absolu) is { } refusMarqueur)
            {
                return refusMarqueur;
            }

            try
            {
                contenu = Denumeroter(Desechapper(contenu));
            }
            catch (ExtraitNumeroteException extrait)
            {
                return $"Refusé : le contenu est un extrait numéroté (lignes {extrait.Debut.ToString(CultureInfo.InvariantCulture)} à {extrait.Fin.ToString(CultureInfo.InvariantCulture)}), pas un fichier complet. Écris le fichier entier, sans numéros de ligne.";
            }
            catch (ListingIncompletException incomplet)
            {
                return $"Refusé : le contenu {incomplet.Explication}. Relis le fichier par morceaux avec lire_fichier (ligne_debut, nombre_lignes), puis écris le fichier entier, sans numéros de ligne.";
            }

            if (MotifRefusForme(contenu) is { } refus)
            {
                return refus;
            }

            var historique = HistoriqueDe(arguments);
            var garde = ConnuEnEntier(absolu, historique)
                ? ""
                : $"{ConditionEcrasementRefuse(absolu)} && {{ cat > /dev/null; {MesureDe(Quoter(absolu))}; exit {CodeEcrasementRefuse.ToString(CultureInfo.InvariantCulture)}; }}; ";
            var commande = $"{garde}mkdir -p \"$(dirname {Quoter(absolu)})\" && cat > {Quoter(absolu)}";
            var resultat = await bac.ExecuterAsync(commande, TimeSpan.FromSeconds(60), repertoireTravail, entree: contenu, ct: ct);
            if (garde.Length > 0 && resultat.Code == CodeEcrasementRefuse)
            {
                return $"Refusé : {absolu} existe déjà, dépasse {limites.TailleMaxSortieOutil.ToString(CultureInfo.InvariantCulture)} octets, et tu n'as pas sous les yeux tout son contenu actuel {RaisonEcrasementRefuse} : le réécrire de mémoire ferait disparaître ce qui marche, il n'a pas été modifié. {ConseilLectures(absolu, historique, Mesurer(resultat.Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))}";
            }

            if (!resultat.Reussi)
            {
                return $"Erreur d'écriture : {resultat.SortieComplete}";
            }

            vus[absolu] = [VuEnEntier(historique)];
            historique?.NoterEcriture(absolu);
            return $"{FichierEcrit}{absolu} ({Encoding.UTF8.GetByteCount(contenu).ToString(CultureInfo.InvariantCulture)} octets).";
        },
        "ecrire_fichier",
        $"Crée ou remplace un fichier avec le contenu donné, brut : sans guillemets JSON autour et sans les numéros de ligne affichés par lire_fichier, qui seraient retirés ; un extrait numéroté qui ne commence pas à la ligne 1, ou un listing incomplet, est refusé. Un fichier existant de plus de {limites.TailleMaxSortieOutil.ToString(CultureInfo.InvariantCulture)} octets n'est remplacé que si tu as sous les yeux tout son contenu actuel (lu en entier lors d'un tour précédent ou écrit par toi, et pas retiré de ton historique) : sinon, modifie-le avec remplacer_dans_fichier (écriture autorisée sous {racineEcriture}).");

    public AIFunction RemplacerDansFichier(string repertoireTravail, string racineEcriture) => AIFunctionFactory.Create(
        async (
            AIFunctionArguments arguments,
            [Description("Chemin du fichier existant (relatif au répertoire courant ou absolu).")] string chemin,
            [Description("Texte exact à remplacer, une ou plusieurs lignes, recopié tel quel depuis lire_fichier ; il doit apparaître une seule fois dans le fichier.")] string? ancien,
            [Description("Texte de remplacement ; vide pour supprimer « ancien ».")] string nouveau,
            CancellationToken ct = default) =>
        {
            var absolu = Resoudre(repertoireTravail, chemin);
            if (!EstSous(absolu, [racineEcriture]))
            {
                return $"Erreur : tu ne peux écrire que sous {racineEcriture}.";
            }

            if (string.IsNullOrEmpty(ancien))
            {
                return $"Refusé : « ancien » est {(ancien is null ? "absent" : "vide")}. Recopie exactement le texte à remplacer ; pour créer un fichier ou le réécrire en entier, utilise ecrire_fichier.";
            }

            if ((RefusMarqueurDeLUsine("ancien", ancien, absolu) ?? RefusMarqueurDeLUsine("nouveau", nouveau, absolu)) is { } refusMarqueur)
            {
                return refusMarqueur;
            }

            var ancienDesechappe = Desechapper(ancien);
            var nouveauDesechappe = Desechapper(nouveau);
            var taille = TailleMaxFichierRemplacement.ToString(CultureInfo.InvariantCulture);
            var commande = $"test -d {Quoter(absolu)} && exit 5; test -f {Quoter(absolu)} || exit 3; test \"$(wc -c < {Quoter(absolu)})\" -le {taille} || exit 4; base64 -w0 {Quoter(absolu)}";
            var lecture = await bac.ExecuterAsync(commande, TimeSpan.FromSeconds(30), repertoireTravail, ct: ct);
            if (lecture.Code == 5)
            {
                return $"Erreur : {absolu} est un répertoire, pas un fichier ; utilise lister_fichiers pour trouver le fichier à modifier.";
            }

            if (lecture.Code == 3)
            {
                return $"Erreur : fichier introuvable : {absolu}. Vérifie le chemin avec lister_fichiers ; pour créer un fichier, utilise ecrire_fichier.";
            }

            if (lecture.Code == 4)
            {
                return $"Erreur : {absolu} dépasse {taille} octets, trop gros pour remplacer_dans_fichier.";
            }

            if (!lecture.Reussi)
            {
                return $"Erreur de lecture : {lecture.SortieComplete}";
            }

            string contenu;
            try
            {
                contenu = Utf8Strict.GetString(Convert.FromBase64String(lecture.Sortie));
            }
            catch (FormatException)
            {
                return "Erreur de lecture : sortie inattendue du bac à sable.";
            }
            catch (DecoderFallbackException)
            {
                return $"Erreur : {absolu} n'est pas un fichier texte UTF-8 ; remplacer_dans_fichier ne modifie que des fichiers texte.";
            }

            if (PreparerExtrait("ancien", ancien, out var ancienNet) is { } refusAncien)
            {
                return refusAncien;
            }

            var localisation = Localiser(contenu, ancienNet);
            var extrait = ancienNet;
            var desechappe = false;
            if (ancienDesechappe != ancien && localisation.Occurrences == 0)
            {
                if (PreparerExtrait("ancien", ancienDesechappe, out var candidat) is { } refusCandidat)
                {
                    return refusCandidat;
                }

                var alternative = Localiser(contenu, candidat);
                if (alternative.Occurrences > 0)
                {
                    localisation = alternative;
                    extrait = candidat;
                    desechappe = true;
                }
            }

            if (localisation.Occurrences == 0)
            {
                localisation = LocaliserSouple(contenu, ancienNet);
            }

            var nouveauLarge = false;
            if (localisation.Occurrences == 0 && DenumeroterLarge(ancienNet) is { } ancienLarge)
            {
                localisation = Localiser(contenu, ancienLarge);
                if (localisation.Occurrences == 0)
                {
                    localisation = LocaliserSouple(contenu, ancienLarge);
                }

                nouveauLarge = localisation.Occurrences > 0;
                if (nouveauLarge)
                {
                    extrait = ancienLarge;
                }
            }

            if (PreparerExtrait("nouveau", desechappe ? nouveauDesechappe : nouveau, out var nouveauNet) is { } refusNouveau)
            {
                return refusNouveau;
            }

            if (nouveauLarge && DenumeroterLarge(nouveauNet) is { } nouveauSansNumeros)
            {
                nouveauNet = nouveauSansNumeros;
            }

            if (EstSurUneLigne(nouveauNet) && !(localisation.Occurrences > 0 && EstSurUneLigne(LigneDuFichier(contenu, localisation))))
            {
                return RefusSurUneLigne("nouveau");
            }

            if (localisation.Occurrences == 0)
            {
                return EstSurUneLigne(ancienDesechappe)
                    ? RefusSurUneLigne("ancien")
                    : RefusAbsent(absolu, contenu, DenumeroterLarge(ancienNet) ?? ancienNet);
            }

            if (localisation.Occurrences > 1)
            {
                return $"Refusé : « ancien » apparaît {localisation.Occurrences.ToString(CultureInfo.InvariantCulture)} fois dans {absolu}. Donne un extrait plus long, avec les lignes voisines, pour qu'il n'apparaisse qu'une fois.";
            }

            var ancienFichier = localisation.Motif;
            if (ancienFichier.EndsWith('\n') && !ancienDesechappe.EndsWith('\n') && nouveauNet.Length > 0 && !nouveauNet.EndsWith('\n'))
            {
                nouveauNet += "\n";
            }

            var decalage = Decalage(contenu, localisation.Index, ancienFichier, extrait);
            nouveauNet = decalage.Length > 0 ? Reindenter(nouveauNet, decalage) : RetirerSurplus(nouveauNet, contenu, localisation.Index, ancienFichier, extrait);
            var nouveauFichier = AdapterFinsDeLigne(nouveauNet, localisation.Crlf);
            if (ancienFichier == nouveauFichier)
            {
                return "Refusé : « ancien » et « nouveau » sont identiques, il n'y a rien à changer.";
            }

            var index = localisation.Index;
            var nouveauContenu = string.Concat(contenu.AsSpan(0, index), nouveauFichier, contenu.AsSpan(index + ancienFichier.Length));
            var ecriture = await bac.ExecuterAsync($"cat > {Quoter(absolu)}", TimeSpan.FromSeconds(60), repertoireTravail, entree: nouveauContenu, ct: ct);
            if (!ecriture.Reussi)
            {
                return $"Erreur d'écriture : {ecriture.SortieComplete}";
            }

            var historique = HistoriqueDe(arguments);
            var connus = Visibles(absolu, historique, tourCourantCompris: true);
            vus[absolu] = Couvre(connus) ? [new Vu(historique, connus.Min(vu => vu.Tour), 1, int.MaxValue, connus.Max(vu => vu.VisibleDes))] : [];
            historique?.NoterEcriture(absolu);
            var debut = contenu.AsSpan(0, index).Count('\n') + 1;
            var lignesEntieres = (index == 0 || contenu[index - 1] == '\n') && (ancienFichier.EndsWith('\n') || index + ancienFichier.Length == contenu.Length);
            return ExecuteurProcessus.Tronquer(ResumerRemplacement(absolu, debut, ancienFichier, nouveauFichier, nouveauContenu, lignesEntieres), limites.TailleMaxSortieOutil);
        },
        new AIFunctionFactoryOptions
        {
            Name = "remplacer_dans_fichier",
            Description = $"Pour modifier une partie d'un fichier sans le réécrire : remplace exactement « ancien » par « nouveau » dans un fichier qui existe déjà. « ancien » doit apparaître une seule fois : recopie-le tel quel depuis lire_fichier, avec assez de lignes voisines pour qu'il soit unique (les numéros de ligne sont retirés). « nouveau » vide supprime « ancien ». Pour créer un fichier ou le réécrire en entier, utilise ecrire_fichier (écriture autorisée sous {racineEcriture}).",
            ConfigureParameterBinding = parametre => parametre.Name == "ancien" ? new AIFunctionFactoryOptions.ParameterBindingOptions { BindParameter = LireTexteFacultatif } : default,
        });

    private static string? LireTexteFacultatif(ParameterInfo parametre, AIFunctionArguments arguments) =>
        arguments.TryGetValue(parametre.Name!, out var valeur)
            ? valeur switch
            {
                null => null,
                string texte => texte,
                JsonElement { ValueKind: JsonValueKind.Null } => null,
                JsonElement { ValueKind: JsonValueKind.String } texte => texte.GetString(),
                JsonElement autre => autre.GetRawText(),
                _ => valeur.ToString(),
            }
            : null;

    public static string? MotifRefusForme(string contenu)
    {
        var net = contenu.Trim();
        var entoure = Enveloppe(net, "'''") || Enveloppe(net, "\"\"\"");
        var uneLigne = EstSurUneLigne(net);
        return (entoure, uneLigne) switch
        {
            (_, true) => "Refusé : le contenu tient sur une seule ligne, avec des « \\n » écrits en toutes lettres à la place des retours à la ligne. Renvoie le fichier avec de vrais retours à la ligne, sans l'entourer de guillemets.",
            (true, _) => "Refusé : le contenu est entouré de guillemets triples. Renvoie le fichier tel quel, sans l'entourer de quoi que ce soit.",
            _ => null,
        };
    }

    public static string TronquerListing(string sortie, int premiereLigne, int tailleMax)
    {
        if (sortie.Length <= tailleMax)
        {
            return sortie;
        }

        var lignes = sortie.Split('\n');
        var corps = PiedDeListing().IsMatch(lignes[^1]) ? lignes.Length - 1 : lignes.Length;
        var budget = tailleMax - ReserveTroncatureListing;
        var gardees = 0;
        var longueur = 0;
        while (gardees < corps && longueur + lignes[gardees].Length + 1 <= budget)
        {
            longueur += lignes[gardees].Length + 1;
            gardees++;
        }

        if (gardees == 0 || gardees == corps)
        {
            return ExecuteurProcessus.Tronquer(sortie, tailleMax);
        }

        var premiereOmise = premiereLigne + gardees;
        var derniereOmise = premiereLigne + corps - 1;
        var resultat = new StringBuilder();
        for (var i = 0; i < gardees; i++)
        {
            resultat.Append(lignes[i]).Append('\n');
        }

        resultat.Append(CultureInfo.InvariantCulture, $"[… lignes {premiereOmise} à {derniereOmise} omises : relis avec ligne_debut={premiereOmise}, nombre_lignes={Math.Min(derniereOmise - premiereOmise + 1, gardees)} …]");
        if (corps < lignes.Length)
        {
            resultat.Append('\n').Append(lignes[^1]);
        }

        return resultat.ToString();
    }

    private static bool Enveloppe(string net, string delimiteur)
    {
        var finPremiereLigne = net.IndexOf('\n');
        return finPremiereLigne >= 0
            && net[..finPremiereLigne].TrimEnd('\r') == delimiteur
            && net.EndsWith(delimiteur, StringComparison.Ordinal)
            && net.Split(delimiteur).Length == 3;
    }

    public static string Desechapper(string contenu)
    {
        if (contenu.Length < 2 || contenu[0] != '"' || contenu[^1] != '"')
        {
            return contenu;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(contenu) ?? contenu;
        }
        catch (System.Text.Json.JsonException)
        {
            return contenu;
        }
    }

    public static string Denumeroter(string contenu)
    {
        var precedent = contenu;
        var resultat = DenumeroterUnNiveau(contenu, strict: true);
        while (resultat != precedent)
        {
            precedent = resultat;
            resultat = DenumeroterUnNiveau(precedent, strict: false);
        }

        return resultat;
    }

    private static string DenumeroterUnNiveau(string contenu, bool strict)
    {
        var lignes = contenu.Split('\n');
        if (strict)
        {
            for (var i = 0; i < lignes.Length; i++)
            {
                var ligne = lignes[i].TrimEnd('\r');
                if (MarqueurDeTroncature().IsMatch(ligne))
                {
                    throw new ListingIncompletException($"contient à la ligne {i + 1} un marqueur de troncature (« {ligne} ») : il vient d'une sortie tronquée, des lignes manquent");
                }
            }
        }

        var fin = lignes.Length;
        if (fin > 0 && lignes[fin - 1].Length == 0)
        {
            fin--;
        }

        int? total = null;
        if (fin > 0 && PiedDeListing().Match(lignes[fin - 1]) is { Success: true } pied)
        {
            fin--;
            total = int.TryParse(pied.Groups["total"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var lignesDuFichier) ? lignesDuFichier : null;
        }

        if (fin == 0)
        {
            return contenu;
        }

        var resultat = new StringBuilder();
        int? premier = null;
        int? attendu = null;
        var blancs = 0;
        for (var i = 0; i < fin; i++)
        {
            if (attendu is not null && lignes[i].Trim().Length == 0)
            {
                blancs++;
                resultat.Append('\n');
                continue;
            }

            if (NumeroDeListing(lignes[i], out var longueurPrefixe) is not { } numero
                || (attendu is { } suite && numero != suite && numero != suite + blancs))
            {
                if (strict && premier is not null)
                {
                    throw new ListingIncompletException($"commence comme un listing numéroté de lire_fichier, mais la ligne {i + 1} n'en est pas la suite (« {Couper(lignes[i].TrimEnd('\r'), 60)} ») : ce n'est pas un fichier complet");
                }

                return contenu;
            }

            premier ??= numero;
            attendu = numero + 1;
            blancs = 0;
            resultat.Append(lignes[i], longueurPrefixe, lignes[i].Length - longueurPrefixe).Append('\n');
        }

        if (premier is { } debut && attendu is { } suivant && (debut != 1 || suivant - 1 + blancs < total))
        {
            throw new ExtraitNumeroteException(debut, suivant - 1, resultat.ToString());
        }

        return resultat.ToString();
    }

    public static string Resoudre(string repertoireTravail, string chemin)
    {
        var brut = chemin.Trim().Replace('\\', '/');
        var complet = brut.StartsWith('/') ? brut : $"{repertoireTravail.TrimEnd('/')}/{brut}";
        var segments = new List<string>();
        foreach (var segment in complet.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return "/" + string.Join('/', segments);
    }

    public static bool EstSous(string cheminAbsolu, IEnumerable<string> racines) =>
        racines.Any(r => cheminAbsolu == r || cheminAbsolu.StartsWith(r.TrimEnd('/') + "/", StringComparison.Ordinal));

    private static string? RefusHorsLecture(string absolu, string action) =>
        !EstSous(absolu, RacinesLecture)
            ? $"Erreur : {action} uniquement sous {string.Join(", ", RacinesLecture)}."
            : EstSous(absolu, [DossierDuLanceur]) ? RefusDossierDuLanceur : null;

    public static string Quoter(string valeur) => "'" + valeur.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string Echapper(string valeur) => valeur.Replace("'", "", StringComparison.Ordinal);

    private static bool EstSurUneLigne(string contenu)
    {
        var net = contenu.Trim();
        return !net.Contains('\n') && net.Length > 200 && net.Split("\\n").Length > 3;
    }

    private static string LigneDuFichier(string contenu, Localisation localisation)
    {
        var debut = localisation.Index == 0 ? 0 : contenu.LastIndexOf('\n', localisation.Index - 1) + 1;
        var fin = contenu.IndexOf('\n', localisation.Index + localisation.Motif.TrimEnd('\n').Length);
        return contenu[debut..(fin < 0 ? contenu.Length : fin)];
    }

    private static IEnumerable<string> SaufNomDeBase(string absolu, IEnumerable<string> noms)
    {
        var nomDeBase = absolu[(absolu.LastIndexOf('/') + 1)..];
        return noms.Where(nom => nom != nomDeBase);
    }

    private static HistoriqueVisible? HistoriqueDe(AIFunctionArguments arguments) =>
        arguments.Context is { } contexte && contexte.TryGetValue(typeof(HistoriqueVisible), out var historique) ? historique as HistoriqueVisible : null;

    private string ConditionEcrasementRefuse(string absolu) => ConditionEcrasementRefuseSur(Quoter(absolu));

    private string ConditionEcrasementRefuseSur(string mot) =>
        $"test -f {mot} && test \"$(wc -c < {mot})\" -gt {limites.TailleMaxSortieOutil.ToString(CultureInfo.InvariantCulture)} && ! grep -qF {string.Concat(MarqueursDeLUsine.Select(marqueur => $"-e {Quoter(marqueur)} "))}{mot}";

    private static string MesureDe(string mot) => $"wc -c < {mot}; awk 'END{{print NR}}' {mot}";

    private string VerificationSuppression(List<CibleSupprimee> cibles)
    {
        var listes = new StringBuilder();
        if (cibles.Where(cible => !cible.Recursif).Select(cible => MotifShell(cible.Chemin)).ToList() is { Count: > 0 } fichiers)
        {
            listes.Append(CultureInfo.InvariantCulture, $"for f in {string.Join(' ', fichiers)}; do test -f \"$f\" && echo \"$f\"; done; ");
        }

        if (cibles.Where(cible => cible.Recursif).Select(cible => MotifShell(cible.Chemin)).ToList() is { Count: > 0 } dossiers)
        {
            var elagage = string.Join(" -o ", RepertoiresExclusDeLaSuppression.Select(nom => $"-name {nom}"));
            listes.Append(CultureInfo.InvariantCulture, $"for d in {string.Join(' ', dossiers)}; do if test -d \"$d\"; then find \"$d\" \\( {elagage} \\) -prune -o -type f -print; elif test -f \"$d\"; then echo \"$d\"; fi; done; ");
        }

        return $"{{ {listes}}} | while IFS= read -r f; do {ConditionEcrasementRefuseSur("\"$f\"")} && grep -qI '' \"$f\" && {{ echo \"$f\"; {MesureDe("\"$f\"")}; }}; done; true";
    }

    private static string MotifShell(string absolu) => Quoter(absolu).Replace("*", "'*'", StringComparison.Ordinal).Replace("?", "'?'", StringComparison.Ordinal);

    private static List<FichierMesure> FichiersMesures(string sortie)
    {
        var lignes = sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fichiers = new List<FichierMesure>();
        for (var i = 0; i < lignes.Length; i += 3)
        {
            fichiers.Add(new FichierMesure(lignes[i], Mesurer(lignes[(i + 1)..Math.Min(i + 3, lignes.Length)])));
        }

        return fichiers;
    }

    private static Mesure? Mesurer(string[] lignes) =>
        lignes.Length >= 2
        && int.TryParse(lignes[0], NumberStyles.None, CultureInfo.InvariantCulture, out var octets)
        && int.TryParse(lignes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var total)
            ? new Mesure(octets, total)
            : null;

    private string ConseilLectures(string absolu, HistoriqueVisible? historique, Mesure? mesure)
    {
        if (mesure is null)
        {
            return ConseilEcrasementRefuse;
        }

        var octets = mesure.Octets.ToString(CultureInfo.InvariantCulture);
        var total = mesure.Lignes.ToString(CultureInfo.InvariantCulture);
        var lectures = Visibles(absolu, historique, tourCourantCompris: true);
        var vues = Fusionner(lectures, mesure.Lignes);
        if (vues.Count == 0)
        {
            return $"{ConseilEcrasementRefuse} Tu n'as aucune lecture de ce fichier sous les yeux : il fait {octets} octets et {total} lignes ; pour le lire en entier, appelle {Appels(absolu, [new Plage(1, mesure.Lignes)], mesure)}, puis écris-le au tour suivant.";
        }

        var manquantes = Manquantes(vues, mesure.Lignes);
        if (manquantes.Count > 0)
        {
            return $"{ConseilEcrasementRefuse} Tes lectures de ce fichier de {total} lignes couvrent les lignes {Enumerer(vues.Select(Libelle))} ; il te manque les lignes {Enumerer(manquantes.Select(Libelle))} : appelle {Appels(absolu, manquantes, mesure)}, puis écris-le au tour suivant.";
        }

        return historique is not null && lectures.Any(vu => vu.VisibleDes > historique.Tour)
            ? $"{ConseilEcrasementRefuse} Tes lectures de ce tour couvrent les {total} lignes de ce fichier, mais une lecture ne compte qu'à partir du tour suivant : écris-le au tour suivant, sans le supprimer."
            : $"{ConseilEcrasementRefuse} Le fichier a changé depuis tes lectures : il fait maintenant {octets} octets et {total} lignes ; relis-le en entier ({Appels(absolu, [new Plage(1, mesure.Lignes)], mesure)}), puis écris-le au tour suivant.";
    }

    private static List<Plage> Fusionner(List<Vu> lectures, int total)
    {
        var plages = new List<Plage>();
        foreach (var vu in lectures.Where(vu => vu.Debut <= total).OrderBy(vu => vu.Debut))
        {
            var fin = Math.Min(vu.Fin, total);
            if (plages.Count > 0 && vu.Debut <= plages[^1].Fin + 1)
            {
                plages[^1] = plages[^1] with { Fin = Math.Max(plages[^1].Fin, fin) };
            }
            else
            {
                plages.Add(new Plage(vu.Debut, fin));
            }
        }

        return plages;
    }

    private static List<Plage> Manquantes(List<Plage> vues, int total)
    {
        var manquantes = new List<Plage>();
        var suivante = 1;
        foreach (var vue in vues)
        {
            if (vue.Debut > suivante)
            {
                manquantes.Add(new Plage(suivante, vue.Debut - 1));
            }

            suivante = vue.Fin + 1;
        }

        if (suivante <= total)
        {
            manquantes.Add(new Plage(suivante, total));
        }

        return manquantes;
    }

    private string Appels(string absolu, List<Plage> plages, Mesure mesure)
    {
        var parLecture = LignesParLecture(mesure);
        var appels = new List<string>();
        foreach (var plage in plages)
        {
            for (var debut = plage.Debut; debut <= plage.Fin; debut += parLecture)
            {
                var nombre = Math.Min(parLecture, plage.Fin - debut + 1);
                appels.Add($"lire_fichier(chemin={absolu}, ligne_debut={debut.ToString(CultureInfo.InvariantCulture)}, nombre_lignes={nombre.ToString(CultureInfo.InvariantCulture)})");
            }
        }

        return Enumerer(appels);
    }

    private int LignesParLecture(Mesure mesure)
    {
        var largeur = mesure.Lignes > 0 ? (mesure.Octets + mesure.Lignes - 1) / mesure.Lignes : LargeurMoyenneLigneListing;
        return Math.Clamp((limites.TailleMaxSortieOutil - ReserveTroncatureListing - LongueurPiedDeListing) / (largeur + LongueurPrefixeDeListing), 1, MaxLignesLecture);
    }

    private static bool EstRegenere(string chemin) => FichiersRegeneres.Contains(Path.GetFileName(chemin), StringComparer.Ordinal);

    private static string Libelle(Plage plage) =>
        plage.Debut == plage.Fin
            ? plage.Debut.ToString(CultureInfo.InvariantCulture)
            : $"{plage.Debut.ToString(CultureInfo.InvariantCulture)} à {plage.Fin.ToString(CultureInfo.InvariantCulture)}";

    private static string Enumerer(IEnumerable<string> elements)
    {
        var liste = elements.ToList();
        return liste.Count <= 1 ? string.Concat(liste) : $"{string.Join(", ", liste.SkipLast(1))} et {liste[^1]}";
    }

    private static string DossierCourant(string repertoireTravail, MatchCollection changements, int index)
    {
        var dossier = repertoireTravail;
        foreach (var changement in changements.Where(changement => changement.Index < index))
        {
            dossier = Resoudre(dossier, changement.Groups["dossier"].Value.Trim('\'', '"'));
        }

        return dossier;
    }

    private static List<string> CiblesReecrites(string repertoireTravail, string commande)
    {
        var changements = ChangementDeDossier().Matches(commande);
        var cibles = new List<string>();
        foreach (Match reecriture in ReecritureParHeredoc().Matches(commande))
        {
            AjouterReecrite(cibles, Resoudre(DossierCourant(repertoireTravail, changements, reecriture.Index), reecriture.Groups["cible"].Value.Trim('\'', '"')));
        }

        foreach (Match copie in Copie().Matches(commande))
        {
            if (Arguments(copie.Groups["arguments"].Value).Operandes is { Count: >= 2 } operandes)
            {
                AjouterReecrite(cibles, Resoudre(DossierCourant(repertoireTravail, changements, copie.Index), operandes[^1].Trim('\'', '"')));
            }
        }

        return cibles;
    }

    private static void AjouterReecrite(List<string> cibles, string cible)
    {
        if (!EstSous(cible, ["/tmp"]) && !cibles.Contains(cible, StringComparer.Ordinal))
        {
            cibles.Add(cible);
        }
    }

    private static (List<string> Options, List<string> Operandes) Arguments(string arguments)
    {
        var mots = Jeton().Matches(arguments).Select(jeton => jeton.Value).ToList();
        var finDesOptions = mots.IndexOf("--");
        var options = mots.Where((mot, i) => (finDesOptions < 0 || i < finDesOptions) && mot.StartsWith('-')).ToList();
        var operandes = mots.Where((mot, i) => mot != "--" && !((finDesOptions < 0 || i < finDesOptions) && mot.StartsWith('-')) && !mot.Contains('<') && !mot.Contains('>')).ToList();
        return (options, operandes);
    }

    private static List<CibleSupprimee> CiblesSupprimees(string repertoireTravail, string commande)
    {
        var changements = ChangementDeDossier().Matches(commande);
        var cibles = new List<CibleSupprimee>();
        foreach (Match suppression in Suppression().Matches(commande))
        {
            var dossier = DossierCourant(repertoireTravail, changements, suppression.Index);
            var (options, operandes) = Arguments(suppression.Groups["arguments"].Value);
            var recursif = suppression.Groups["outil"].Value == "rm" && options.Any(option => option.StartsWith("--", StringComparison.Ordinal) ? option == "--recursive" : option.Contains('r') || option.Contains('R'));
            if (suppression.Groups["outil"].Value == "mv")
            {
                operandes = operandes.Count >= 2 ? operandes[..^1] : [];
            }

            foreach (var operande in operandes)
            {
                Ajouter(cibles, Resoudre(dossier, operande.Trim('\'', '"')), recursif);
            }
        }

        foreach (Match troncature in Troncature().Matches(commande))
        {
            Ajouter(cibles, Resoudre(DossierCourant(repertoireTravail, changements, troncature.Index), troncature.Groups["cible"].Value.Trim('\'', '"')), recursif: false);
        }

        return cibles;
    }

    private static void Ajouter(List<CibleSupprimee> cibles, string chemin, bool recursif)
    {
        var cible = new CibleSupprimee(chemin, recursif);
        if (chemin != "/" && !EstSous(chemin, ["/tmp"]) && !cibles.Contains(cible))
        {
            cibles.Add(cible);
        }
    }

    private static int? LignesVues(string listing, int debut)
    {
        var lignes = listing.TrimEnd().Split('\n');
        if (lignes.Any(ligne => CaracteresOmis().IsMatch(ligne))
            || PiedDeListing().Match(lignes[^1]) is not { Success: true } pied
            || !int.TryParse(pied.Groups["total"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
        {
            return null;
        }

        var suivante = debut;
        foreach (var ligne in lignes.SkipLast(1))
        {
            if (NumeroDeListing(ligne, out _) is not { } numero || numero != suivante)
            {
                break;
            }

            suivante++;
        }

        return suivante - 1 >= total ? int.MaxValue : suivante > debut ? suivante - 1 : null;
    }

    private static int? NumeroDeListing(string ligne, out int longueurPrefixe)
    {
        var prefixe = PrefixeDeListing().Match(ligne);
        longueurPrefixe = prefixe.Length;
        return prefixe.Success && int.TryParse(prefixe.Groups["numero"].ValueSpan.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var numero) ? numero : null;
    }

    private void Noter(string absolu, Vu vu)
    {
        if (!vus.TryGetValue(absolu, out var liste))
        {
            liste = [];
            vus[absolu] = liste;
        }

        liste.Add(vu);
    }

    private List<Vu> Visibles(string absolu, HistoriqueVisible? historique, bool tourCourantCompris) =>
        vus.TryGetValue(absolu, out var liste)
            ? liste.Where(vu => ReferenceEquals(vu.Historique, historique)
                && vu.Tour >= (historique?.PremierTourVisible ?? 0)
                && (tourCourantCompris || historique is null || vu.VisibleDes <= historique.Tour)).ToList()
            : [];

    private bool ConnuEnEntier(string absolu, HistoriqueVisible? historique) => Couvre(Visibles(absolu, historique, tourCourantCompris: false));

    private static Vu VuEnEntier(HistoriqueVisible? historique) => new(historique, historique?.Tour ?? 0, 1, int.MaxValue, historique?.Tour ?? 0);

    private static bool Couvre(List<Vu> connus)
    {
        var couvert = 0L;
        foreach (var vu in connus.OrderBy(vu => vu.Debut))
        {
            if (vu.Debut > couvert + 1)
            {
                return false;
            }

            couvert = Math.Max(couvert, vu.Fin);
        }

        return couvert == int.MaxValue;
    }

    private static string? MarqueurRepris(string texte) =>
        MarqueursDeLUsine.FirstOrDefault(candidat => ReprendLeMarqueur(texte, candidat) || ReprendLeMarqueur(Desechapper(texte), candidat));

    private static string? RefusMarqueurDeLUsine(string argument, string texte, string absolu) =>
        MarqueurRepris(texte) is { } marqueur
            ? $"Refusé : « {argument} » contient « {marqueur} », un marqueur de l'usine qui remplace dans ton historique un contenu retiré pour faire de la place : ce n'est pas le contenu du fichier, et {absolu} n'a pas été modifié. Relis le fichier avec lire_fichier, puis envoie le vrai contenu."
            : null;

    internal static bool EstUnMarqueurDeLUsine(string texte) =>
        MarqueurRepris(texte) is not null;

    private static bool ReprendLeMarqueur(string texte, string marqueur) =>
        texte.Contains(marqueur, StringComparison.Ordinal)
        || texte.Trim().StartsWith(marqueur[..marqueur.IndexOf(" pour faire de la place", StringComparison.Ordinal)], StringComparison.Ordinal);

    private static string RefusSurUneLigne(string argument) =>
        $"Refusé : « {argument} » tient sur une seule ligne, avec des « \\n » écrits en toutes lettres à la place des retours à la ligne. Renvoie le texte avec de vrais retours à la ligne.";

    private string RefusAbsent(string absolu, string contenu, string ancien)
    {
        var refus = $"Refusé : « ancien » n'apparaît pas dans {absolu}. Relis le fichier avec lire_fichier et recopie exactement le texte à remplacer, espaces et indentation compris.";
        if (PassageLePlusProche(contenu, ancien) is not { } passage)
        {
            return refus;
        }

        var entete = $"{refus}\nPassage le plus proche de « ancien », à recopier tel quel si c'est bien le code visé (sinon, ce code n'existe plus) :\n";
        var budget = limites.TailleMaxSortieOutil - entete.Length;
        if (budget <= ReserveTroncatureListing)
        {
            return refus;
        }

        var (debut, listing) = passage.Listing.Length > budget
            ? (passage.Aligne, string.Join('\n', passage.Listing.Split('\n')[(passage.Aligne - passage.Debut)..]))
            : (passage.Debut, passage.Listing);
        return entete + TronquerListing(listing, debut, budget);
    }

    private static (int Debut, int Aligne, string Listing)? PassageLePlusProche(string contenu, string ancien)
    {
        var lignesAncien = LignesSansFinDeLigne(ancien);
        var repere = Array.FindIndex(lignesAncien, ligne => Mot().IsMatch(ligne));
        if (repere < 0)
        {
            return null;
        }

        var motsAncien = lignesAncien.Select(Mots).ToArray();
        var mots = motsAncien[repere];
        var cle = lignesAncien[repere].Trim();
        var lignes = contenu.Split('\n');
        var total = NombreDeLignes(lignes);
        var motsLignes = lignes.Take(total).Select(ligne => Mots(ligne.TrimEnd('\r'))).ToArray();
        var decalages = new SortedSet<int>();
        for (var j = 0; j < total; j++)
        {
            for (var k = 0; k < lignesAncien.Length; k++)
            {
                var communs = motsAncien[k].Count(motsLignes[j].Contains);
                if (communs > 1 && communs * 2 > motsAncien[k].Count)
                {
                    decalages.Add(j - k);
                }
            }
        }

        int? meilleur = null;
        var meilleurClassement = (Fenetre: 0, Score: 0, Prefixe: 0);
        foreach (var decalage in decalages)
        {
            var fenetre = 0;
            for (var k = 0; k < lignesAncien.Length; k++)
            {
                var j = decalage + k;
                if (j >= 0 && j < total)
                {
                    fenetre += motsAncien[k].Count(motsLignes[j].Contains);
                }
            }

            var i = decalage + repere;
            var classement = i >= 0 && i < total
                ? (Fenetre: fenetre, Score: mots.Count(motsLignes[i].Contains), Prefixe: cle.AsSpan().CommonPrefixLength(lignes[i].Trim()))
                : (Fenetre: fenetre, Score: 0, Prefixe: 0);
            if (classement.CompareTo(meilleurClassement) > 0)
            {
                meilleur = decalage;
                meilleurClassement = classement;
            }
        }

        if (meilleur is not { } retenu)
        {
            return null;
        }

        var aligne = Math.Max(0, retenu);
        var debut = Math.Max(0, aligne - 2);
        var fin = Math.Min(total, retenu + lignesAncien.Length + 2);
        var listing = new StringBuilder();
        for (var i = debut; i < fin; i++)
        {
            listing.Append(LigneDeListing(i + 1, lignes[i])).Append('\n');
        }

        listing.Append(PiedDeListingTexte(total));
        return (debut + 1, aligne + 1, listing.ToString());
    }

    private static HashSet<string> Mots(string ligne) => Mot().Matches(ligne).Select(mot => mot.Value).ToHashSet(StringComparer.Ordinal);

    private static string[] LignesSansFinDeLigne(string texte) => texte.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');

    private static int NombreDeLignes(string[] lignes) => lignes[^1].Length == 0 ? lignes.Length - 1 : lignes.Length;

    private static string LigneDeListing(int numero, string ligne) => $"{numero.ToString(CultureInfo.InvariantCulture).PadLeft(6)}  {ligne.TrimEnd('\r')}";

    private static string PiedDeListingTexte(int total) => $"[fichier de {total.ToString(CultureInfo.InvariantCulture)} lignes]";

    private static string? PreparerExtrait(string argument, string texte, out string extrait)
    {
        try
        {
            extrait = DenumeroterExtrait(texte);
            return null;
        }
        catch (ListingIncompletException incomplet)
        {
            extrait = texte;
            return $"Refusé : « {argument} » {incomplet.Explication}. Relis le fichier par morceaux avec lire_fichier (ligne_debut, nombre_lignes) et renvoie le texte complet, sans numéros de ligne.";
        }
    }

    private static string DenumeroterExtrait(string contenu)
    {
        try
        {
            return Denumeroter(contenu);
        }
        catch (ExtraitNumeroteException extrait)
        {
            return DenumeroterExtrait(extrait.Contenu);
        }
    }

    private static Localisation Localiser(string contenu, string extrait)
    {
        var lf = extrait.Replace("\r\n", "\n", StringComparison.Ordinal);
        var localisation = LocaliserFormes(contenu, lf);
        if (!lf.EndsWith('\n') || contenu.EndsWith('\n'))
        {
            return localisation;
        }

        foreach (var forme in Formes(lf[..^1]))
        {
            if (forme.Length > 0 && contenu.EndsWith(forme, StringComparison.Ordinal))
            {
                var index = contenu.Length - forme.Length;
                return localisation.Occurrences == 0
                    ? new Localisation(forme, 1, index, FinDeLigneCrlf(contenu, index, forme))
                    : localisation with { Occurrences = localisation.Occurrences + 1 };
            }
        }

        return localisation;
    }

    private static Localisation LocaliserFormes(string contenu, string lf)
    {
        Localisation? premiere = null;
        var occurrences = 0;
        foreach (var forme in Formes(lf))
        {
            var nombre = Occurrences(contenu, forme);
            if (forme == lf && lf.StartsWith('\n') && lf.IndexOf('\n', 1) < 0)
            {
                nombre -= Occurrences(contenu, "\r" + lf);
            }

            if (nombre == 0)
            {
                continue;
            }

            occurrences += nombre;
            var index = contenu.IndexOf(forme, StringComparison.Ordinal);
            premiere ??= new Localisation(forme, 0, index, FinDeLigneCrlf(contenu, index, forme));
        }

        return premiere is null ? new Localisation(lf, 0, -1, false) : premiere with { Occurrences = occurrences };
    }

    private static string? DenumeroterLarge(string texte)
    {
        var lf = texte.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lignes = (lf.EndsWith('\n') ? lf[..^1] : lf).Split('\n');
        var resultat = new StringBuilder();
        var contenuNumerote = false;
        foreach (var ligne in lignes)
        {
            if (ligne.Trim().Length == 0)
            {
                resultat.Append('\n');
                continue;
            }

            var prefixe = PrefixeLarge().Match(ligne);
            if (!prefixe.Success)
            {
                return null;
            }

            contenuNumerote |= prefixe.Length < ligne.Length || prefixe.Value.EndsWith("  ", StringComparison.Ordinal);
            resultat.Append(ligne, prefixe.Length, ligne.Length - prefixe.Length).Append('\n');
        }

        if (!contenuNumerote)
        {
            return null;
        }

        return resultat.ToString();
    }

    private static Localisation LocaliserSouple(string contenu, string extrait)
    {
        var morceaux = extrait.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (morceaux.Length == 0)
        {
            return new Localisation(extrait, 0, -1, false);
        }

        try
        {
            var debutDeLigne = extrait[..(extrait.Length - extrait.TrimStart().Length)].Contains('\n') ? @"(?<=(?:\A|\n)[ \t]*)" : "";
            var finDeLigne = extrait[extrait.TrimEnd().Length..].Contains('\n') ? @"(?=[ \t]*(?:\r?\n|\z))" : "";
            var regex = new Regex(debutDeLigne + string.Join(@"\s+", morceaux.Select(Regex.Escape)) + finDeLigne, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            var premiere = regex.Match(contenu);
            if (!premiere.Success)
            {
                return new Localisation(extrait, 0, -1, false);
            }

            var occurrences = 0;
            for (var correspondance = premiere; correspondance.Success; correspondance = regex.Match(contenu, correspondance.Index + 1))
            {
                occurrences++;
            }

            var debut = DebutSouple(contenu, premiere.Index, extrait);
            var fin = FinSouple(contenu, premiere.Index + premiere.Length, extrait);
            var motif = contenu[debut..fin];
            return new Localisation(motif, occurrences, debut, FinDeLigneCrlf(contenu, debut, motif));
        }
        catch (RegexMatchTimeoutException)
        {
            return new Localisation(extrait, 0, -1, false);
        }
    }

    private static int DebutSouple(string contenu, int debut, string extrait)
    {
        var blancs = extrait[..(extrait.Length - extrait.TrimStart().Length)];
        if (blancs.Length == 0)
        {
            return debut;
        }

        while (debut > 0 && contenu[debut - 1] is ' ' or '\t')
        {
            debut--;
        }

        if (blancs.Contains('\n') && debut > 0 && contenu[debut - 1] == '\n')
        {
            debut--;
            if (debut > 0 && contenu[debut - 1] == '\r')
            {
                debut--;
            }
        }

        return debut;
    }

    private static int FinSouple(string contenu, int fin, string extrait)
    {
        var blancs = extrait[extrait.TrimEnd().Length..];
        if (blancs.Length == 0)
        {
            return fin;
        }

        while (fin < contenu.Length && contenu[fin] is ' ' or '\t')
        {
            fin++;
        }

        if (blancs.Contains('\n'))
        {
            if (fin < contenu.Length && contenu[fin] == '\r')
            {
                fin++;
            }

            if (fin < contenu.Length && contenu[fin] == '\n')
            {
                fin++;
            }
        }

        return fin;
    }

    private static string Decalage(string contenu, int index, string motif, string extrait)
    {
        var prefixe = contenu[(index == 0 ? 0 : contenu.LastIndexOf('\n', index - 1) + 1)..index];
        if (prefixe.Length == 0 || prefixe.Any(c => c is not (' ' or '\t')))
        {
            return "";
        }

        var lignesMotif = LignesSansFinDeLigne(motif);
        var lignesExtrait = LignesSansFinDeLigne(extrait);
        if (lignesMotif.Length != lignesExtrait.Length || (lignesExtrait.Skip(1).All(l => l.Trim().Length == 0) && Indentation(lignesExtrait[0]).Length == 0))
        {
            return "";
        }

        for (var i = 1; i < lignesMotif.Length; i++)
        {
            if (lignesMotif[i].Trim().Length > 0 && Indentation(lignesMotif[i]) != prefixe + Indentation(lignesExtrait[i]))
            {
                return "";
            }
        }

        return prefixe;
    }

    private static string Indentation(string ligne) => ligne[..(ligne.Length - ligne.TrimStart(' ', '\t').Length)];

    private static string Reindenter(string texte, string decalage)
    {
        var lignes = texte.Split('\n');
        for (var i = 1; i < lignes.Length; i++)
        {
            if (lignes[i].Trim().Length > 0)
            {
                lignes[i] = decalage + lignes[i];
            }
        }

        return string.Join('\n', lignes);
    }

    private static string RetirerSurplus(string texte, string contenu, int index, string motif, string extrait)
    {
        var depuis = index == 0 || contenu[index - 1] == '\n' ? 0 : 1;
        var lignesMotif = LignesSansFinDeLigne(motif);
        var lignesExtrait = LignesSansFinDeLigne(extrait);
        if (lignesMotif.Length != lignesExtrait.Length)
        {
            return texte;
        }

        var surplus = "";
        for (var i = depuis; i < lignesMotif.Length; i++)
        {
            if (lignesMotif[i].Trim().Length == 0)
            {
                continue;
            }

            var indentationMotif = Indentation(lignesMotif[i]);
            var indentationExtrait = Indentation(lignesExtrait[i]);
            if (!indentationExtrait.EndsWith(indentationMotif, StringComparison.Ordinal))
            {
                return texte;
            }

            var ecart = indentationExtrait[..^indentationMotif.Length];
            if (ecart.Length == 0 || (surplus.Length > 0 && ecart != surplus))
            {
                return texte;
            }

            surplus = ecart;
        }

        if (surplus.Length == 0)
        {
            return texte;
        }

        var lignes = texte.Split('\n');
        for (var i = depuis; i < lignes.Length; i++)
        {
            if (lignes[i].StartsWith(surplus, StringComparison.Ordinal))
            {
                lignes[i] = lignes[i][surplus.Length..];
            }
        }

        return string.Join('\n', lignes);
    }

    private static IEnumerable<string> Formes(string lf) =>
        lf.Contains('\n') ? [lf, lf.Replace("\n", "\r\n", StringComparison.Ordinal)] : [lf];

    private static bool FinDeLigneCrlf(string contenu, int index, string motif)
    {
        if (motif.Contains('\n'))
        {
            return motif.Contains("\r\n", StringComparison.Ordinal);
        }

        var fin = contenu.IndexOf('\n', index + motif.Length);
        if (fin < 0)
        {
            fin = contenu.LastIndexOf('\n', index);
        }

        return fin > 0 && contenu[fin - 1] == '\r';
    }

    private static string AdapterFinsDeLigne(string texte, bool crlf)
    {
        var lf = texte.Replace("\r\n", "\n", StringComparison.Ordinal);
        return crlf ? lf.Replace("\n", "\r\n", StringComparison.Ordinal) : lf;
    }

    private static int Occurrences(string texte, string motif)
    {
        if (motif.Length == 0)
        {
            return 0;
        }

        var nombre = 0;
        for (var index = texte.IndexOf(motif, StringComparison.Ordinal); index >= 0; index = texte.IndexOf(motif, index + 1, StringComparison.Ordinal))
        {
            nombre++;
        }

        return nombre;
    }

    private static int CompterLignes(string texte) =>
        texte.Length == 0 ? 0 : texte.AsSpan().Count('\n') + (texte.EndsWith('\n') ? 0 : 1);

    private static string ResumerRemplacement(string absolu, int debut, string ancienFichier, string nouveauFichier, string nouveauContenu, bool lignesEntieres)
    {
        var lignesAvant = CompterLignes(ancienFichier).ToString(CultureInfo.InvariantCulture);
        var lignesApres = CompterLignes(nouveauFichier);
        var fin = debut + lignesApres - 1;
        var lignes = nouveauContenu.Split('\n');
        var total = NombreDeLignes(lignes);
        var bornes = fin == debut
            ? $"ligne {debut.ToString(CultureInfo.InvariantCulture)}"
            : $"lignes {debut.ToString(CultureInfo.InvariantCulture)} à {fin.ToString(CultureInfo.InvariantCulture)}";
        var retour = new List<string>
        {
            (lignesApres, lignesEntieres) switch
            {
                (0, true) => $"Remplacé dans {absolu} : {lignesAvant} ligne(s) supprimée(s) (à la ligne {debut.ToString(CultureInfo.InvariantCulture)}).",
                (0, false) => $"Remplacé dans {absolu} : texte supprimé à la ligne {debut.ToString(CultureInfo.InvariantCulture)}.",
                _ => $"Remplacé dans {absolu} : {lignesAvant} ligne(s) remplacée(s) par {lignesApres.ToString(CultureInfo.InvariantCulture)} ({bornes}).",
            },
        };
        for (var numero = Math.Max(1, debut - 3); numero <= Math.Min(total, fin + 3); numero++)
        {
            retour.Add(LigneDeListing(numero, lignes[numero - 1]));
        }

        retour.Add(PiedDeListingTexte(total));
        return string.Join('\n', retour);
    }

    private sealed record Localisation(string Motif, int Occurrences, int Index, bool Crlf);

    private sealed record Vu(HistoriqueVisible? Historique, int Tour, int Debut, int Fin, int VisibleDes);

    private sealed record Plage(int Debut, int Fin);

    private sealed record Mesure(int Octets, int Lignes);

    private sealed record FichierMesure(string Chemin, Mesure? Mesure);

    private sealed record CibleSupprimee(string Chemin, bool Recursif);

    [GeneratedRegex(@"^(?<numero> {5}\d| {4}\d{2}| {3}\d{3}| {2}\d{4}| \d{5}|\d{6,}(?=  ))(?:  |$)")]
    private static partial Regex PrefixeDeListing();

    [GeneratedRegex(@"^\[fichier de (?<total>\d+) lignes\]$")]
    private static partial Regex PiedDeListing();

    [GeneratedRegex(@"^[ \t]*\d+(?:[ \t]{2}|[ \t]*$)")]
    private static partial Regex PrefixeLarge();

    [GeneratedRegex(@"^\[… (?:\d+ caractères omis|lignes \d+ à \d+ omises\b[^\]]*) …\]$")]
    private static partial Regex MarqueurDeTroncature();

    [GeneratedRegex(@"\w+")]
    private static partial Regex Mot();

    [GeneratedRegex(@"^\[… \d+ caractères omis …\]$")]
    private static partial Regex CaracteresOmis();

    [GeneratedRegex(@"\bcat\s*(?:>(?!>)\s*(?<cible>'[^']+'|""[^""]+""|[\w./-]+)\s*<<|<<-?\s*(?:'\w+'|""\w+""|\w+)\s*>(?!>)\s*(?<cible>'[^']+'|""[^""]+""|[\w./-]+)(?=[\s;&|)]|$))")]
    private static partial Regex ReecritureParHeredoc();

    [GeneratedRegex(@"(?:^|[;&|(\n]\s*)cd\s+(?<dossier>'[^']+'|""[^""]+""|[\w./-]+)(?=\s*(?:&&|;|\n|$))")]
    private static partial Regex ChangementDeDossier();

    [GeneratedRegex(@"(?:^|[;&|(\n]\s*)(?<outil>rm|mv)\s+(?<arguments>[^;&|\n)]+)")]
    private static partial Regex Suppression();

    [GeneratedRegex(@"(?:^|[;&|(\n]\s*)(?<outil>mv|cp)\s+(?<arguments>[^;&|\n)]+)")]
    private static partial Regex Copie();

    [GeneratedRegex(@"(?:^|(?:[;(\n]|&&|\|\|?)\s*)(?::\s*>(?!>)|>(?!>)|truncate\s+(?:-s\s*0+|--size[= ]0+)\s+)\s*(?<cible>'[^']+'|""[^""]+""|[^\s;&|()<>'""]+)(?=[\s;&|)]|$)")]
    private static partial Regex Troncature();

    [GeneratedRegex(@"'[^']*'|""[^""]*""|[^\s'""]+")]
    private static partial Regex Jeton();

    [GeneratedRegex(@"(?<=NODE_PATH=[""']?)/opt/acceptance-runner/node_modules(?=[""']?(?:[\s:;]|$))")]
    private static partial Regex NodePathDuLanceur();

    [GeneratedRegex(@"acceptance-runner|(?<![\w.-])/opt(?![\w.-])(?!/echantillons(?![\w.-])(?!/\.\.))|(?<![\w.-])/tmp/acc(?![\w.-])")]
    private static partial Regex DossierInterne();

    [GeneratedRegex(@"(?:^|[;&|(\n`]|\$\(|(?<![\w-])(?:ba)?sh\s+-c\s+['""])\s*(?:\w+=\S*\s+)*(?:(?:sudo|env|time|nice|exec|xargs|command(?:\s+-p)?|corepack|if|then|else|elif|(?<![(`]\s*)\{|timeout(?:\s+-\S+)*\s+\S+)\s+)*(?:[^\s;&|()'""`]*/)?(?:npx|bunx|(?:npm|pnpm)(?:\s+-\S+(?:\s+[^-\s;&|()<>]\S*)?)*\s+(?:install|i|ci|add|exec|x|dlx|pack|(?:init|create)(?=(?:\s+-\S+)*\s+[\w@]))|yarn(?:\s+-\S+(?:\s+[^-\s;&|()<>]\S*)?)*\s+(?:add|dlx|install)|(?:pip3?|python3?\s+-m\s+pip|uv\s+pip)(?:\s+-\S+(?:\s+[^-\s;&|()<>]\S*)?)*\s+install)(?![\w-])")]
    private static partial Regex Installation();

    [GeneratedRegex(@"(?:^|[;&|(\n`]|\$\(|(?<![\w-])(?:ba)?sh\s+-c\s+['""])\s*(?:\w+=\S*\s+)*(?:(?:sudo|env|time|nice|exec|xargs|timeout(?:\s+-\S+)*\s+\S+)\s+)*(?:[^\s;&|()'""`]*/)?(?:find(?:\s+(?:-[HLP](?=\s)|(?!-)(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+))*?|du(?:\s+(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+)*?|[ef]?grep(?=(?:\s+(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+)*?\s+(?:-[a-zA-Z]*[rR][a-zA-Z0-9]*|--(?:dereference-)?recursive)(?=[\s;&|)'""]|$))(?(?=(?:\s+(?!--(?=[\s;&|)]|$))(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+)*?\s+(?:-[a-zA-Z-[ABCDdmef]]*[ef]|--(?:regexp|file)(?=[\s=])))(?:\s+(?:-(?(?=(?:[a-zA-Z-[ABCDdmef]]*[ABCDdmef]|-(?:include|exclude(?:-dir|-from)?|(?:after-|before-)?context|max-count|directories|devices|label|binary-files|group-separator|regexp|file))\s)[a-zA-Z-]+\s+(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+|(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])*)|(?!-)(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+))*?|(?:\s+-(?(?=(?:[a-zA-Z-[ABCDdmef]]*[ABCDdmef]|-(?:include|exclude(?:-dir|-from)?|(?:after-|before-)?context|max-count|directories|devices|label|binary-files|group-separator|regexp|file))\s)[a-zA-Z-]+\s+(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+|(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])*))*\s+(?:--\s+(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+|(?!-)(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+)(?:\s+(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+)*?)|ls(?=(?:\s+(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+)*?\s+(?:-[a-zA-Z]*R[a-zA-Z0-9]*|--recursive)(?=[\s;&|)'""]|$))(?:\s+(?:'[^']*'|""[^""]*""|[^\s;&|'""()<>`])+)*?)\s+(?:'/[.*]?'|""/[.*]?""|/[.*]?)(?=[\s;&|)<>`'""]|$)")]
    private static partial Regex ParcoursDeLaRacine();
}

public sealed class ExtraitNumeroteException(int debut, int fin, string contenu) : InvalidOperationException($"extrait numéroté (lignes {debut.ToString(CultureInfo.InvariantCulture)} à {fin.ToString(CultureInfo.InvariantCulture)})")
{
    public int Debut { get; } = debut;

    public int Fin { get; } = fin;

    public string Contenu { get; } = contenu;
}

public sealed class ListingIncompletException(string explication) : InvalidOperationException($"listing incomplet : {explication}")
{
    public string Explication { get; } = explication;
}
