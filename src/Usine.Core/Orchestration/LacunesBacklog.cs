using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Usine.Core.Etat;

namespace Usine.Core.Orchestration;

internal sealed record Lacune(string Message, string? Correction = null);

internal sealed class RappelLacunes
{
    public bool Fait { get; set; }
}

internal static partial class LacunesBacklog
{
    private const int LongueurMinCitation = 2;

    private const int LongueurMaxCitation = 60;

    private const int DecimalesMaxExactes = 6;

    private const int DecimalesAffichees = 4;

    private const string CodeTropVolumineux = "413";

    public static List<Lacune> Trouver(Backlog backlog, Demande demande)
    {
        var besoin = string.Join('\n', demande.TextesDuBesoin);
        var criteres = backlog.Stories.SelectMany(s => s.Criteres).ToList();
        var sources = criteres.Select(c => (Nom: c.Id, Sujet: c.Id, c.Texte))
            .Concat(backlog.Hypotheses.Select(h => (Nom: "une hypothèse", Sujet: "Une hypothèse", Texte: h)))
            .ToList();
        var lacunes = new List<Lacune>();
        foreach (var (nom, sujet, texte) in sources)
        {
            lacunes.AddRange(Calculs(nom, sujet, texte));
        }

        lacunes.AddRange(Citations(backlog, demande.Texte));
        lacunes.AddRange(Enumerations(backlog, demande.Texte));
        lacunes.AddRange(Quantificateurs(criteres, besoin));
        lacunes.AddRange(Limites(criteres, besoin));
        return [.. lacunes.DistinctBy(l => l.Message, StringComparer.Ordinal)];
    }

    private static IEnumerable<Lacune> Calculs(string nom, string sujet, string texte)
    {
        var normalise = NormaliserCalcul(texte);
        foreach (Match egalite in Egalite().Matches(normalise))
        {
            var gauche = egalite.Groups["gauche"];
            var jetons = Jeton().Matches(gauche.Value).ToList();
            var debut = gauche.Index + gauche.Value.Length - gauche.Value.TrimStart().Length;
            var premier = debut > 0 && char.IsLetterOrDigit(normalise[debut - 1]) ? 1 : 0;
            var calcul = Enumerable.Range(premier, Math.Max(jetons.Count - premier, 0))
                .Where(i => i == 0 || (jetons[i].Value == "(" && char.IsWhiteSpace(normalise[gauche.Index + jetons[i].Index - 1])) || (!Operateur().IsMatch(jetons[i - 1].Value) && !char.IsDigit(jetons[i - 1].Value[0])))
                .Select(i => (Debut: gauche.Index + jetons[i].Index, Valeur: Evaluer(gauche.Value[jetons[i].Index..])))
                .FirstOrDefault(c => c.Valeur is not null);
            if (calcul.Valeur is not { } exact)
            {
                continue;
            }

            var extrait = texte[calcul.Debut..(egalite.Index + egalite.Length)].Trim();
            var virgule = VirguleDecimale().IsMatch(extrait);
            var valeur = egalite.Groups["valeur"].Value;
            var tronque = egalite.Groups["tronque"].Success;
            var arrondi = egalite.Groups["arrondi"];
            var valeurExacte = Fraction.Lire(valeur);
            var uniteValeur = Fraction.Unite(Decimales(valeur));
            if (tronque && !arrondi.Success)
            {
                if ((exact - valeurExacte).Abs() >= uniteValeur)
                {
                    yield return CalculFaux(nom, sujet, extrait, valeur, exact, null, virgule);
                }

                continue;
            }

            var annonce = arrondi.Success ? arrondi.Value : valeur;
            var decimales = Decimales(annonce);
            if (Arrondir(exact, decimales) is not { } attendu)
            {
                yield return new Lacune($"Calcul pile au milieu dans {nom} : « {extrait} » : la valeur exacte {FormaterExact(exact, virgule)} tombe pile entre deux arrondis à {Precision(decimales)}, sur lesquels les règles usuelles divergent. Choisis un exemple qui ne tombe pas pile au milieu, ou un milieu positif avec un chiffre impair avant le 5.");
                continue;
            }

            var egaliteFausse = arrondi.Success && (tronque
                ? (exact - valeurExacte).Abs() >= uniteValeur
                : (exact - valeurExacte).Abs() * Fraction.Deux > uniteValeur);
            var zeroSigne = attendu.EstNul && annonce.StartsWith('-');
            if (egaliteFausse || Fraction.Lire(annonce) != attendu || zeroSigne)
            {
                yield return CalculFaux(nom, sujet, extrait, annonce, exact, (attendu, decimales), virgule);
            }
        }
    }

    private static Lacune CalculFaux(string nom, string sujet, string extrait, string annonce, Fraction exact, (Fraction Valeur, int Decimales)? attendu, bool virgule)
    {
        var texteExact = FormaterExact(exact, virgule);
        var annonceAffichee = virgule ? annonce.Replace('.', ',') : annonce;
        if (attendu is not { } arrondi)
        {
            return new Lacune(
                $"Calcul faux dans {nom} : « {extrait} » : le calcul exact donne {texteExact}. Corrige l'attendu.",
                $"{sujet} annonce {annonceAffichee} ; le calcul exact donne {texteExact} : écris le test avec la valeur exacte et note-la dans CONTRAT.md");
        }

        var texteAttendu = Formater(arrondi.Valeur, arrondi.Decimales, virgule);
        var sansSigne = arrondi.Valeur.EstNul ? ", sans signe" : "";
        var calcul = arrondi.Valeur == exact ? texteExact : $"{texteExact} → {texteAttendu}";
        var soit = arrondi.Valeur == exact ? sansSigne : $", soit {texteAttendu} arrondi à {Precision(arrondi.Decimales)}{sansSigne}";
        return new Lacune(
            $"Calcul faux dans {nom} : « {extrait} » : le calcul exact donne {texteExact}{soit}. Corrige l'attendu.",
            $"{sujet} annonce {annonceAffichee} ; le calcul exact donne {calcul} : écris le test avec {texteAttendu} et note-le dans CONTRAT.md");
    }

    private static Fraction? Arrondir(Fraction exact, int decimales)
    {
        var echelle = BigInteger.Pow(10, decimales);
        var absolu = exact.Abs() * new Fraction(echelle, 1);
        var entier = absolu.Numerateur / absolu.Denominateur;
        var reste = absolu - new Fraction(entier, 1);
        var negatif = exact.Numerateur.Sign < 0;
        var retenu = (reste * Fraction.Deux).Comparer(Fraction.Un) switch
        {
            < 0 => entier,
            > 0 => entier + 1,
            _ when negatif || entier.IsEven => (BigInteger?)null,
            _ => entier + 1,
        };
        return retenu is { } valeur ? new Fraction(negatif ? -valeur : valeur, echelle) : null;
    }

    private static IEnumerable<Lacune> Citations(Backlog backlog, string demande)
    {
        var contenu = Comparable(string.Join('\n', TextesDuBacklog(backlog)));
        var citations = Citation().Matches(demande)
            .Select(m => m.Groups["texte"].Value.Trim())
            .Where(c => c.Length is >= LongueurMinCitation and <= LongueurMaxCitation)
            .Distinct(StringComparer.Ordinal);
        foreach (var citation in citations)
        {
            var motif = string.Join(@"\s+", Orchestrateur.Blancs().Split(citation).Where(m => m.Length > 0).Select(m => LettreIsolee().IsMatch(m)
                ? $@"(?:\d+|{Regex.Escape(m.ToLowerInvariant())})"
                : Regex.Escape(Comparable(m))));
            if (!Regex.IsMatch(contenu, motif, RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                yield return new Lacune($"La demande cite « {citation} » : reprends ce libellé tel quel dans les critères.");
            }
        }
    }

    private static IEnumerable<Lacune> Enumerations(Backlog backlog, string demande)
    {
        var textes = backlog.Stories.SelectMany(s => s.Criteres).Select(c => c.Texte)
            .Concat(backlog.Hypotheses)
            .Concat(backlog.HorsPerimetre)
            .Append($"{backlog.Stack.Choix} {backlog.Stack.Justification}")
            .Select(MotsEnumeration)
            .ToList();
        foreach (Match groupe in Parentheses().Matches(demande))
        {
            var elements = SeparateurEnumeration().Split(groupe.Groups["contenu"].Value)
                .Select(e => e.Trim())
                .Where(e => MotsEnumeration(e).Count > 0)
                .ToList();
            if (elements.Count < 2)
            {
                continue;
            }

            foreach (var element in elements)
            {
                var mots = MotsEnumeration(element);
                if (!textes.Any(t => mots.Count(t.Contains) * 3 >= mots.Count * 2))
                {
                    yield return new Lacune($"La demande énumère {groupe.Value} : aucun critère ne reprend « {element} ». Couvre chaque élément de l'énumération.");
                }
            }
        }
    }

    private static IEnumerable<Lacune> Quantificateurs(IEnumerable<Critere> criteres, string besoin)
    {
        var besoinComparable = Comparable(besoin);
        foreach (var critere in criteres)
        {
            var comparable = Orchestrateur.SansAccents(critere.Texte).ToLowerInvariant();
            if (Alors().Match(comparable) is not { Success: true } alors)
            {
                continue;
            }

            foreach (Match vague in QuantificateurVague().Matches(comparable, alors.Index))
            {
                if (besoinComparable.Contains(Orchestrateur.Blancs().Replace(vague.Groups["quantificateur"].Value, " "), StringComparison.Ordinal))
                {
                    continue;
                }

                var extrait = comparable.Length == critere.Texte.Length ? critere.Texte.Substring(vague.Index, vague.Length) : vague.Value;
                yield return new Lacune($"{critere.Id} se contente de « {extrait} » : vérifie la valeur exacte attendue, sauf si la demande fixe ce seuil.");
            }
        }
    }

    private static IEnumerable<Lacune> Limites(IEnumerable<Critere> criteres, string besoin)
    {
        var nombres = Nombre().Matches(besoin).Select(m => m.Value.Replace(',', '.')).ToHashSet(StringComparer.Ordinal);
        if (nombres.Contains(CodeTropVolumineux))
        {
            yield break;
        }

        foreach (var critere in criteres.Where(c => CodeLimite().IsMatch(c.Texte)))
        {
            var limites = Limite().Matches(critere.Texte);
            var nonFixee = limites.FirstOrDefault(m => !nombres.Contains(m.Groups["nombre"].Value.Replace(',', '.')));
            if (limites.Count > 0 && nonFixee is null)
            {
                continue;
            }

            var limite = nonFixee?.Value.Trim() ?? $"HTTP {CodeTropVolumineux}";
            yield return new Lacune($"{critere.Id} impose une limite ({limite}) que la demande ne fixe pas : retire-la, ou cite la phrase de la demande qui la justifie.");
        }
    }

    private static IEnumerable<string> TextesDuBacklog(Backlog backlog) =>
        backlog.Stories.SelectMany(s => s.Criteres.Select(c => c.Texte).Prepend(s.Description).Prepend(s.Titre))
            .Concat(backlog.Hypotheses)
            .Concat(backlog.HorsPerimetre)
            .Prepend($"{backlog.Stack.Choix} {backlog.Stack.Justification}")
            .Prepend(backlog.Resume)
            .Prepend(backlog.Titre);

    private static HashSet<string> MotsEnumeration(string texte) =>
        Orchestrateur.MotSignificatif().Matches(Orchestrateur.SansAccents(texte).ToLowerInvariant()).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    private static string Comparable(string texte) =>
        Orchestrateur.Blancs().Replace(Orchestrateur.SansAccents(texte).ToLowerInvariant().Replace('’', '\''), " ").Trim();

    private static string NormaliserCalcul(string texte)
    {
        var caracteres = texte.Replace('−', '-').Replace('÷', '/').Replace('×', '*').ToCharArray();
        foreach (Match fois in FoisEntreNombres().Matches(texte))
        {
            caracteres[fois.Index] = '*';
        }

        foreach (Match virgule in VirguleDecimale().Matches(texte))
        {
            caracteres[virgule.Index + 1] = '.';
        }

        return new string(caracteres);
    }

    private static int Decimales(string nombre) => nombre.IndexOf('.', StringComparison.Ordinal) is var point and >= 0 ? nombre.Length - point - 1 : 0;

    private static string Precision(int decimales) => decimales switch
    {
        0 => "l'unité",
        1 => "un chiffre après la virgule",
        _ => $"{decimales.ToString(CultureInfo.InvariantCulture)} chiffres après la virgule",
    };

    private static string FormaterExact(Fraction valeur, bool virgule)
    {
        for (var decimales = 0; decimales <= DecimalesMaxExactes; decimales++)
        {
            if ((valeur * new Fraction(BigInteger.Pow(10, decimales), 1)).Denominateur.IsOne)
            {
                return Formater(valeur, decimales, virgule);
            }
        }

        var echelle = BigInteger.Pow(10, DecimalesAffichees);
        var tronque = valeur * new Fraction(echelle, 1);
        return $"{Formater(new Fraction(tronque.Numerateur / tronque.Denominateur, echelle), DecimalesAffichees, virgule)}…";
    }

    private static string Formater(Fraction valeur, int decimales, bool virgule)
    {
        var echelle = BigInteger.Pow(10, decimales);
        var mise = valeur.Abs() * new Fraction(echelle, 1);
        var entier = mise.Numerateur / mise.Denominateur;
        var signe = valeur.Numerateur.Sign < 0 && !entier.IsZero ? "-" : "";
        var partieEntiere = (entier / echelle).ToString(CultureInfo.InvariantCulture);
        return decimales == 0
            ? $"{signe}{partieEntiere}"
            : $"{signe}{partieEntiere}{(virgule ? ',' : '.')}{(entier % echelle).ToString(CultureInfo.InvariantCulture).PadLeft(decimales, '0')}";
    }

    private static Fraction? Evaluer(string expression)
    {
        var jetons = Jeton().Matches(expression).Select(m => m.Value).ToList();
        if (string.Concat(jetons).Length != Orchestrateur.Blancs().Replace(expression, "").Length)
        {
            return null;
        }

        var lecteur = new LecteurExpression(jetons);
        var valeur = lecteur.Somme();
        return valeur is not null && lecteur.Termine && lecteur.Binaire ? valeur : null;
    }

    private sealed class LecteurExpression(List<string> jetons)
    {
        private int position;

        public bool Termine => position == jetons.Count;

        public bool Binaire { get; private set; }

        private string? Courant => position < jetons.Count ? jetons[position] : null;

        public Fraction? Somme()
        {
            var valeur = Produit();
            while (valeur is not null && Courant is "+" or "-")
            {
                var operateur = jetons[position++];
                Binaire = true;
                var droite = Produit();
                valeur = droite is null ? null : operateur == "+" ? valeur + droite.Value : valeur - droite.Value;
            }

            return valeur;
        }

        private Fraction? Produit()
        {
            var valeur = Facteur();
            while (valeur is not null && Courant is "*" or "/")
            {
                var operateur = jetons[position++];
                Binaire = true;
                var droite = Facteur();
                valeur = droite switch
                {
                    null => null,
                    { EstNul: true } when operateur == "/" => null,
                    _ when operateur == "*" => valeur * droite.Value,
                    _ => valeur / droite.Value,
                };
            }

            return valeur;
        }

        private Fraction? Facteur()
        {
            switch (Courant)
            {
                case "-":
                    position++;
                    return Facteur() is { } oppose ? -oppose : null;
                case "(":
                    position++;
                    var interieur = Somme();
                    if (interieur is null || Courant != ")")
                    {
                        return null;
                    }

                    position++;
                    return interieur;
                case { } nombre when char.IsDigit(nombre[0]):
                    position++;
                    return Fraction.Lire(nombre);
                default:
                    return null;
            }
        }
    }

    private readonly record struct Fraction
    {
        public Fraction(BigInteger numerateur, BigInteger denominateur)
        {
            var diviseur = BigInteger.GreatestCommonDivisor(numerateur, denominateur);
            var signe = denominateur.Sign < 0 ? -1 : 1;
            Numerateur = signe * numerateur / diviseur;
            Denominateur = signe * denominateur / diviseur;
        }

        public static Fraction Un => new(1, 1);

        public static Fraction Deux => new(2, 1);

        public BigInteger Numerateur { get; }

        public BigInteger Denominateur { get; }

        public bool EstNul => Numerateur.IsZero;

        public static Fraction Lire(string nombre)
        {
            var decimales = Decimales(nombre);
            return new Fraction(BigInteger.Parse(nombre.Replace(".", "", StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), BigInteger.Pow(10, decimales));
        }

        public static Fraction Unite(int decimales) => new(1, BigInteger.Pow(10, decimales));

        public Fraction Abs() => new(BigInteger.Abs(Numerateur), Denominateur);

        public int Comparer(Fraction autre) => (Numerateur * autre.Denominateur).CompareTo(autre.Numerateur * Denominateur);

        public static Fraction operator +(Fraction a, Fraction b) => new((a.Numerateur * b.Denominateur) + (b.Numerateur * a.Denominateur), a.Denominateur * b.Denominateur);

        public static Fraction operator -(Fraction a, Fraction b) => a + (-b);

        public static Fraction operator -(Fraction a) => new(-a.Numerateur, a.Denominateur);

        public static Fraction operator *(Fraction a, Fraction b) => new(a.Numerateur * b.Numerateur, a.Denominateur * b.Denominateur);

        public static Fraction operator /(Fraction a, Fraction b) => new(a.Numerateur * b.Denominateur, a.Denominateur * b.Numerateur);

        public static bool operator >=(Fraction a, Fraction b) => a.Comparer(b) >= 0;

        public static bool operator <=(Fraction a, Fraction b) => a.Comparer(b) <= 0;

        public static bool operator >(Fraction a, Fraction b) => a.Comparer(b) > 0;

        public static bool operator <(Fraction a, Fraction b) => a.Comparer(b) < 0;
    }

    [GeneratedRegex(@"(?<gauche>[0-9. \t\u00A0\u202F+\-*/()]+)=[ \t]*[""«“]?[ \t]*(?<valeur>(?>-?\d+(?:\.\d+)?))(?!\d|[ \t\u00A0\u202F]\d{3}(?!\d)|[ \t\u00A0\u202F]*(?:(?:…|\.\.\.)[ \t\u00A0\u202F]*)?%)(?<tronque>[ \t]*(?:…|\.\.\.))?(?:[ \t\u00A0\u202F]*(?:€|\$|£|°[CF]?))?[ \t]*[""»”]?(?:[ \t]*,?[ \t]*(?:→|->|≈|soit|donne|arrondie?(?:[ \t]+[àa])?)[ \t]*[""«“]?[ \t]*(?<arrondi>(?>-?\d+(?:\.\d+)?))(?!\d|[ \t\u00A0\u202F]\d{3}(?!\d)|[ \t\u00A0\u202F]*(?:(?:…|\.\.\.)[ \t\u00A0\u202F]*)?%)(?:[ \t\u00A0\u202F]*(?:€|\$|£|°[CF]?))?[ \t]*[""»”]?)?")]
    private static partial Regex Egalite();

    [GeneratedRegex(@"(?<=[\d)][ \t]*)[x×](?=[ \t]*[\d(])")]
    private static partial Regex FoisEntreNombres();

    [GeneratedRegex(@"\d,(?=\d)")]
    private static partial Regex VirguleDecimale();

    [GeneratedRegex(@"\d+(?:\.\d+)?|[-+*/()]")]
    private static partial Regex Jeton();

    [GeneratedRegex(@"^[-+*/]$")]
    private static partial Regex Operateur();

    [GeneratedRegex(@"«\s*(?<texte>[^«»\n]+?)\s*»|""(?<texte>[^""\n]+)""|“(?<texte>[^”\n]+)”")]
    private static partial Regex Citation();

    [GeneratedRegex(@"^[A-Z]$")]
    private static partial Regex LettreIsolee();

    [GeneratedRegex(@"\((?<contenu>[^()]+)\)")]
    private static partial Regex Parentheses();

    [GeneratedRegex(@",|\bou\b")]
    private static partial Regex SeparateurEnumeration();

    [GeneratedRegex(@"\balors\b")]
    private static partial Regex Alors();

    [GeneratedRegex(@"(?:(?<quantificateur>≥|>=)\s*|\b(?<quantificateur>superieure?s?\s+ou\s+egale?s?)\s+au?\s+)(?![1-5]\d\d\b)\d+")]
    private static partial Regex QuantificateurVague();

    [GeneratedRegex(@"(?<nombre>\d+(?:[.,]\d+)?)\s*(?:Mo|Ko|Go|octets?|caract[eè]res?|secondes?|minutes?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Limite();

    [GeneratedRegex(@"\b413\b")]
    private static partial Regex CodeLimite();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex Nombre();
}
