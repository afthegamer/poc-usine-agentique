import { chromium, request } from 'playwright';
import { randomUUID } from 'node:crypto';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const debut = Date.now();

function argument(nom, defaut) {
  const position = process.argv.indexOf(`--${nom}`);
  return position >= 0 && position + 1 < process.argv.length ? process.argv[position + 1] : defaut;
}

const options = {
  journal: argument('journal', '/tmp/usine-journal'),
  base: argument('base', 'http://localhost:8080'),
  contrat: argument('contrat', '/acceptance/CONTRAT.md'),
  delai: Number(argument('delai', '150')) * 1000,
  maxRejeux: Number(argument('max-rejeux', '400')),
};

const MaxGestes = 6;
const MaxJeuxParCombinaison = 3;
const MaxJeuxParGeste = 9;
const MaxEcarts = 12;
const LongueurMessage = 400;
const CalmeReference = 50;
const CalmeApresClic = 150;
const AttenteMax = 2000;
const DelaiAction = 2000;
const DelaiChargement = 10000;
const MaxDenominateur = 1000;
const MaxPentes = 20;
const MaxVerifications = 2000000;
const MinObservations = 30;
const MilieuxClasses = 6;
const MilieuxAuHasard = 2;
const Grille = [-1000, -500, -200, -100, -50, -20, -10, -5, -2, -1, 0, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];
const PasGrilleFormule = 3713;
const Numerique = /^\s*[+-]?\d+([.,]\d+)?\s*$/;
const ZeroSigne = /^\s*[+-]0+([.,]0+)?\s*$/;
const TypesTexte = new Set(['text', 'search', 'tel', 'textarea']);
const TypesTexteLibre = new Set(['text', 'search', 'textarea']);
const Temoin = '<b data-usine="1">x</b><img src=x onerror="window.__usineXss=1"> $& {{a}} 100% %41 é€';
const MorceauxTemoin = ['$&', '{{a}}', '100%', '%41', 'é€'];
const Motifs = [
  ['zero-negatif', /(?:^|[\s(:=>«"'])[-−‑]0(?:[.,]0+)?(?![\d.,])/g],
  ['undefined', /\bundefined\b/g],
  ['NaN', /\bNaN\b/g],
  ['objet', /\[object (?:Object|Promise|Array|HTMLElement)\]/g],
  ['trace', /^\s*at (?:[^\n(]*\()?(?:file:\/\/|\/|[A-Za-z]:[\\/]|node:)[^\n]*:\d+:\d+\)?\s*$|Traceback \(most recent call last\)|^\s*File "[^"]+", line \d+|\bnode_modules\/|\.cs:line \d+/gm],
];
const DelaiApi = 30000;
const MaxRequetesApi = 120;
const MaxRoutesApi = 8;
const MaxChampsApi = 4;
const MaxEcartsApi = 8;
const MaxPagesApi = 3;
const DelaiRequeteApi = 5000;
const LongueurReponseApi = 200;
const MethodesApi = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const SegmentUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DateIso = /^\d{4}-\d{2}-\d{2}$/;
const TraceApi = new RegExp(`${Motifs.find(([motif]) => motif === 'trace')[1].source}|Exception|/tmp/usine-app`, 'm');
const Entites = { '&nbsp;': ' ', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&#39;': "'", '&amp;': '&' };
const ConsigneApi = "Une requête mal formée ou un champ de mauvais type reçoit une erreur 4xx en JSON, sans trace de pile ni modification : un gestionnaire d'erreurs commun à toutes les routes, chaque champ validé avant d'écrire.";
const ConsigneCoherence = 'La liste et le détail doivent renvoyer la même donnée enregistrée.';
const ConsigneHorsContrat = 'Une méthode que le contrat ne prévoit pas sur ce chemin est refusée (404 ou 405) sans rien modifier.';
const ConsigneTexte = "Enregistre et renvoie le texte tel qu'il a été envoyé, et réaffiche-le comme du texte brut (textContent), sans l'interpréter.";
const DelaiConcurrence = 20000;
const MaxGestesConcurrence = 5;
const MaxEntreesConcurrence = 5;
const AttenteRequete = 1000;
const DelaiClicConcurrence = 500;
const Echantillons = '/opt/echantillons';
const TypesRetardes = new Set(['fetch', 'xhr']);
const ConsigneConcurrence = "Ignore la réponse d'un envoi dépassé par un plus récent (numéro d'envoi, AbortController).";
const EcarteeRefuseeParLeServeur = 'refusée(s) par le serveur';
const EcarteeSansRequete = 'sans requête envoyée';
const EcarteeAlerte = 'avec une alerte affichée';
const EcarteeInchangee = "sans changement d'affichage";
const EcarteeCommeRefusee = "au même affichage qu'une entrée refusée";
const EcarteeCommePremiere = 'au même affichage que la première';
const EcarteeNonRejouable = 'non rejouable(s)';
const MotifsEnClair = {
  'zero-negatif': "zéro négatif, un zéro s'affiche sans signe",
  undefined: 'undefined, une valeur absente affichée telle quelle',
  NaN: "NaN, un calcul sur une valeur qui n'est pas un nombre",
  objet: 'objet non converti en texte',
  trace: 'trace de pile',
};

class Arret extends Error {}

const ecarts = [];
const clesEcarts = new Set();
const formules = [];
let rejeux = 0;
let arret = null;
let gestesRejoues = 0;
let gestesInstables = 0;
let suffixes = 0;

const origine = (() => {
  try {
    return new URL(options.base).origin;
  } catch {
    return null;
  }
})();

function attendre(millisecondes) {
  return new Promise((resoudre) => setTimeout(resoudre, millisecondes));
}

function normaliser(texte) {
  return String(texte).replace(/\s+/g, ' ').trim();
}

function couper(texte, longueur) {
  return texte.length > longueur ? `${texte.slice(0, longueur - 1)}…` : texte;
}

function memeOrigine(url) {
  try {
    return origine !== null && new URL(url).origin === origine;
  } catch {
    return false;
  }
}

function cheminDe(url) {
  try {
    const analyse = new URL(url);
    return `${analyse.pathname}${analyse.search}`;
  } catch {
    return url;
  }
}

function contientJeton(valeur, jeton) {
  if (jeton.length === 0) {
    return false;
  }

  let position = valeur.indexOf(jeton);
  while (position >= 0) {
    const avant = position > 0 ? valeur[position - 1] : '';
    const apres = valeur[position + jeton.length] || '';
    if (!/[\p{L}\p{N}_]/u.test(avant) && !/[\p{L}\p{N}_.,]/u.test(apres)) {
      return true;
    }

    position = valeur.indexOf(jeton, position + 1);
  }

  return false;
}

function signaler(regle, geste, champ, message) {
  const cle = `saisie|${regle}|${champ ? libelleDe(champ) : '-'}|${nomDeclencheur(geste)}`;
  if (ecarts.length >= MaxEcarts || clesEcarts.has(cle)) {
    return;
  }

  clesEcarts.add(cle);
  ecarts.push(cle);
  emettre(cle, message);
}

function emettre(cle, message) {
  process.stdout.write(`@@usine-signal ${Buffer.from(JSON.stringify({ cle, message: couper(message, LongueurMessage) }), 'utf8').toString('base64')}\n`);
}

function verifierBudget() {
  if (Date.now() - debut >= options.delai) {
    arret = `délai de ${Math.round(options.delai / 1000)} s`;
    throw new Arret();
  }

  if (rejeux >= options.maxRejeux) {
    arret = `${options.maxRejeux} rejeux`;
    throw new Arret();
  }

  if (ecarts.length >= MaxEcarts) {
    arret = `${MaxEcarts} écarts`;
    throw new Arret();
  }
}

function libelleDe(champ) {
  return normaliser(champ.libelle || champ.selecteur);
}

function nomDeclencheur(geste) {
  return normaliser(geste.declencheur.nom || geste.declencheur.id || geste.declencheur.role || '?');
}

function lireSoumissions() {
  return lignesJournal().filter((objet) => objet && objet.type === 'soumission' && typeof objet.url === 'string' && objet.declencheur && typeof objet.declencheur === 'object' && Array.isArray(objet.champs));
}

function lignesJournal() {
  let fichiers = [];
  try {
    fichiers = readdirSync(options.journal).filter((fichier) => fichier.endsWith('.jsonl')).sort();
  } catch {
    return [];
  }

  const lignes = [];
  for (const fichier of fichiers) {
    let contenu = '';
    try {
      contenu = readFileSync(join(options.journal, fichier), 'utf8');
    } catch {
      continue;
    }

    for (const ligne of contenu.split('\n')) {
      if (ligne.trim().length === 0) {
        continue;
      }

      try {
        lignes.push(JSON.parse(ligne));
      } catch {
      }
    }
  }

  return lignes;
}

function typeDe(champ) {
  return String(champ.type || 'text').toLowerCase();
}

function regrouperChamps(champs) {
  const parSelecteur = new Map();
  for (const champ of champs) {
    if (!champ || typeof champ.selecteur !== 'string' || champ.selecteur.length === 0) {
      continue;
    }

    const valeur = { valeur: String(champ.valeur ?? ''), coche: champ.coche === true };
    const existant = parSelecteur.get(champ.selecteur);
    if (!existant) {
      parSelecteur.set(champ.selecteur, { champ: { selecteur: champ.selecteur, libelle: champ.libelle || null, type: typeDe(champ) }, valeur, option: champ.option || null, parDefaut: champ.parDefaut === true, autreFormulaire: champ.autreFormulaire === true });
    } else if (typeDe(champ) === 'radio' && valeur.coche) {
      existant.valeur = valeur;
      existant.parDefaut = champ.parDefaut === true;
    }
  }

  return [...parSelecteur.values()].sort((a, b) => (a.champ.selecteur < b.champ.selecteur ? -1 : a.champ.selecteur > b.champ.selecteur ? 1 : 0));
}

function cleCombinaison(geste, jeu, exclu) {
  return JSON.stringify(geste.champs.map((champ, index) => (index === exclu || (exclu < 0 && TypesTexte.has(champ.type)) ? null : jeu.valeurs[index])));
}

function lireGestes(soumissions) {
  const gestes = new Map();
  for (const soumission of soumissions) {
    if (soumission.champs.some((champ) => champ && typeDe(champ) === 'file')) {
      continue;
    }

    const champs = regrouperChamps(soumission.champs);
    if (champs.length === 0) {
      continue;
    }

    const declencheur = { id: soumission.declencheur.id || null, role: soumission.declencheur.role || null, nom: soumission.declencheur.nom || null };
    const cle = JSON.stringify([soumission.url, declencheur.id, declencheur.role, declencheur.nom, champs.map((c) => c.champ.selecteur)]);
    if (!gestes.has(cle)) {
      if (gestes.size >= MaxGestes) {
        continue;
      }

      gestes.set(cle, { cle, url: soumission.url, declencheur, champs: champs.map((c) => c.champ), jeux: [], vus: new Set(), parCombinaison: new Map(), instable: false });
    }

    const geste = gestes.get(cle);
    const jeu = { valeurs: champs.map((c) => c.valeur), options: champs.map((c) => c.option) };
    const empreinte = JSON.stringify(jeu.valeurs);
    if (geste.vus.has(empreinte) || geste.jeux.length >= MaxJeuxParGeste) {
      continue;
    }

    const combinaison = cleCombinaison(geste, jeu, -1);
    const nombre = geste.parCombinaison.get(combinaison) || 0;
    if (nombre >= MaxJeuxParCombinaison) {
      continue;
    }

    geste.parCombinaison.set(combinaison, nombre + 1);
    geste.vus.add(empreinte);
    geste.jeux.push(jeu);
  }

  return [...gestes.values()];
}

function echapperAttribut(valeur) {
  return String(valeur).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

async function lireTexte(page) {
  try {
    return await page.evaluate(() => (document.body ? String(document.body.innerText || '') : ''));
  } catch {
    return null;
  }
}

async function attendreStable(page, etat, calme) {
  const limite = Date.now() + AttenteMax;
  let texte = await lireTexte(page);
  let depuis = Date.now();
  while (Date.now() < limite) {
    await attendre(25);
    const nouveau = await lireTexte(page);
    if (nouveau === null || nouveau !== texte || etat.enVol > 0) {
      texte = nouveau;
      depuis = Date.now();
    } else if (Date.now() - depuis >= calme) {
      break;
    }
  }

  return texte ?? '';
}

async function appliquer(page, champ, entree) {
  const cible = page.locator(champ.selecteur).first();
  if (champ.type === 'select') {
    await cible.selectOption({ value: entree.valeur }, { timeout: DelaiAction });
  } else if (champ.type === 'checkbox') {
    await cible.setChecked(entree.coche, { timeout: DelaiAction });
  } else if (champ.type === 'radio') {
    if (entree.coche) {
      await page.locator(`${champ.selecteur}[value="${echapperAttribut(entree.valeur)}"]`).first().check({ timeout: DelaiAction });
    }
  } else if (champ.type === 'file') {
    await cible.setInputFiles(entree.valeur.length > 0 ? entree.valeur : [], { timeout: DelaiAction });
  } else {
    await cible.fill(entree.valeur, { timeout: DelaiAction });
  }
}

async function cliquer(page, declencheur, delai = DelaiAction) {
  const candidats = [];
  if (declencheur.id) {
    candidats.push(() => page.locator(`[id="${echapperAttribut(declencheur.id)}"]`));
  }

  if (declencheur.nom) {
    candidats.push(() => page.getByRole(declencheur.role || 'button', { name: declencheur.nom, exact: true }));
    candidats.push(() => page.getByText(declencheur.nom, { exact: true }));
  }

  for (const candidat of candidats) {
    try {
      const cible = candidat().first();
      if (await cible.count() > 0) {
        await cible.click({ timeout: delai });
        return true;
      }
    } catch {
    }
  }

  return false;
}

function lignesDe(texte) {
  return texte.split('\n').map(normaliser).filter((ligne) => ligne.length > 0);
}

function difference(avant, apres) {
  const restantes = new Map();
  for (const ligne of avant) {
    restantes.set(ligne, (restantes.get(ligne) || 0) + 1);
  }

  const ajoutees = [];
  for (const ligne of apres) {
    const nombre = restantes.get(ligne) || 0;
    if (nombre > 0) {
      restantes.set(ligne, nombre - 1);
    } else {
      ajoutees.push(ligne);
    }
  }

  const retirees = [];
  for (const [ligne, nombre] of restantes) {
    for (let i = 0; i < nombre; i += 1) {
      retirees.push(ligne);
    }
  }

  return { ajoutees, retirees };
}

function anomaliesDe(texte, saisies) {
  const trouvees = [];
  for (const [motif, expression] of Motifs) {
    expression.lastIndex = 0;
    let correspondance = expression.exec(texte);
    while (correspondance !== null) {
      const brut = correspondance[0];
      const jeton = (motif === 'zero-negatif' ? brut.replace(/^[\s(:=>«"']/, '') : brut).trim();
      if (!saisies.some((saisie) => contientJeton(saisie, jeton))) {
        const debutLigne = texte.lastIndexOf('\n', correspondance.index - 1) + 1;
        const finLigne = texte.indexOf('\n', correspondance.index + brut.length);
        trouvees.push({ motif, extrait: normaliser(texte.slice(debutLigne, finLigne < 0 ? texte.length : finLigne)).slice(0, 80) });
      }

      if (brut.length === 0) {
        expression.lastIndex += 1;
      }

      correspondance = expression.exec(texte);
    }
  }

  return trouvees;
}

const cache = new Map();

async function rejouer(navigateur, geste, jeu) {
  verifierBudget();
  rejeux += 1;
  const saisies = geste.champs.map((champ, index) => (['select', 'checkbox', 'radio'].includes(champ.type) ? null : jeu.valeurs[index].valeur)).filter((valeur) => valeur !== null);
  const etat = { enVol: 0, statuts: [], dialogues: [], exceptions: [], erreurs5xx: [] };
  let contexte = null;
  try {
    contexte = await navigateur.newContext({ baseURL: options.base });
    const page = await contexte.newPage();
    page.on('request', () => {
      etat.enVol += 1;
    });
    page.on('requestfinished', () => {
      etat.enVol -= 1;
    });
    page.on('requestfailed', () => {
      etat.enVol -= 1;
    });
    page.on('response', (reponse) => {
      try {
        if (!memeOrigine(reponse.url())) {
          return;
        }

        const methode = reponse.request().method();
        if (methode !== 'GET') {
          etat.statuts.push(reponse.status());
        }

        if (reponse.status() >= 500) {
          etat.erreurs5xx.push(`${methode} ${cheminDe(reponse.url())} → ${reponse.status()}`);
        }
      } catch {
      }
    });
    page.on('pageerror', (erreur) => {
      etat.exceptions.push(normaliser(erreur && erreur.message ? erreur.message : String(erreur)).slice(0, 160));
    });
    page.on('dialog', (dialogue) => {
      etat.dialogues.push(`${dialogue.type()}: ${normaliser(dialogue.message())}`);
      dialogue.dismiss().catch(() => {});
    });
    await page.goto(new URL(geste.url, options.base).href, { waitUntil: 'load', timeout: DelaiChargement });
    const reference = await attendreStable(page, etat, CalmeReference);
    for (let index = 0; index < geste.champs.length; index += 1) {
      await appliquer(page, geste.champs[index], jeu.valeurs[index]);
    }

    if (!(await cliquer(page, geste.declencheur))) {
      return null;
    }

    const apres = await attendreStable(page, etat, CalmeApresClic);
    const marques = await page.evaluate(() => ({ xss: window.__usineXss === 1, element: document.querySelector('[data-usine]') !== null })).catch(() => ({ xss: false, element: false }));
    const { ajoutees, retirees } = difference(lignesDe(reference), lignesDe(apres));
    const chemin = cheminDe(page.url());
    return {
      empreinte: JSON.stringify([[...ajoutees].sort(), retirees.sort(), [...etat.statuts].sort((a, b) => a - b), chemin, etat.dialogues]),
      ajoutees,
      anomalies: anomaliesDe(ajoutees.join('\n'), saisies),
      exceptions: etat.exceptions,
      erreurs5xx: etat.erreurs5xx,
      xss: marques.xss,
      element: marques.element,
    };
  } catch (erreur) {
    if (erreur instanceof Arret) {
      throw erreur;
    }

    return null;
  } finally {
    if (contexte !== null) {
      await contexte.close().catch(() => {});
    }
  }
}

function jeuAvec(jeu, index, valeur) {
  const valeurs = jeu.valeurs.map((entree, position) => (position === index ? { valeur, coche: entree.coche } : entree));
  return { valeurs, options: jeu.options };
}

async function rejeuEnCache(navigateur, geste, jeu) {
  const cle = `${geste.cle}|${JSON.stringify(jeu.valeurs)}`;
  if (!cache.has(cle)) {
    cache.set(cle, await rejouer(navigateur, geste, jeu));
  }

  return cache.get(cle);
}

function decrireCombinaison(geste, jeu, exclu) {
  const parties = [];
  geste.champs.forEach((champ, index) => {
    if (index === exclu) {
      return;
    }

    const entree = jeu.valeurs[index];
    if (champ.type === 'select') {
      parties.push(normaliser(jeu.options[index] || entree.valeur));
    } else if (champ.type === 'checkbox' || champ.type === 'radio') {
      parties.push(`${libelleDe(champ)} ${entree.coche ? 'coché' : 'non coché'}`);
    } else {
      parties.push(`${libelleDe(champ)} « ${couper(entree.valeur, 30)} »`);
    }
  });
  return parties.length > 0 ? ` (${parties.join(', ')})` : '';
}

function partieEntiere(valeur) {
  const entier = valeur.trim().split(/[.,]/)[0];
  return ZeroSigne.test(entier) ? '0' : entier;
}

function nouvelles(liste, reference) {
  return liste.filter((element) => !reference.includes(element));
}

function nouvellesAnomalies(resultat, base) {
  const motifsBase = new Set(base.anomalies.map((a) => a.motif));
  return resultat.anomalies.filter((a) => !motifsBase.has(a.motif));
}

function infiniDans(resultat) {
  return resultat.ajoutees.some((ligne) => /Infinity|∞/.test(ligne));
}

function ligneInfinie(resultat) {
  return resultat.ajoutees.find((ligne) => /Infinity|∞/.test(ligne)) || '';
}

function lireNombres(texte) {
  const nombres = [];
  const gabarit = texte.replace(/[-−‑]?\d+(?:[.,]\d+)?/g, (jeton, position) => {
    const precedent = position > 0 ? texte[position - 1] : '';
    if (/^[-−‑]/.test(jeton) && /[\p{L}\p{N}_]/u.test(precedent)) {
      nombres.push(jeton.slice(1));
      return `${jeton[0]}§`;
    }

    nombres.push(jeton);
    return '§';
  });
  return { gabarit, nombres };
}

function versNombre(texte) {
  return Number(String(texte).trim().replace(/[−‑]/g, '-').replace(',', '.'));
}

function decimalesDe(texte) {
  const position = texte.search(/[.,]/);
  return position < 0 ? 0 : texte.length - position - 1;
}

function identifierSortie(observations) {
  const parGabarit = new Map();
  for (const observation of observations) {
    const { gabarit, nombres } = lireNombres(observation.resultat.ajoutees.join('\n'));
    if (!parGabarit.has(gabarit)) {
      parGabarit.set(gabarit, []);
    }

    parGabarit.get(gabarit).push({ saisie: observation.saisie, nombres });
  }

  let meilleur = null;
  for (const [gabarit, entrees] of parGabarit) {
    if (meilleur === null || entrees.length > meilleur.entrees.length) {
      meilleur = { gabarit, entrees };
    }
  }

  if (meilleur === null || meilleur.entrees.length < 2) {
    return null;
  }

  const candidats = [];
  const total = meilleur.entrees[0].nombres.length;
  for (let index = 0; index < total; index += 1) {
    const valeurs = meilleur.entrees.map((entree) => versNombre(entree.nombres[index]));
    const varie = new Set(valeurs).size > 1;
    const copie = meilleur.entrees.every((entree) => versNombre(entree.nombres[index]) === versNombre(entree.saisie));
    if (varie && !copie) {
      candidats.push(index);
    }
  }

  if (candidats.length !== 1) {
    return null;
  }

  const index = candidats[0];
  const decimales = new Set(meilleur.entrees.map((entree) => decimalesDe(entree.nombres[index])));
  return { gabarit: meilleur.gabarit, index, decimales: decimales.size === 1 ? [...decimales][0] : null };
}

function lireSortie(sortie, resultat) {
  if (!resultat) {
    return null;
  }

  const { gabarit, nombres } = lireNombres(resultat.ajoutees.join('\n'));
  return gabarit === sortie.gabarit ? nombres[sortie.index] : null;
}

function classeDe(texte) {
  const valeur = versNombre(texte);
  if (valeur < 0 || Object.is(valeur, -0)) {
    return -1;
  }

  return valeur === 0 ? 0 : 1;
}

function formater(entier, decimales, separateur) {
  const signe = entier < 0n ? '-' : '';
  const absolu = (entier < 0n ? -entier : entier).toString().padStart(decimales + 1, '0');
  return decimales === 0 ? `${signe}${absolu}` : `${signe}${absolu.slice(0, absolu.length - decimales)}${separateur}${absolu.slice(absolu.length - decimales)}`;
}

function pgcd(a, b) {
  let x = a < 0n ? -a : a;
  let y = b < 0n ? -b : b;
  while (y !== 0n) {
    [x, y] = [y, x % y];
  }

  return x;
}

function divisionPlancher(a, b) {
  const quotient = a / b;
  return a % b !== 0n && (a < 0n) !== (b < 0n) ? quotient - 1n : quotient;
}

function divisionPlafond(a, b) {
  return -divisionPlancher(-a, b);
}

function fraction(texte) {
  const correspondance = /^([+-]?)(\d+)(?:[.,](\d+))?$/.exec(String(texte).trim().replace(/[−‑]/g, '-'));
  if (!correspondance) {
    return null;
  }

  const decimales = correspondance[3] || '';
  const absolu = BigInt(correspondance[2] + decimales);
  return { n: correspondance[1] === '-' ? -absolu : absolu, d: 10n ** BigInt(decimales.length) };
}

function enDecimal(numerateur, denominateur) {
  let n = numerateur;
  let d = denominateur;
  const diviseur = pgcd(n, d);
  n /= diviseur;
  d /= diviseur;
  let reste = d;
  while (reste % 2n === 0n) {
    reste /= 2n;
  }

  while (reste % 5n === 0n) {
    reste /= 5n;
  }

  if (reste !== 1n) {
    return null;
  }

  let decimales = 0;
  let puissance = 1n;
  while (puissance % d !== 0n) {
    puissance *= 10n;
    decimales += 1;
  }

  return formater(n * (puissance / d), decimales, '.');
}

function arrondisAdmis(m, d) {
  const plancher = divisionPlancher(m, d);
  const double = 2n * (m - plancher * d);
  if (double < d) {
    return [plancher];
  }

  return double > d ? [plancher + 1n] : [plancher, plancher + 1n];
}

function chercherFormule(observations, zero, grand, decimales) {
  const echelle = 10n ** BigInt(decimales);
  const y0 = zero.y.n * echelle / zero.y.d;
  const yN = grand.y.n * echelle / grand.y.d;
  const n = grand.x.n / grand.x.d;
  const pentes = [];
  for (let q = 1n; q <= BigInt(MaxDenominateur) && pentes.length < MaxPentes; q += 1n) {
    const minimum = divisionPlafond((yN - y0 - 1n) * q, n * echelle);
    const maximum = divisionPlancher((yN - y0 + 1n) * q, n * echelle);
    const candidats = [];
    for (let p = minimum; p <= maximum && candidats.length < MaxPentes; p += 1n) {
      if (pgcd(p, q) === 1n) {
        candidats.push(p);
      }
    }

    candidats.sort((a, b) => ((a < 0n ? -a : a) < (b < 0n ? -b : b) ? -1 : 1));
    for (const p of candidats) {
      if (pentes.length < MaxPentes) {
        pentes.push({ p, q });
      }
    }
  }

  const triees = [...observations].sort((a, b) => {
    const xa = Math.abs(Number(a.x.n) / Number(a.x.d));
    const xb = Math.abs(Number(b.x.n) / Number(b.x.d));
    return xb - xa;
  });
  let verifications = 0;
  for (const { p, q } of pentes) {
    for (let r = 1n; r <= BigInt(MaxDenominateur); r += 1n) {
      const kMin = divisionPlafond((2n * y0 - 1n) * r, 2n * echelle);
      const kMax = divisionPlancher((2n * y0 + 1n) * r, 2n * echelle);
      for (let k = kMin; k <= kMax; k += 1n) {
        if (pgcd(k, r) !== 1n) {
          continue;
        }

        verifications += 1;
        if (verifications > MaxVerifications || (verifications % 10000 === 0 && Date.now() - debut >= options.delai)) {
          return { formule: null, raison: 'recherche de la formule arrêtée à sa limite de calcul' };
        }

        let juste = true;
        for (const observation of triees) {
          const m = (p * observation.x.n * r + k * q * observation.x.d) * echelle;
          const d = q * observation.x.d * r;
          const attendue = observation.y.n * echelle / observation.y.d;
          if (!arrondisAdmis(m, d).includes(attendue)) {
            juste = false;
            break;
          }
        }

        if (juste) {
          return { formule: { p, q, k, r, decimales, echelle } };
        }
      }
    }
  }

  return { formule: null, raison: "aucune formule affine ne redonne exactement toutes les sorties observées (fonction non affine, troncature au lieu d'arrondi…)" };
}

function estMilieu(formule, x) {
  const m = (formule.p * x.n * formule.r + formule.k * formule.q * x.d) * formule.echelle;
  const d = formule.q * x.d * formule.r;
  return 2n * (m - divisionPlancher(m, d) * d) === d;
}

function decrireFormule(formule) {
  const pente = formule.q === 1n ? `${formule.p}` : `${formule.p}/${formule.q}`;
  if (formule.k === 0n) {
    return `${pente}·x`;
  }

  const absolu = formule.k < 0n ? -formule.k : formule.k;
  return `${pente}·x ${formule.k < 0n ? '−' : '+'} ${formule.r === 1n ? `${absolu}` : `${absolu}/${formule.r}`}`;
}

function calculsPlausibles(formule) {
  const p = Number(formule.p);
  const q = Number(formule.q);
  const b = Number(formule.k) / Number(formule.r);
  const calculs = [(x) => x * p / q + b, (x) => x * (p / q) + b];
  const decimal = enDecimal(formule.p, formule.q);
  if (decimal !== null) {
    const a = Number(decimal);
    calculs.push((x) => x * a + b);
  }

  if (formule.p !== 0n) {
    const c = enDecimal(-formule.k * formule.q, formule.r * formule.p);
    const inverse = enDecimal(formule.q, formule.p);
    if (c !== null && inverse !== null) {
      const centre = Number(c);
      const diviseur = Number(inverse);
      calculs.push((x) => (x - centre) * p / q);
      calculs.push((x) => (x - centre) / diviseur);
    }
  }

  return calculs;
}

function arrondisFlottants(valeur, decimales) {
  const puissance = 10 ** decimales;
  return [Number(valeur.toFixed(decimales)), Math.round(valeur * puissance) / puissance];
}

function generateur(graine) {
  let etat = 2166136261;
  for (const caractere of graine) {
    etat = Math.imul(etat ^ caractere.charCodeAt(0), 16777619) >>> 0;
  }

  return () => {
    etat = (etat + 0x6D2B79F5) >>> 0;
    let t = etat;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function choisirMilieux(formule, minimum, maximum, separateur, graine) {
  const calculs = calculsPlausibles(formule);
  const puissance = 10 ** formule.decimales;
  for (const decimalesSaisie of [2, 3]) {
    const echelleSaisie = 10n ** BigInt(decimalesSaisie);
    const debutJ = divisionPlafond(minimum.n * echelleSaisie, minimum.d);
    const finJ = divisionPlancher(maximum.n * echelleSaisie, maximum.d);
    const d = formule.q * echelleSaisie * formule.r;
    const pas = formule.p * formule.r * formule.echelle;
    let m = (formule.p * debutJ * formule.r + formule.k * formule.q * echelleSaisie) * formule.echelle;
    const milieux = [];
    for (let j = debutJ; j <= finJ; j += 1n, m += pas) {
      if (m <= 0n || 2n * (m - divisionPlancher(m, d) * d) !== d) {
        continue;
      }

      const plancher = divisionPlancher(m, d);
      if (plancher % 2n === 0n) {
        continue;
      }

      const saisie = formater(j, decimalesSaisie, separateur);
      const x = Number(formater(j, decimalesSaisie, '.'));
      const attendu = Number(plancher + 1n);
      let fautes = 0;
      for (const calcul of calculs) {
        for (const arrondi of arrondisFlottants(calcul(x), formule.decimales)) {
          if (Math.round(arrondi * puissance) !== attendu) {
            fautes += 1;
          }
        }
      }

      milieux.push({ saisie, x, j, plancher, fautes, exacte: formater((2n * plancher + 1n) * 5n, formule.decimales + 1, ',') });
    }

    if (milieux.length === 0) {
      continue;
    }

    milieux.sort((a, b) => b.fautes - a.fautes || Math.abs(a.x) - Math.abs(b.x) || a.x - b.x);
    const retenus = milieux.slice(0, MilieuxClasses);
    const restants = milieux.slice(MilieuxClasses);
    const hasard = generateur(graine);
    for (let i = 0; i < MilieuxAuHasard && restants.length > 0; i += 1) {
      retenus.push(restants.splice(Math.floor(hasard() * restants.length), 1)[0]);
    }

    return { milieux: retenus, calculs };
  }

  return { milieux: [], calculs };
}

function texteFlottant(calculs, x, affiche, decimales) {
  const puissance = 10 ** decimales;
  for (const calcul of calculs) {
    const valeur = calcul(x);
    if (arrondisFlottants(valeur, decimales).some((arrondi) => Math.round(arrondi * puissance) === Math.round(versNombre(affiche) * puissance))) {
      return String(valeur);
    }
  }

  return null;
}

async function explorerGeste(navigateur, geste) {
  const valides = [];
  for (const jeu of geste.jeux) {
    const premier = await rejeuEnCache(navigateur, geste, jeu);
    const second = await rejouer(navigateur, geste, jeu);
    if (premier === null || second === null) {
      continue;
    }

    if (premier.empreinte !== second.empreinte) {
      geste.instable = true;
    }

    valides.push(jeu);
  }

  geste.jeux = valides;
  if (valides.length === 0) {
    return;
  }

  gestesRejoues += 1;
  if (geste.instable) {
    gestesInstables += 1;
  }

  geste.champsNumeriques = [];
  const affichages = [];
  const erreurs = [];
  for (let index = 0; index < geste.champs.length; index += 1) {
    const champ = geste.champs[index];
    if (!TypesTexte.has(champ.type)) {
      continue;
    }

    const bases = valides.filter((jeu) => Numerique.test(jeu.valeurs[index].valeur));
    if (bases.length > 0) {
      geste.champsNumeriques.push(await explorerChampNumerique(navigateur, geste, index, bases, affichages, erreurs));
    } else if (!geste.instable) {
      await explorerChampTexte(navigateur, geste, index, valides[0]);
    }
  }

  const sortiesTrouvees = new Set(geste.champsNumeriques.filter((c) => c.combinaisons.some((combinaison) => combinaison.sortie !== null)).map((c) => c.index));
  const parChamp = new Map();
  for (const affichage of affichages) {
    if (affichage.anomalie.motif === 'zero-negatif' && sortiesTrouvees.has(affichage.index)) {
      continue;
    }

    if (!parChamp.has(affichage.index)) {
      parChamp.set(affichage.index, []);
    }

    parChamp.get(affichage.index).push(affichage);
  }

  for (const [index, liste] of parChamp) {
    const champ = geste.champs[index];
    const premier = liste[0];
    const autres = [...new Set(liste.slice(1).map((a) => `« ${couper(a.saisie, 20)} » : « ${a.anomalie.extrait} »`))].slice(0, 3);
    signaler('S4', geste, champ, `Affichage anormal provoqué par une saisie que les tests n'essaient pas : avec « ${libelleDe(champ)} » à « ${couper(premier.saisie, 30)} »${premier.combinaison} puis « ${nomDeclencheur(geste)} », la page affiche « ${premier.anomalie.extrait} » (${MotifsEnClair[premier.anomalie.motif] || premier.anomalie.motif}).${autres.length > 0 ? ` Autres : ${autres.join(' ; ')}.` : ''} Une saisie inattendue doit donner un message clair, jamais cet affichage.`);
  }

  const erreursParChamp = new Map();
  for (const erreur of erreurs) {
    if (!erreursParChamp.has(erreur.index)) {
      erreursParChamp.set(erreur.index, erreur);
    }
  }

  for (const [index, erreur] of erreursParChamp) {
    const champ = geste.champs[index];
    signaler('S5', geste, champ, `Erreur provoquée par une saisie que les tests n'essaient pas : avec « ${libelleDe(champ)} » à « ${couper(erreur.saisie, 30)} »${erreur.combinaison} puis « ${nomDeclencheur(geste)} », ${erreur.description}. Une saisie inattendue doit être refusée avec un message clair, sans erreur.`);
  }
}

function noterErreurs(resultat, base, index, saisie, combinaison, erreurs) {
  for (const exception of nouvelles(resultat.exceptions, base.exceptions)) {
    erreurs.push({ index, saisie, combinaison, description: `la page lève une exception JavaScript non interceptée : « ${couper(exception, 120)} »` });
  }

  for (const reponse of nouvelles(resultat.erreurs5xx, base.erreurs5xx)) {
    erreurs.push({ index, saisie, combinaison, description: `l'application renvoie une erreur serveur : « ${reponse} »` });
  }
}

async function explorerChampNumerique(navigateur, geste, index, bases, affichages, erreurs) {
  const champ = geste.champs[index];
  const lectures = [];
  const virgules = [];
  const infinis = [];
  const combinaisons = new Map();
  const leveeInfini = (() => {
    try {
      return /infini/i.test(readFileSync(options.contrat, 'utf8'));
    } catch {
      return false;
    }
  })();
  for (const jeu of bases.filter((j) => !ZeroSigne.test(j.valeurs[index].valeur))) {
    const cle = cleCombinaison(geste, jeu, index);
    if (!combinaisons.has(cle)) {
      combinaisons.set(cle, { jeu, description: decrireCombinaison(geste, jeu, index), saisies: new Set(), valeursBases: [], sortie: null });
    }

    const combinaison = combinaisons.get(cle);
    if (combinaison.valeursBases.length >= MaxJeuxParCombinaison) {
      continue;
    }

    const c = jeu.valeurs[index].valeur;
    combinaison.valeursBases.push(c);
    const entier = partieEntiere(c);
    const base = await rejeuEnCache(navigateur, geste, jeu);
    if (base === null) {
      continue;
    }

    const variantes = ['abc', `${c}abc`, `${c} 34`, '0x10', '0', `${entier},5`, `${entier}.5`, entier, '1e400', '-1e400', '', '   '];
    const resultats = new Map();
    for (const variante of variantes) {
      const resultat = await rejeuEnCache(navigateur, geste, jeuAvec(jeu, index, variante));
      resultats.set(variante, resultat);
      if (resultat === null) {
        continue;
      }

      for (const anomalie of nouvellesAnomalies(resultat, base)) {
        affichages.push({ index, saisie: variante, combinaison: combinaison.description, anomalie });
      }

      noterErreurs(resultat, base, index, variante, combinaison.description, erreurs);
    }

    for (const saisie of [c, '0', entier, `${entier}.5`]) {
      combinaison.saisies.add(saisie);
    }

    if (geste.instable) {
      continue;
    }

    const empreinte = (saisie) => (saisie === c ? base.empreinte : resultats.get(saisie)?.empreinte ?? null);
    const invalide = empreinte('abc');
    for (const [variante, modele] of [[`${c}abc`, c], [`${c} 34`, c], ['0x10', '0']]) {
      const ev = empreinte(variante);
      const ep = empreinte(modele);
      if (ev !== null && ep !== null && invalide !== null && ev === ep && ep !== invalide) {
        lectures.push(`« ${variante} » s'affiche comme « ${modele} »`);
      }
    }

    const virgule = empreinte(`${entier},5`);
    const point = empreinte(`${entier}.5`);
    const entiere = empreinte(entier);
    if (virgule !== null && point !== null && entiere !== null && virgule === entiere && point !== entiere) {
      virgules.push(`« ${entier},5 » s'affiche comme « ${entier} » alors que « ${entier}.5 » donne un autre résultat`);
    }

    if (!leveeInfini && !infiniDans(base)) {
      for (const variante of ['1e400', '-1e400']) {
        const resultat = resultats.get(variante);
        if (resultat && infiniDans(resultat)) {
          infinis.push(`« ${variante} » fait afficher « ${couper(ligneInfinie(resultat), 60)} »`);
        }
      }
    }
  }

  const libelle = libelleDe(champ);
  const bouton = nomDeclencheur(geste);
  if (lectures.length > 0) {
    signaler('S1', geste, champ, ajusterListe(`Lecture partielle de la saisie « ${libelle} » (puis « ${bouton} ») : `, [...new Set(lectures)], ", alors que « abc » donne un autre affichage. Une saisie qui n'est pas entièrement un nombre doit être refusée comme « abc », pas lue en partie."));
  }

  if (virgules.length > 0) {
    signaler('S2', geste, champ, ajusterListe(`Virgule décimale tronquée dans « ${libelle} » (puis « ${bouton} ») : `, [...new Set(virgules)], '. Accepte la virgule comme séparateur décimal ou refuse la saisie avec un message, mais ne la tronque jamais.'));
  }

  if (infinis.length > 0) {
    signaler('S3', geste, champ, ajusterListe(`Valeur non finie affichée avec « ${libelle} » (puis « ${bouton} ») : `, [...new Set(infinis)], '. Un nombre hors limites doit être refusé avec un message, pas affiché comme infini.'));
  }

  const resultatChamp = { index, combinaisons: [] };
  for (const combinaison of combinaisons.values()) {
    const observations = [];
    for (const saisie of combinaison.saisies) {
      const resultat = cache.get(`${geste.cle}|${JSON.stringify(jeuAvec(combinaison.jeu, index, saisie).valeurs)}`);
      if (resultat) {
        observations.push({ saisie, resultat });
      }
    }

    combinaison.sortie = geste.instable ? null : identifierSortie(observations);
    resultatChamp.combinaisons.push(combinaison);
  }

  return resultatChamp;
}

async function explorerChampTexte(navigateur, geste, index, jeu) {
  const champ = geste.champs[index];
  suffixes += 1;
  const suffixe = `uzq${suffixes}w${Date.now().toString(36)}`;
  const saisie = `${Temoin} ${suffixe}`;
  const base = await rejeuEnCache(navigateur, geste, jeu);
  const resultat = await rejeuEnCache(navigateur, geste, jeuAvec(jeu, index, saisie));
  if (base === null || resultat === null) {
    return;
  }

  const raisons = [];
  if (resultat.element && !base.element) {
    raisons.push('la page a interprété la saisie comme du HTML (un élément <b data-usine> a été créé)');
  }

  if (resultat.xss && !base.xss) {
    raisons.push('un script contenu dans la saisie s\'est exécuté (attribut onerror)');
  }

  const fautes = [];
  noterErreurs(resultat, base, index, saisie, '', fautes);
  for (const faute of fautes) {
    raisons.push(faute.description);
  }

  const ligne = resultat.ajoutees.find((l) => l.includes(suffixe));
  if (ligne !== undefined && MorceauxTemoin.some((morceau) => !ligne.includes(morceau))) {
    raisons.push(`la ligne réaffichée « ${couper(ligne.replace(suffixe, '…'), 90)} » a perdu ou transformé une partie de la saisie (${MorceauxTemoin.filter((morceau) => !ligne.includes(morceau)).join(', ')})`);
  }

  if (raisons.length > 0) {
    signaler('S7', geste, champ, `Texte saisi mal réaffiché : avec « ${libelleDe(champ)} » à « <b data-usine="1">x</b><img src=x onerror=…> $& {{a}} 100% %41 é€ » puis « ${nomDeclencheur(geste)} », ${raisons.join(' ; ')}. Réaffiche toute saisie comme du texte brut (textContent, échappement HTML), sans l'interpréter.`);
  }
}

function ajusterListe(avant, elements, apres) {
  const retenus = elements.slice(0, 1);
  for (const element of elements.slice(1)) {
    if (`${avant}${[...retenus, element].join(' ; ')}${apres}`.length > LongueurMessage) {
      break;
    }

    retenus.push(element);
  }

  return `${avant}${retenus.join(' ; ')}${apres}`;
}

function avecAutres(message, autres) {
  const retenus = [];
  for (const autre of autres) {
    if (`${message} Autres saisies fautives : ${[...retenus, autre].join(' ; ')}.`.length > LongueurMessage) {
      break;
    }

    retenus.push(autre);
  }

  return retenus.length === 0 ? message : `${message} Autres saisies fautives : ${retenus.join(' ; ')}.`;
}

function enAlternant(fautes) {
  const premieres = [];
  const suivantes = [];
  const vues = new Set();
  for (const faute of fautes) {
    if (vues.has(faute.description)) {
      suivantes.push(faute);
    } else {
      vues.add(faute.description);
      premieres.push(faute);
    }
  }

  return [...premieres, ...suivantes];
}

function separateurDe(combinaison) {
  const saisies = combinaison.valeursBases;
  return saisies.some((saisie) => saisie.includes(',')) && !saisies.some((saisie) => saisie.includes('.')) ? ',' : '.';
}

async function essayer(navigateur, geste, index, combinaison, saisie) {
  const resultat = await rejeuEnCache(navigateur, geste, jeuAvec(combinaison.jeu, index, saisie));
  const affiche = lireSortie(combinaison.sortie, resultat);
  return affiche === null ? null : { saisie, affiche, resultat };
}

async function chercherZeroNegatif(navigateur, geste, champNumerique) {
  const champ = geste.champs[champNumerique.index];
  const fautes = [];
  for (const combinaison of champNumerique.combinaisons) {
    if (combinaison.sortie === null) {
      continue;
    }

    const separateur = separateurDe(combinaison);
    const vus = new Set();
    const essai = async (entier, decimales) => {
      const saisie = formater(entier, decimales, separateur);
      const essaiFait = await essayer(navigateur, geste, champNumerique.index, combinaison, saisie);
      if (essaiFait && !vus.has(saisie)) {
        vus.add(saisie);
        const anomalie = essaiFait.resultat.anomalies.find((a) => a.motif === 'zero-negatif');
        if (anomalie) {
          fautes.push({ saisie, affiche: anomalie.extrait, description: combinaison.description });
        }
      }

      return essaiFait;
    };
    const grille = [];
    for (const valeur of Grille) {
      const essaiFait = await essai(BigInt(valeur * 100), 2);
      if (essaiFait) {
        grille.push({ centiemes: BigInt(valeur * 100), classe: classeDe(essaiFait.affiche) });
      }
    }

    let bas = null;
    let haut = null;
    for (let i = 1; i < grille.length; i += 1) {
      if (grille[i].classe !== grille[i - 1].classe) {
        bas = grille[i - 1];
        haut = grille[i];
        break;
      }
    }

    if (bas === null) {
      continue;
    }

    let borneBasse = bas.centiemes;
    let borneHaute = haut.centiemes;
    const classeBasse = bas.classe;
    while (borneHaute - borneBasse > 1n) {
      const milieu = divisionPlancher(borneBasse + borneHaute, 2n);
      const essaiFait = await essai(milieu, 2);
      if (essaiFait === null) {
        break;
      }

      if (classeDe(essaiFait.affiche) === classeBasse) {
        borneBasse = milieu;
      } else {
        borneHaute = milieu;
      }
    }

    for (let pas = 1n; pas <= 8n; pas += 1n) {
      await essai(borneHaute - pas, 2);
      await essai(borneHaute + pas, 2);
    }

    for (let pas = 1n; pas <= 8n; pas += 1n) {
      await essai(borneHaute * 10n - pas, 3);
      await essai(borneHaute * 10n + pas, 3);
    }
  }

  if (fautes.length > 0) {
    const premiere = fautes[0];
    const autres = [...new Set(enAlternant(fautes).slice(1).map((f) => `« ${f.saisie} »${f.description}`))];
    signaler('S8', geste, champ, avecAutres(`Zéro négatif : avec « ${libelleDe(champ)} » à « ${premiere.saisie} »${premiere.description} puis « ${nomDeclencheur(geste)} », la page affiche « ${premiere.affiche} » ; un résultat qui s'arrondit à zéro s'affiche sans signe moins.`, autres));
  }
}

async function verifierArrondi(navigateur, geste, champNumerique) {
  const champ = geste.champs[champNumerique.index];
  const fautes = [];
  for (const combinaison of champNumerique.combinaisons) {
    const nom = `« ${libelleDe(champ)} »${combinaison.description}`;
    if (combinaison.sortie === null) {
      formules.push(`${nom} : pas de sortie numérique reconnue`);
      continue;
    }

    if (combinaison.sortie.decimales === null) {
      formules.push(`${nom} : nombre de décimales variable, arrondi non vérifié`);
      continue;
    }

    const separateur = separateurDe(combinaison);
    const zero = await essayer(navigateur, geste, champNumerique.index, combinaison, '0');
    let grand = null;
    for (const valeur of ['10000', '1000', '100']) {
      const essaiFait = await essayer(navigateur, geste, champNumerique.index, combinaison, valeur);
      if (essaiFait !== null && decimalesDe(essaiFait.affiche) === combinaison.sortie.decimales) {
        grand = essaiFait;
        break;
      }
    }

    if (zero === null || grand === null) {
      formules.push(`${nom} : formule non confirmée (0 ou 100 refusé)`);
      continue;
    }

    const essais = [];
    for (let k = -20; k <= 20; k += 1) {
      essais.push(await essayer(navigateur, geste, champNumerique.index, combinaison, formater(BigInt(k * PasGrilleFormule), 2, separateur)));
    }

    const negatifs = essais.slice(0, 20).some((e) => e !== null);
    const positifs = essais.slice(21).some((e) => e !== null);
    if (negatifs !== positifs) {
      for (let k = 21; k <= 40; k += 1) {
        await essayer(navigateur, geste, champNumerique.index, combinaison, formater(BigInt((positifs ? k : -k) * PasGrilleFormule), 2, separateur));
      }
    }

    const motif = separateur === ',' ? /^[+-]?\d+(,\d+)?$/ : /^[+-]?\d+(\.\d+)?$/;
    const observations = [];
    for (const [cle, resultat] of cache) {
      const prefixe = `${geste.cle}|`;
      if (!cle.startsWith(prefixe) || resultat === null) {
        continue;
      }

      const valeurs = JSON.parse(cle.slice(prefixe.length));
      const saisie = valeurs[champNumerique.index].valeur;
      if (!motif.test(saisie) || JSON.stringify(jeuAvec(combinaison.jeu, champNumerique.index, saisie).valeurs) !== JSON.stringify(valeurs)) {
        continue;
      }

      const affiche = lireSortie(combinaison.sortie, resultat);
      if (affiche === null || decimalesDe(affiche) !== combinaison.sortie.decimales) {
        continue;
      }

      observations.push({ saisie, x: fraction(saisie), y: fraction(affiche) });
    }

    const { formule, raison } = chercherFormule(observations, { x: fraction(zero.saisie), y: fraction(zero.affiche) }, { x: fraction(grand.saisie), y: fraction(grand.affiche) }, combinaison.sortie.decimales);
    if (formule === null) {
      formules.push(`${nom} : formule non confirmée, ${raison}`);
      continue;
    }

    const horsMilieu = observations.filter((o) => !estMilieu(formule, o.x)).length;
    if (horsMilieu < MinObservations) {
      formules.push(`${nom} : formule ${decrireFormule(formule)} non confirmée (${horsMilieu} saisies observées sur ${MinObservations} exigées)`);
      continue;
    }

    const minimum = observations.reduce((a, o) => (o.x.n * a.d < a.n * o.x.d ? o.x : a), observations[0].x);
    const maximum = observations.reduce((a, o) => (o.x.n * a.d > a.n * o.x.d ? o.x : a), observations[0].x);
    const { milieux, calculs } = choisirMilieux(formule, minimum, maximum, separateur, `${geste.cle}|${champNumerique.index}|${combinaison.description}`);
    formules.push(`${nom} : formule ${decrireFormule(formule)} confirmée sur ${horsMilieu} saisies${milieux.length === 0 ? ', aucun milieu à vérifier' : `, ${milieux.length} milieux essayés`}`);
    for (const milieu of milieux) {
      const essaiFait = await essayer(navigateur, geste, champNumerique.index, combinaison, milieu.saisie);
      if (essaiFait === null) {
        continue;
      }

      const lu = fraction(essaiFait.affiche);
      if (lu === null || lu.n * formule.echelle / lu.d !== milieu.plancher + 1n || (lu.n * formule.echelle) % lu.d !== 0n) {
        const separateurSortie = essaiFait.affiche.includes(',') ? ',' : '.';
        fautes.push({
          saisie: milieu.saisie,
          description: combinaison.description,
          exacte: milieu.exacte,
          attendu: formater(milieu.plancher + 1n, formule.decimales, separateurSortie),
          affiche: essaiFait.affiche,
          flottant: texteFlottant(calculs, milieu.x, essaiFait.affiche, formule.decimales),
        });
      }
    }
  }

  if (fautes.length > 0) {
    const premiere = fautes[0];
    const autres = enAlternant(fautes).slice(1).map((f) => `« ${f.saisie} »${f.description} affiche ${f.affiche}`);
    signaler('S9', geste, champ, avecAutres(`Arrondi faux au milieu : la saisie « ${premiere.saisie} »${premiere.description} donne exactement ${premiere.exacte} ; toute règle d'arrondi usuelle donne ${premiere.attendu}, la page affiche ${premiere.affiche}${premiere.flottant === null ? '' : ` (le calcul flottant donne ${premiere.flottant})`} : arrondis la valeur exacte, par exemple ramenée d'abord à 12 chiffres significatifs, pas son approximation.`, autres));
  }
}

function bilan(gestes, erreurInterne) {
  const duree = ((Date.now() - debut) / 1000).toFixed(1);
  const parties = [`Sonde de saisie : ${gestes} geste(s) relevé(s) dans le journal, ${gestesRejoues} rejoué(s)${gestesInstables > 0 ? ` dont ${gestesInstables} instable(s) (S4 et S5 seulement)` : ''}, ${rejeux} rejeu(x) en ${duree} s`];
  if (formules.length > 0) {
    parties.push(formules.join(' ; '));
  }

  parties.push(arret === null ? 'terminée' : `arrêtée au budget (${arret}), écarts déjà trouvés gardés`);
  if (erreurInterne !== null) {
    parties.push(`erreur interne : ${couper(erreurInterne, 200)}`);
  }

  parties.push(`${ecarts.length} écart(s)`);
  return `${parties.join(' ; ')}.`;
}

const ecartsConcurrence = [];
const notesConcurrence = [];
let debutConcurrence = 0;
let arretConcurrence = null;
let soumissionsConcurrence = 0;

function verifierDelaiConcurrence() {
  if (Date.now() - debutConcurrence >= DelaiConcurrence) {
    arretConcurrence = `délai de ${Math.round(DelaiConcurrence / 1000)} s`;
    throw new Arret();
  }
}

function nomFichier(valeur) {
  return String(valeur).split(/[\\/]/).pop();
}

function extensionDe(nom) {
  const position = nom.lastIndexOf('.');
  return position > 0 ? nom.slice(position).toLowerCase() : '';
}

function lireEchantillons() {
  try {
    return readdirSync(Echantillons).sort();
  } catch {
    return [];
  }
}

function ajouterEntree(geste, valeurs) {
  const cle = JSON.stringify(valeurs);
  if (!geste.vues.has(cle) && geste.entrees.length < MaxEntreesConcurrence) {
    geste.vues.add(cle);
    geste.entrees.push(valeurs);
  }
}

function rempli(champ) {
  return !champ.parDefaut && (champ.champ.type === 'checkbox' || champ.champ.type === 'radio' ? champ.valeur.coche : champ.valeur.valeur.length > 0);
}

function lireGestesConcurrence(soumissions, echantillons) {
  const gestes = new Map();
  const derniersParTest = new Map();
  for (const soumission of soumissions) {
    const champs = regrouperChamps(soumission.champs);
    if (!derniersParTest.has(soumission.test)) {
      derniersParTest.set(soumission.test, new Map());
    }

    const derniers = derniersParTest.get(soumission.test);
    const propres = champs.filter((c) => !c.autreFormulaire);
    const saisis = propres.filter((c) => rempli(c) && derniers.get(c.champ.selecteur) !== JSON.stringify(c.valeur));
    for (const c of propres) {
      derniers.set(c.champ.selecteur, JSON.stringify(c.valeur));
    }

    if (saisis.length === 0) {
      continue;
    }

    const declencheur = { id: soumission.declencheur.id || null, role: soumission.declencheur.role || null, nom: soumission.declencheur.nom || null };
    const cle = JSON.stringify([soumission.url, declencheur.id, declencheur.role, declencheur.nom, champs.map((c) => c.champ.selecteur)]);
    if (!gestes.has(cle)) {
      gestes.set(cle, { url: soumission.url, declencheur, champs: champs.map((c) => c.champ), entrees: [], vues: new Set() });
    }

    const valeurs = champs.map((c) => {
      if (c.champ.type !== 'file' || c.valeur.valeur.length === 0) {
        return c.valeur;
      }

      const nom = nomFichier(c.valeur.valeur);
      return echantillons.includes(nom) ? { valeur: join(Echantillons, nom), coche: false } : null;
    });
    if (valeurs.every((valeur) => valeur !== null)) {
      ajouterEntree(gestes.get(cle), valeurs);
    }
  }

  const retenus = [...gestes.values()].sort((a, b) => Number(b.entrees.length > 1) - Number(a.entrees.length > 1)).slice(0, MaxGestesConcurrence);
  for (const geste of retenus) {
    const fournies = [...geste.entrees];
    for (const entree of fournies) {
      geste.champs.forEach((champ, index) => {
        if (champ.type !== 'file' || entree[index].valeur.length === 0) {
          return;
        }

        const nom = nomFichier(entree[index].valeur);
        for (const autre of echantillons.filter((fichier) => fichier !== nom && extensionDe(fichier) === extensionDe(nom))) {
          ajouterEntree(geste, entree.map((valeur, position) => (position === index ? { valeur: join(Echantillons, autre), coche: false } : valeur)));
        }
      });
    }

    if (fournies.length === 1) {
      ajouterEntree(geste, fournies[0].map((valeur, index) => (TypesTexteLibre.has(geste.champs[index].type) && valeur.valeur.length > 0 ? { valeur: `${valeur.valeur} ${index + 2}`, coche: valeur.coche } : valeur)));
    }
  }

  return retenus;
}

function nouvelEtat() {
  return {
    lancees: 0,
    retenues: [],
    repondues: 0,
    liberees: false,
    phase: 'avant',
    envois: { avant: 0, premiere: 0, seconde: 0 },
    refus: 0,
    get enVol() {
      return this.lancees - (this.liberees ? 0 : this.retenues.length);
    },
  };
}

function suivreRequetes(page, etat) {
  page.on('request', (requete) => {
    etat.lancees += 1;
    if (memeOrigine(requete.url()) && TypesRetardes.has(requete.resourceType())) {
      etat.envois[etat.phase] += 1;
    }
  });
  page.on('requestfinished', () => {
    etat.lancees -= 1;
  });
  page.on('requestfailed', () => {
    etat.lancees -= 1;
  });
  page.on('response', (reponse) => {
    try {
      if (memeOrigine(reponse.url()) && reponse.status() >= 400 && [...TypesRetardes, 'document'].includes(reponse.request().resourceType())) {
        etat.refus += 1;
      }
    } catch {
    }
  });
  page.on('dialog', (dialogue) => {
    dialogue.dismiss().catch(() => {});
  });
}

async function remplir(page, geste, entree) {
  for (let index = 0; index < geste.champs.length; index += 1) {
    await appliquer(page, geste.champs[index], entree[index]);
  }
}

async function lireAlertes(page) {
  try {
    return await page.evaluate(() => [...document.querySelectorAll('[role="alert"]')].filter((element) => element.checkVisibility()).map((element) => String(element.innerText || '').trim()).filter((texte) => texte.length > 0).join('\n'));
  } catch {
    return '';
  }
}

async function soumettreSeule(navigateur, geste, entree) {
  verifierDelaiConcurrence();
  soumissionsConcurrence += 1;
  const etat = nouvelEtat();
  let contexte = null;
  try {
    contexte = await navigateur.newContext({ baseURL: options.base });
    const page = await contexte.newPage();
    suivreRequetes(page, etat);
    await page.goto(new URL(geste.url, options.base).href, { waitUntil: 'load', timeout: DelaiChargement });
    const avant = await attendreStable(page, etat, CalmeReference);
    const alertesAvant = await lireAlertes(page);
    await remplir(page, geste, entree);
    etat.phase = 'premiere';
    if (!(await cliquer(page, geste.declencheur, DelaiClicConcurrence))) {
      return null;
    }

    const apres = await attendreStable(page, etat, CalmeApresClic);
    const alertes = await lireAlertes(page);
    return { valide: etat.refus === 0, envois: etat.envois.premiere, alerte: alertes.length > 0 && alertes !== alertesAvant, ajoutees: difference(lignesDe(avant), lignesDe(apres)).ajoutees };
  } catch (erreur) {
    if (erreur instanceof Arret) {
      throw erreur;
    }

    return null;
  } finally {
    if (contexte !== null) {
      await contexte.close().catch(() => {});
    }
  }
}

async function soumettreEnConcurrence(navigateur, geste, premiere, seconde) {
  verifierDelaiConcurrence();
  soumissionsConcurrence += 2;
  const etat = nouvelEtat();
  const liberer = () => {
    etat.liberees = true;
    for (const resoudre of etat.retenues) {
      resoudre();
    }
  };
  let contexte = null;
  try {
    contexte = await navigateur.newContext({ baseURL: options.base });
    const page = await contexte.newPage();
    suivreRequetes(page, etat);
    await page.route('**/*', async (route) => {
      const requete = route.request();
      if (etat.phase !== 'premiere' || !memeOrigine(requete.url()) || !TypesRetardes.has(requete.resourceType())) {
        await route.continue().catch(() => {});
        return;
      }

      const liberee = new Promise((resoudre) => {
        etat.retenues.push(resoudre);
        if (etat.liberees) {
          resoudre();
        }
      });
      let reponse = null;
      try {
        reponse = await route.fetch({ timeout: DelaiChargement });
      } catch {
      }

      etat.repondues += 1;
      await liberee;
      await (reponse === null ? route.abort() : route.fulfill({ response: reponse })).catch(() => {});
    });
    await page.goto(new URL(geste.url, options.base).href, { waitUntil: 'load', timeout: DelaiChargement });
    await attendreStable(page, etat, CalmeReference);
    await remplir(page, geste, premiere);
    etat.phase = 'premiere';
    if (!(await cliquer(page, geste.declencheur, DelaiClicConcurrence))) {
      return null;
    }

    const limiteRequete = Date.now() + AttenteRequete;
    while (etat.retenues.length === 0 && Date.now() < limiteRequete) {
      await attendre(25);
    }

    const limiteReponse = Date.now() + DelaiChargement;
    while (etat.repondues < etat.retenues.length && Date.now() < limiteReponse) {
      await attendre(25);
    }

    etat.phase = 'seconde';
    if (etat.retenues.length === 0) {
      return { retenues: 0 };
    }

    try {
      await remplir(page, geste, seconde);
      if (!(await cliquer(page, geste.declencheur, DelaiClicConcurrence))) {
        return { retenues: etat.retenues.length, secondeImpossible: true };
      }
    } catch (erreur) {
      if (erreur instanceof Arret) {
        throw erreur;
      }

      return { retenues: etat.retenues.length, secondeImpossible: true };
    }

    await attendreStable(page, etat, CalmeApresClic);
    const secondes = etat.envois.seconde;
    liberer();
    const finale = await attendreStable(page, etat, CalmeApresClic);
    return { retenues: etat.retenues.length, secondes, lignes: lignesDe(finale) };
  } catch (erreur) {
    if (erreur instanceof Arret) {
      throw erreur;
    }

    return null;
  } finally {
    liberer();
    if (contexte !== null) {
      await contexte.close().catch(() => {});
    }
  }
}

function communes(lignes, autres) {
  const restantes = [...autres];
  return lignes.filter((ligne) => {
    const position = restantes.indexOf(ligne);
    if (position < 0) {
      return false;
    }

    restantes.splice(position, 1);
    return true;
  });
}

function propres(lignes, autres) {
  return difference(autres, lignes).ajoutees;
}

function decrireEntree(geste, entree, autre) {
  const parties = [];
  geste.champs.forEach((champ, index) => {
    if (JSON.stringify(entree[index]) === JSON.stringify(autre[index])) {
      return;
    }

    if (champ.type === 'checkbox' || champ.type === 'radio') {
      parties.push(`« ${libelleDe(champ)} » ${entree[index].coche ? 'coché' : 'non coché'}`);
    } else {
      parties.push(`« ${libelleDe(champ)} » à « ${couper(champ.type === 'file' ? nomFichier(entree[index].valeur) : entree[index].valeur, 30)} »`);
    }
  });
  return parties.join(', ');
}

function messageConcurrence(geste, premiere, seconde, manquante, restee) {
  const fin = `. ${ConsigneConcurrence}`;
  const affichage = restee === undefined ? `n'affiche pas « ${couper(manquante, 40)} »` : `affiche « ${couper(restee, 40)} » au lieu de « ${couper(manquante, 40)} »`;
  return `${couper(`Envois concurrents désordonnés : « ${nomDeclencheur(geste)} » avec ${decrireEntree(geste, premiere, seconde)}, puis avec ${decrireEntree(geste, seconde, premiere)} avant la réponse (retardée) du premier : la page ${affichage}, ce que donne le second envoi seul`, LongueurMessage - fin.length)}${fin}`;
}

function empreinteAffichage(resultat) {
  return JSON.stringify([...resultat.ajoutees].sort());
}

function raisonDEcarter(resultat, affichagesRefuses) {
  if (!resultat.valide) {
    return EcarteeRefuseeParLeServeur;
  }

  if (resultat.envois === 0) {
    return EcarteeSansRequete;
  }

  if (resultat.alerte) {
    return EcarteeAlerte;
  }

  if (resultat.ajoutees.length === 0) {
    return EcarteeInchangee;
  }

  return affichagesRefuses.has(empreinteAffichage(resultat)) ? EcarteeCommeRefusee : null;
}

function decrireEcartees(ecartees) {
  const nombres = new Map();
  for (const raison of ecartees) {
    nombres.set(raison, (nombres.get(raison) || 0) + 1);
  }

  return [...nombres].map(([raison, nombre]) => `${nombre} ${raison}`).join(', ');
}

async function eprouverConcurrence(navigateur, geste) {
  const nom = `« ${nomDeclencheur(geste)} »`;
  if (geste.entrees.length === 0) {
    notesConcurrence.push(`${nom} : aucune entrée rejouable (fichier des tests absent de ${Echantillons})`);
    return;
  }

  let premiere = null;
  let seconde = null;
  const ecartees = [];
  const affichagesRefuses = new Set();
  for (const entree of geste.entrees) {
    const resultat = await soumettreSeule(navigateur, geste, entree);
    if (resultat === null && premiere === null) {
      notesConcurrence.push(`${nom} : rejeu impossible`);
      return;
    }

    if (resultat === null) {
      ecartees.push(EcarteeNonRejouable);
      continue;
    }

    const raison = raisonDEcarter(resultat, affichagesRefuses);
    if (raison === EcarteeSansRequete && premiere === null && !resultat.alerte && resultat.ajoutees.length > 0 && ecartees.every((autre) => autre === EcarteeSansRequete)) {
      notesConcurrence.push(`${nom} : la soumission n'envoie aucune requête, rien à retarder`);
      return;
    }

    if (raison !== null) {
      if (raison !== EcarteeInchangee && raison !== EcarteeCommeRefusee) {
        affichagesRefuses.add(empreinteAffichage(resultat));
      }

      ecartees.push(raison);
      continue;
    }

    if (premiere === null) {
      premiere = { entree, resultat };
    } else if (propres(resultat.ajoutees, premiere.resultat.ajoutees).length > 0) {
      seconde = { entree, resultat };
      break;
    } else {
      ecartees.push(EcarteeCommePremiere);
    }
  }

  if (premiere === null) {
    notesConcurrence.push(ecartees.every((raison) => raison === EcarteeSansRequete)
      ? `${nom} : la soumission n'envoie aucune requête, rien à retarder`
      : `${nom} : aucune entrée des tests acceptée seule (${decrireEcartees(ecartees)}), essai passé`);
    return;
  }

  if (seconde === null) {
    notesConcurrence.push(`${nom} : pas de seconde entrée valide qui change l'affichage, dans les tests, ${Echantillons} ou les champs texte suffixés (${ecartees.length === 0 ? 'aucune autre candidate' : `candidate(s) écartée(s) : ${decrireEcartees(ecartees)}`}), essai passé`);
    return;
  }

  notesConcurrence.push(`${nom} : seconde entrée retenue, ${decrireEntree(geste, seconde.entree, premiere.entree)}${ecartees.length === 0 ? '' : ` (candidate(s) écartée(s) : ${decrireEcartees(ecartees)})`}`);

  const concurrence = await soumettreEnConcurrence(navigateur, geste, premiere.entree, seconde.entree);
  if (concurrence === null) {
    notesConcurrence.push(`${nom} : rejeu impossible`);
    return;
  }

  if (concurrence.retenues === 0) {
    notesConcurrence.push(`${nom} : la soumission n'envoie aucune requête, rien à retarder`);
    return;
  }

  if (concurrence.secondeImpossible) {
    notesConcurrence.push(`${nom} : seconde soumission impossible tant que la première attend sa réponse`);
    return;
  }

  if (concurrence.secondes === 0) {
    notesConcurrence.push(`${nom} : la seconde soumission n'envoie aucune requête`);
    return;
  }

  const encore = await soumettreSeule(navigateur, geste, seconde.entree);
  const attendues = encore === null ? [] : propres(communes(seconde.resultat.ajoutees, encore.ajoutees), premiere.resultat.ajoutees);
  if (attendues.length === 0) {
    notesConcurrence.push(`${nom} : l'affichage de la seconde entrée seule change d'un envoi à l'autre, essai passé`);
    return;
  }

  const manquantes = attendues.filter((ligne) => !concurrence.lignes.includes(ligne));
  if (manquantes.length === 0) {
    notesConcurrence.push(`${nom} : la seconde entrée reste affichée`);
    return;
  }

  const cle = `concurrence|${nomDeclencheur(geste)}`;
  if (!clesEcarts.has(cle)) {
    clesEcarts.add(cle);
    ecartsConcurrence.push(cle);
    const restee = concurrence.lignes.find((ligne) => propres(premiere.resultat.ajoutees, seconde.resultat.ajoutees).includes(ligne));
    emettre(cle, messageConcurrence(geste, premiere.entree, seconde.entree, manquantes[0], restee));
  }
}

async function sonderConcurrence(soumissions) {
  const gestes = lireGestesConcurrence(soumissions, lireEchantillons());
  if (gestes.length === 0) {
    return null;
  }

  debutConcurrence = Date.now();
  let erreur = null;
  let navigateurConcurrence = null;
  try {
    navigateurConcurrence = await chromium.launch();
    for (const geste of gestes) {
      await eprouverConcurrence(navigateurConcurrence, geste);
    }
  } catch (exception) {
    if (!(exception instanceof Arret)) {
      erreur = exception && exception.message ? exception.message : String(exception);
    }
  } finally {
    if (navigateurConcurrence !== null) {
      await navigateurConcurrence.close().catch(() => {});
    }
  }

  const parties = [`Sonde de concurrence : ${gestes.length} formulaire(s) relevé(s) dans le journal, ${soumissionsConcurrence} soumission(s) en ${((Date.now() - debutConcurrence) / 1000).toFixed(1)} s`, ...notesConcurrence];
  parties.push(arretConcurrence === null ? 'terminée' : `arrêtée au budget (${arretConcurrence}), écarts déjà trouvés gardés`);
  if (erreur !== null) {
    parties.push(`erreur interne : ${couper(erreur, 200)}`);
  }

  parties.push(`${ecartsConcurrence.length} écart(s)`);
  return `${parties.join(' ; ')}.`;
}

const ecartsApi = [];
let requetesApi = 0;
let debutApi = 0;
let arretApi = null;
let versionDonnees = 0;
let navigateurApi = null;
let pagesApi = ['/'];
let temoinAvant = null;
let temoinEcrit = null;
const genresSignales = new Set();

function lireJson(texte) {
  if (typeof texte !== 'string' || texte.trim().length === 0) {
    return undefined;
  }

  try {
    return JSON.parse(texte);
  } catch {
    return undefined;
  }
}

function estObjet(valeur) {
  return valeur !== null && typeof valeur === 'object' && !Array.isArray(valeur);
}

function estScalaire(valeur) {
  return valeur === null || ['string', 'number', 'boolean'].includes(typeof valeur);
}

function texteJson(lecture) {
  return JSON.stringify(lecture.json);
}

function identifiantDe(json) {
  if (!estObjet(json)) {
    return null;
  }

  for (const cle of ['id', 'Id', '_id']) {
    if (typeof json[cle] === 'string' || typeof json[cle] === 'number') {
      return String(json[cle]);
    }
  }

  return null;
}

function tableauDe(json) {
  if (Array.isArray(json)) {
    return json;
  }

  if (estObjet(json)) {
    for (const valeur of Object.values(json)) {
      if (Array.isArray(valeur) && valeur.some(estObjet)) {
        return valeur;
      }
    }
  }

  return null;
}

function elementDe(json, id) {
  const tableau = tableauDe(json);
  return tableau === null ? undefined : tableau.find((element) => identifiantDe(element) === id);
}

function cheminSeul(url) {
  const position = url.search(/[?#]/);
  return position < 0 ? url : url.slice(0, position);
}

function parent(chemin) {
  return chemin.slice(0, chemin.lastIndexOf('/'));
}

function dernierSegment(url) {
  const segment = cheminSeul(url).split('/').pop();
  try {
    return decodeURIComponent(segment);
  } catch {
    return segment;
  }
}

function urlAvecId(url, id) {
  const chemin = cheminSeul(url);
  return `${parent(chemin)}/${encodeURIComponent(id)}${url.slice(chemin.length)}`;
}

function idInexistant(url) {
  return /^\d+$/.test(dernierSegment(url)) ? '999999999' : randomUUID();
}

function routeDe(chemin, identifiants) {
  return chemin.split('/').map((segment) => (segment.length > 0 && (/^\d+$/.test(segment) || SegmentUuid.test(segment) || identifiants.has(segment)) ? '{id}' : segment)).join('/');
}

function routesDuContrat() {
  let contrat = '';
  try {
    contrat = readFileSync(options.contrat, 'utf8');
  } catch {
    return new Set();
  }

  const routes = new Set();
  for (const [, methode, chemin] of contrat.matchAll(/\b(GET|POST|PUT|PATCH|DELETE)[\s|`]+(\/[^\s`|)?#]*)/g)) {
    routes.add(`${methode} ${chemin.replace(/\/+$/, '').split('/').map((segment) => (/^(\{.*\}|:.+|<.*>|\d+)$/.test(segment) || SegmentUuid.test(segment) ? '{id}' : segment)).join('/')}`);
  }

  return routes;
}

function reussie(envoi) {
  return envoi.statut >= 200 && envoi.statut < 300;
}

function enJson(envoi) {
  return /json/i.test(envoi.type) || envoi.json !== undefined;
}

function typeEntre(envoi) {
  const type = envoi.type.split(';')[0].trim();
  if (/json/i.test(type)) {
    return '';
  }

  return type.length > 0 ? ` en ${type}` : ' sans Content-Type';
}

function extraitApi(envoi) {
  const html = /html/i.test(envoi.type) || /^\s*</.test(envoi.texte);
  return normaliser(html ? envoi.texte.replace(/<[^>]*>/g, ' ').replace(/&(?:nbsp|lt|gt|quot|#39|amp);/g, (entite) => Entites[entite]) : envoi.texte);
}

function messageApi(debut, envoi, suite, consigne) {
  const fin = `. ${consigne}`;
  const reste = LongueurMessage - fin.length;
  const extrait = envoi === null ? '' : extraitApi(envoi);
  const place = Math.min(LongueurReponseApi, reste - debut.length - suite.length - 5);
  return `${couper(extrait.length > 0 && place >= 20 ? `${debut} « ${couper(extrait, place)} »${suite}` : `${debut}${suite}`, reste)}${fin}`;
}

function signalerApi(invariant, methode, chemin, debut, envoi, suite, consigne, genre) {
  const cle = `api|${invariant}|${methode} ${chemin}`;
  if (ecartsApi.length >= MaxEcartsApi || clesEcarts.has(cle) || (genre !== undefined && genresSignales.has(`${invariant}|${genre}`))) {
    return;
  }

  if (genre !== undefined) {
    genresSignales.add(`${invariant}|${genre}`);
  }

  clesEcarts.add(cle);
  ecartsApi.push(cle);
  emettre(cle, messageApi(debut, envoi, suite, consigne));
}

function verifierDelaiApi() {
  if (Date.now() - debutApi >= DelaiApi) {
    arretApi = `délai de ${Math.round(DelaiApi / 1000)} s`;
    throw new Arret();
  }

  if (ecartsApi.length >= MaxEcartsApi) {
    arretApi = `${MaxEcartsApi} écarts`;
    throw new Arret();
  }
}

async function envoyerApi(contexte, methode, chemin, corps, typeContenu) {
  verifierDelaiApi();
  if (requetesApi >= MaxRequetesApi) {
    arretApi = `${MaxRequetesApi} requêtes`;
    throw new Arret();
  }

  requetesApi += 1;
  const envoi = { method: methode, timeout: Math.max(1000, Math.min(DelaiRequeteApi, DelaiApi - (Date.now() - debutApi))), failOnStatusCode: false, headers: {} };
  if (corps !== null) {
    envoi.data = Buffer.from(corps, 'utf8');
    if (typeContenu !== null) {
      envoi.headers['Content-Type'] = typeContenu;
    }
  }

  try {
    const reponse = await contexte.fetch(chemin, envoi);
    let texte = '';
    try {
      texte = await reponse.text();
    } catch {
    }

    return { statut: reponse.status(), type: String(reponse.headers()['content-type'] || ''), texte, json: lireJson(texte) };
  } catch {
    return null;
  } finally {
    if (methode !== 'GET') {
      versionDonnees += 1;
    }
  }
}

async function lireApi(contexte, chemin) {
  const lecture = await envoyerApi(contexte, 'GET', chemin, null, null);
  return lecture !== null && lecture.statut === 200 && /json/i.test(lecture.type) && lecture.json !== undefined ? lecture : null;
}

function verifierReponse(methode, chemin, routeJson, debut, envoi, genre) {
  if (envoi.statut >= 500) {
    signalerApi('I1', methode, chemin, `Erreur 5xx : ${debut}`, envoi, '', ConsigneApi, genre);
  }

  if (envoi.statut >= 400 && TraceApi.test(envoi.texte)) {
    signalerApi('I2', methode, chemin, `Trace de pile renvoyée : ${debut}`, envoi, '', ConsigneApi, genre);
  }

  if (routeJson && envoi.statut >= 400 && envoi.statut < 500 && envoi.texte.trim().length > 0 && !enJson(envoi)) {
    signalerApi('I3', methode, chemin, `Erreur ${/html/i.test(envoi.type) ? 'en HTML' : 'hors JSON'} sur une route JSON : ${debut}`, envoi, '', ConsigneApi, genre);
  }
}

function lireRoutes() {
  const requetes = lignesJournal().filter((objet) => objet && objet.type === 'requete' && typeof objet.methode === 'string' && typeof objet.url === 'string' && MethodesApi.has(objet.methode.toUpperCase()) && Number.isInteger(objet.statut));
  const identifiants = new Set();
  for (const requete of requetes) {
    const id = requete.statut >= 200 && requete.statut < 300 ? identifiantDe(lireJson(requete.reponse)) : null;
    if (id !== null) {
      identifiants.add(id);
    }
  }

  const routes = new Map();
  for (const requete of requetes) {
    const typeContenu = typeof requete.contentType === 'string' ? requete.contentType : null;
    const corps = typeof requete.corps === 'string' ? requete.corps : null;
    const objet = corps === null ? undefined : lireJson(corps);
    if (typeContenu !== null ? !/json/i.test(typeContenu) : corps !== null && objet === undefined) {
      continue;
    }

    const methode = requete.methode.toUpperCase();
    const chemin = routeDe(cheminSeul(requete.url), identifiants);
    const succes = requete.statut >= 200 && requete.statut < 300;
    const exemplaire = { url: requete.url, corps, objet: estObjet(objet) ? objet : null, typeContenu, score: (succes ? 2 : 0) + (estObjet(objet) ? 1 : 0), champs: estObjet(objet) ? Object.values(objet).filter((valeur) => valeur !== null && estScalaire(valeur)).length : 0 };
    const cle = `${methode} ${chemin}`;
    const route = routes.get(cle);
    if (!route) {
      routes.set(cle, { methode, chemin, ...exemplaire, succes, json: succes && /json/i.test(String(requete.typeReponse || '')) });
    } else {
      route.succes = route.succes || succes;
      route.json = route.json || (succes && /json/i.test(String(requete.typeReponse || '')));
      if (exemplaire.score > route.score || (exemplaire.score === route.score && exemplaire.champs > route.champs)) {
        Object.assign(route, exemplaire);
      }
    }
  }

  const retenues = [...routes.values()].sort((a, b) => Number(b.succes) - Number(a.succes)).slice(0, MaxRoutesApi);
  for (const route of retenues) {
    route.aId = route.chemin.endsWith('/{id}');
    route.post = route.aId ? retenues.find((autre) => autre.methode === 'POST' && autre.corps !== null && autre.chemin === parent(route.chemin)) || null : null;
    Object.assign(route, { cible: null, sale: true, stable: true, stabiliteVue: false, typeLecture: undefined, lus: new Map(), canoniques: new Map() });
  }

  return retenues;
}

async function creerEntite(contexte, route) {
  const envoi = await envoyerApi(contexte, 'POST', route.post.url, route.post.corps, route.post.typeContenu);
  if (envoi === null) {
    return null;
  }

  verifierReponse('POST', route.post.chemin, route.post.json, `POST ${route.post.url} avec le corps des tests rejoué tel quel répond ${envoi.statut}${typeEntre(envoi)}`, envoi);
  return reussie(envoi) ? identifiantDe(envoi.json) : null;
}

async function choisirLecture(contexte, route, url) {
  if (route.typeLecture === undefined) {
    if (route.aId && await lireApi(contexte, cheminSeul(url)) !== null) {
      route.typeLecture = 'detail';
    } else {
      route.typeLecture = await lireApi(contexte, route.aId ? parent(cheminSeul(url)) : cheminSeul(url)) !== null ? 'collection' : null;
    }
  }

  if (route.typeLecture === null) {
    return null;
  }

  return route.typeLecture === 'detail' || !route.aId ? cheminSeul(url) : parent(cheminSeul(url));
}

async function preparerCible(contexte, route) {
  if (route.cible !== null && !route.sale && route.cible.version === versionDonnees) {
    return route.cible;
  }

  if (route.cible !== null && !(route.sale && route.aId && route.post !== null)) {
    route.cible.reference = route.cible.lecture === null ? null : await lireApi(contexte, route.cible.lecture);
    route.cible.version = versionDonnees;
    route.sale = false;
    return route.cible;
  }

  let url = route.url;
  if (route.aId && route.post !== null) {
    const id = await creerEntite(contexte, route);
    if (id === null) {
      route.post = null;
    } else {
      url = urlAvecId(route.url, id);
    }
  }

  const lecture = await choisirLecture(contexte, route, url);
  const reference = lecture === null ? null : await lireApi(contexte, lecture);
  if (!route.stabiliteVue && reference !== null) {
    const seconde = await lireApi(contexte, lecture);
    route.stable = seconde !== null && texteJson(seconde) === texteJson(reference);
    route.stabiliteVue = true;
  }

  route.cible = { url, lecture, reference, version: versionDonnees };
  route.sale = false;
  return route.cible;
}

function valeurLue(route, lecture, url, champ) {
  if (lecture === null || !route.aId) {
    return undefined;
  }

  const objet = route.typeLecture === 'detail' ? lecture.json : elementDe(lecture.json, dernierSegment(url));
  return estObjet(objet) && Object.prototype.hasOwnProperty.call(objet, champ) ? objet[champ] : undefined;
}

function inverser(route, cible, { booleen, autre, invalide }) {
  const actuelle = valeurLue(route, cible.reference, cible.url, booleen);
  const valeur = typeof actuelle === 'boolean' ? !actuelle : !route.objet[booleen];
  return { nom: `« ${booleen} » à ${valeur} et « ${autre} » à ${JSON.stringify(invalide)}`, corps: JSON.stringify({ ...route.objet, [booleen]: valeur, [autre]: invalide }) };
}

async function executerMutation(contexte, route, mutation) {
  const cible = await preparerCible(contexte, route);
  const avant = cible.reference;
  const { nom, corps } = mutation.inverse === undefined ? mutation : inverser(route, cible, mutation.inverse);
  const url = mutation.inexistant ? urlAvecId(cible.url, idInexistant(cible.url)) : cible.url;
  const envoi = await envoyerApi(contexte, route.methode, url, corps, mutation.type);
  if (envoi === null) {
    route.sale = true;
    return null;
  }

  const debut = `${route.methode} ${url} avec ${nom} répond ${envoi.statut}${typeEntre(envoi)}`;
  verifierReponse(route.methode, route.chemin, route.json, debut, envoi, mutation.genre);
  const apres = cible.lecture === null ? null : await lireApi(contexte, cible.lecture);
  if (cible.lecture !== null) {
    const inchangee = apres !== null && cible.reference !== null && texteJson(apres) === texteJson(cible.reference);
    if (!inchangee && apres !== null && cible.reference !== null && route.stable && envoi.statut >= 400 && envoi.statut < 500) {
      signalerApi('I4', route.methode, route.chemin, `Refus qui modifie quand même : ${debut}`, envoi, `, mais GET ${cible.lecture} a changé`, ConsigneApi);
    }

    cible.reference = apres;
    cible.version = versionDonnees;
    route.sale = apres === null || (!inchangee && route.aId && route.post !== null);
  }

  return { envoi, apres, avant, url, debut };
}

function valeurRelue(route, resultat, champ) {
  const id = route.aId ? dernierSegment(resultat.url) : identifiantDe(resultat.envoi.json);
  let objet;
  if (resultat.apres !== null) {
    objet = route.typeLecture === 'detail' ? resultat.apres.json : id === null ? undefined : elementDe(resultat.apres.json, id);
  }

  if (!estObjet(objet)) {
    objet = resultat.envoi.json;
  }

  return estObjet(objet) && Object.prototype.hasOwnProperty.call(objet, champ) ? objet[champ] : undefined;
}

function corpsCanonique(route) {
  return { nom: 'le corps des tests rejoué tel quel', corps: route.corps, type: route.typeContenu };
}

async function lectureCanonique(contexte, route, champ, valeur) {
  const cle = JSON.stringify([champ, valeur]);
  if (!route.canoniques.has(cle)) {
    const resultat = await executerMutation(contexte, route, { nom: `« ${champ} » à ${JSON.stringify(valeur)}`, corps: JSON.stringify({ ...route.objet, [champ]: valeur }), type: route.typeContenu || 'application/json' });
    route.canoniques.set(cle, resultat !== null && reussie(resultat.envoi) ? valeurRelue(route, resultat, champ) : undefined);
  }

  return route.canoniques.get(cle);
}

function groupesDe(route) {
  const groupes = [];
  const type = route.typeContenu || 'application/json';
  if (route.corps !== null) {
    const tronque = route.corps.slice(0, Math.floor(route.corps.length / 2));
    groupes.push([
      { nom: `un corps tronqué « ${couper(tronque, 20)} »`, corps: tronque, type, genre: 'corps' },
      { nom: 'un corps vide en JSON', corps: '', type, genre: 'corps' },
      { nom: 'le corps null', corps: 'null', type, genre: 'corps' },
      { nom: 'le corps []', corps: '[]', type, genre: 'corps' },
      { nom: 'son corps sans Content-Type JSON', corps: route.corps, type: null, genre: 'corps' },
    ]);
  }

  if (route.objet !== null) {
    for (const [champ, valeur] of Object.entries(route.objet).filter(([, v]) => v !== null && estScalaire(v)).slice(0, MaxChampsApi)) {
      const groupe = [];
      const ajouter = (nouvelle, details) => groupe.push({ nom: `« ${champ} » à ${details.temoin ? 'la chaîne témoin' : couper(JSON.stringify(nouvelle), 40)}`, corps: JSON.stringify({ ...route.objet, [champ]: nouvelle }), type, champ, valeur: nouvelle, ...details });
      ajouter(typeof valeur === 'string' ? 123 : 'texte', { texte: typeof valeur === 'number' });
      ajouter([], {});
      if (typeof valeur === 'number') {
        ajouter(`${valeur}abc`, { partielle: valeur });
      } else if (typeof valeur === 'boolean') {
        for (const [nouvelle, canonique] of [['true', true], [1, true], ['oui', true], ['false', false], [0, false]]) {
          ajouter(nouvelle, { canonique });
        }
      } else if (DateIso.test(valeur)) {
        ajouter(valeur.replaceAll('-', ''), { canonique: valeur });
        ajouter('2026-02-30', { impossible: true });
      } else {
        suffixes += 1;
        const marque = `uzq${suffixes}w${Date.now().toString(36)}`;
        ajouter(`${Temoin} ${marque}`, { temoin: true, marque });
      }

      groupes.push(groupe);
    }

    const booleen = Object.keys(route.objet).find((champ) => typeof route.objet[champ] === 'boolean');
    const autre = Object.keys(route.objet).find((champ) => champ !== booleen && !['id', 'Id', '_id'].includes(champ) && route.objet[champ] !== null && estScalaire(route.objet[champ]));
    if (booleen !== undefined && autre !== undefined) {
      groupes.push([{ type, inverse: { booleen, autre, invalide: typeof route.objet[autre] === 'string' ? 123 : 'texte' } }]);
    }
  }

  if (route.aId) {
    groupes.push([{ nom: 'un identifiant inexistant', corps: route.corps, type: route.typeContenu, inexistant: true }]);
  }

  return groupes;
}

async function temoinDansLesPages() {
  if (navigateurApi === null) {
    navigateurApi = await chromium.launch();
  }

  for (const chemin of pagesApi) {
    verifierDelaiApi();
    let contexte = null;
    try {
      contexte = await navigateurApi.newContext({ baseURL: options.base });
      const page = await contexte.newPage();
      await page.goto(new URL(chemin, options.base).href, { waitUntil: 'load', timeout: Math.max(1000, Math.min(DelaiChargement, DelaiApi - (Date.now() - debutApi))) });
      await attendreStable(page, { enVol: 0 }, CalmeApresClic);
      if (await page.evaluate(() => document.querySelector('[data-usine]') !== null || window.__usineXss === 1)) {
        return chemin;
      }
    } catch {
    } finally {
      if (contexte !== null) {
        await contexte.close().catch(() => {});
      }
    }
  }

  return null;
}

async function essayerMutation(contexte, route, mutation) {
  if (mutation.temoin && temoinAvant === null) {
    temoinAvant = await temoinDansLesPages() !== null;
  }

  const resultat = await executerMutation(contexte, route, mutation);
  if (resultat === null || mutation.champ === undefined) {
    return;
  }

  const champ = mutation.champ;
  const accepte = reussie(resultat.envoi);
  const relue = accepte ? valeurRelue(route, resultat, champ) : undefined;
  const avant = valeurLue(route, resultat.avant, resultat.url, champ);
  const changee = avant === undefined || JSON.stringify(relue) !== JSON.stringify(avant);
  if (mutation.texte) {
    route.lus.set(champ, relue);
  } else if (mutation.partielle !== undefined) {
    if (accepte && (relue === mutation.partielle || relue === String(mutation.partielle)) && JSON.stringify(route.lus.get(champ)) !== JSON.stringify(relue)) {
      signalerApi('I5', route.methode, route.chemin, `Lecture partielle : ${resultat.debut}`, resultat.envoi, ` et relu ${JSON.stringify(relue)}`, ConsigneApi);
    }
  } else if (mutation.canonique !== undefined) {
    if (accepte && relue !== undefined) {
      const attendue = await lectureCanonique(contexte, route, champ, mutation.canonique);
      if (attendue !== undefined && JSON.stringify(attendue) !== JSON.stringify(relue)) {
        signalerApi('A6', route.methode, route.chemin, `Valeur relue autrement : ${resultat.debut}`, resultat.envoi, ` et relu ${couper(JSON.stringify(relue), 40)}, alors que ${JSON.stringify(mutation.canonique)} est relu ${couper(JSON.stringify(attendue), 40)}`, ConsigneApi);
      }
    }
  } else if (mutation.impossible) {
    if (accepte && relue !== undefined && changee) {
      signalerApi('A6', route.methode, route.chemin, `Date impossible acceptée : ${resultat.debut}`, resultat.envoi, ` et relue ${couper(JSON.stringify(relue), 40)}`, ConsigneApi);
    }
  } else if (mutation.temoin && accepte) {
    if (temoinEcrit === null) {
      temoinEcrit = { route, champ, url: resultat.url };
    }

    if (typeof relue === 'string' && relue !== mutation.valeur && (relue.includes(mutation.marque) || (avant !== undefined && changee))) {
      signalerApi('U3', route.methode, route.chemin, `Texte altéré : ${resultat.debut}`, null, ` et relu « ${couper(relue, 80)} »`, ConsigneTexte);
    }
  }
}

async function verifierCoherences(contexte, routes) {
  for (const route of routes) {
    if (route.objet === null || route.methode === 'DELETE' || (route.aId && route.post === null)) {
      continue;
    }

    const resultat = await executerMutation(contexte, route, corpsCanonique(route));
    if (resultat === null || !reussie(resultat.envoi)) {
      continue;
    }

    const collection = route.aId ? parent(cheminSeul(resultat.url)) : cheminSeul(resultat.url);
    const id = route.aId ? dernierSegment(resultat.url) : identifiantDe(resultat.envoi.json);
    if (id === null) {
      continue;
    }

    const liste = await lireApi(contexte, collection);
    const detail = await lireApi(contexte, `${collection}/${encodeURIComponent(id)}`);
    const element = liste === null ? undefined : elementDe(liste.json, id);
    if (detail === null || !estObjet(detail.json) || !estObjet(element)) {
      continue;
    }

    const champ = Object.keys(detail.json).find((cle) => estScalaire(detail.json[cle]) && Object.prototype.hasOwnProperty.call(element, cle) && estScalaire(element[cle]) && element[cle] !== detail.json[cle]);
    if (champ !== undefined) {
      signalerApi('A5', route.methode, route.chemin, `Liste et détail en désaccord : après ${route.methode} ${resultat.url} avec le corps des tests (réponse ${resultat.envoi.statut}), GET ${collection} donne « ${champ} » à ${couper(JSON.stringify(element[champ]), 40)} et GET ${collection}/${encodeURIComponent(id)} à ${couper(JSON.stringify(detail.json[champ]), 40)}`, null, '', ConsigneCoherence);
    }
  }
}

async function verifierHorsContrat(contexte, routes) {
  const prevues = routesDuContrat();
  const collections = new Map();
  for (const route of routes) {
    const collection = route.aId ? parent(route.chemin) : route.methode === 'POST' ? route.chemin : null;
    if (collection !== null && !collections.has(collection)) {
      collections.set(collection, { chemin: route.aId ? parent(cheminSeul(route.url)) : cheminSeul(route.url), corps: null });
    }

    if (collection !== null && route.methode === 'POST' && route.corps !== null) {
      collections.get(collection).corps = route.corps;
    }
  }

  for (const [collection, { chemin, corps }] of collections) {
    for (const methode of ['DELETE', 'PUT']) {
      if (prevues.has(`${methode} ${collection}`) || routes.some((route) => route.methode === methode && route.chemin === collection) || (methode === 'PUT' && corps === null)) {
        continue;
      }

      const avant = await lireApi(contexte, chemin);
      if (avant === null) {
        continue;
      }

      const envoi = await envoyerApi(contexte, methode, chemin, methode === 'PUT' ? corps : null, methode === 'PUT' ? 'application/json' : null);
      if (envoi === null) {
        continue;
      }

      const debut = `${methode} ${chemin} (sans identifiant, absent du contrat)${methode === 'PUT' ? ' avec le corps du POST des tests' : ''} répond ${envoi.statut}${typeEntre(envoi)}`;
      verifierReponse(methode, collection, false, debut, envoi);
      const apres = reussie(envoi) ? await lireApi(contexte, chemin) : null;
      if (apres !== null && texteJson(apres) !== texteJson(avant)) {
        const nombres = [tableauDe(avant.json), tableauDe(apres.json)];
        const detail = nombres.every((tableau) => tableau !== null) ? ` (${nombres[0].length} élément(s) avant, ${nombres[1].length} après)` : '';
        signalerApi('A7', methode, collection, `Route hors contrat qui modifie les données : ${debut}`, envoi, `, et GET ${chemin} a changé${detail}`, ConsigneHorsContrat);
      }
    }
  }
}

async function explorerApi(contexte, routes) {
  await verifierCoherences(contexte, routes);
  const plans = routes.map((route) => ({ route, groupes: groupesDe(route) }));
  const tours = Math.max(0, ...plans.map((plan) => plan.groupes.length));
  for (let tour = 0; tour < tours; tour += 1) {
    for (const { route, groupes } of plans) {
      for (const mutation of groupes[tour] || []) {
        await essayerMutation(contexte, route, mutation);
      }
    }
  }

  if (temoinEcrit !== null && temoinAvant === false) {
    const page = await temoinDansLesPages();
    if (page !== null) {
      signalerApi('U3', temoinEcrit.route.methode, temoinEcrit.route.chemin, `Texte interprété comme du HTML : la chaîne témoin écrite par ${temoinEcrit.route.methode} ${temoinEcrit.url} dans « ${temoinEcrit.champ} » crée un élément <b data-usine> ou exécute son script sur la page ${page}, rechargée après la sonde`, null, '', ConsigneTexte);
    }
  }

  await verifierHorsContrat(contexte, routes);
}

async function sonderApi(soumissions) {
  const routes = lireRoutes();
  if (routes.length === 0) {
    return null;
  }

  debutApi = Date.now();
  pagesApi = [...new Set(['/', ...soumissions.map((soumission) => soumission.url)])].slice(0, MaxPagesApi);
  let erreur = null;
  let contexte = null;
  try {
    contexte = await request.newContext({ baseURL: options.base });
    await explorerApi(contexte, routes);
  } catch (exception) {
    if (!(exception instanceof Arret)) {
      erreur = exception && exception.message ? exception.message : String(exception);
    }
  } finally {
    if (contexte !== null) {
      await contexte.dispose().catch(() => {});
    }

    if (navigateurApi !== null) {
      await navigateurApi.close().catch(() => {});
    }
  }

  const parties = [`Sonde d'API : ${routes.length} route(s) d'écriture JSON retenue(s) dans le journal, ${requetesApi} requête(s) en ${((Date.now() - debutApi) / 1000).toFixed(1)} s`];
  parties.push(arretApi === null ? 'terminée' : `arrêtée au budget (${arretApi}), écarts déjà trouvés gardés`);
  if (erreur !== null) {
    parties.push(`erreur interne : ${couper(erreur, 200)}`);
  }

  parties.push(`${ecartsApi.length} écart(s)`);
  return `${parties.join(' ; ')}.`;
}

let nombreGestes = 0;
let erreurInterne = null;
let navigateur = null;
const soumissions = lireSoumissions();
try {
  const gestes = lireGestes(soumissions);
  nombreGestes = gestes.length;
  if (gestes.length > 0) {
    navigateur = await chromium.launch();
    for (const geste of gestes) {
      await explorerGeste(navigateur, geste);
    }

    for (const geste of gestes) {
      for (const champNumerique of geste.champsNumeriques || []) {
        await chercherZeroNegatif(navigateur, geste, champNumerique);
      }
    }

    for (const geste of gestes) {
      for (const champNumerique of geste.champsNumeriques || []) {
        await verifierArrondi(navigateur, geste, champNumerique);
      }
    }
  }
} catch (erreur) {
  if (!(erreur instanceof Arret)) {
    erreurInterne = erreur && erreur.message ? erreur.message : String(erreur);
  }
} finally {
  if (navigateur !== null) {
    await navigateur.close().catch(() => {});
  }
}

const bilanConcurrence = await sonderConcurrence(soumissions);
const bilanApi = await sonderApi(soumissions);
process.stdout.write(`@@usine-sonde ${Buffer.from(`${bilan(nombreGestes, erreurInterne)}${bilanConcurrence === null ? '' : ` ${bilanConcurrence}`}${bilanApi === null ? '' : ` ${bilanApi}`}`, 'utf8').toString('base64')}\n`);
