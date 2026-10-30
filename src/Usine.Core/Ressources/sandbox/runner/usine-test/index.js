const fs = require('node:fs');
const path = require('node:path');
const base = require('playwright/test');

const NomPiece = 'usine-console';
const NomPieceGarde = 'usine-garde';
const MaxMessages = 20;
const MaxLongueurMessage = 500;
const MaxLignesGarde = 12;
const LongueurExtrait = 60;
const MaxLignesJournal = 300;
const MaxCorpsJournal = 4096;
const MethodesJournalisees = new Set(['fetch', 'post', 'put', 'patch', 'delete']);

let lignesJournal = 0;

function couper(texte) {
  return texte.length > MaxLongueurMessage ? `${texte.slice(0, MaxLongueurMessage)}…` : texte;
}

function decrireErreur(erreur) {
  const stack = typeof erreur.stack === 'string' ? erreur.stack.split('\n') : [];
  const premiereLigne = stack[0] && stack[0].includes(erreur.message) ? stack[0] : `${erreur.name || 'Error'}: ${erreur.message}`;
  const localisation = stack.slice(1, 3).map((ligne) => ligne.trim()).filter((ligne) => ligne.length > 0);
  return [premiereLigne, ...localisation].join(' ');
}

function attendre(millisecondes) {
  return new Promise((resoudre) => setTimeout(resoudre, millisecondes));
}

function gardeActive() {
  return process.env.USINE_GARDE === '1';
}

function dossierJournal() {
  const dossier = process.env.USINE_JOURNAL;
  return typeof dossier === 'string' && dossier.length > 0 ? dossier : null;
}

function titreComplet(testInfo) {
  return testInfo.titlePath.slice(1).join(' › ');
}

function journaliser(ligne) {
  const dossier = dossierJournal();
  if (dossier === null || lignesJournal >= MaxLignesJournal) {
    return;
  }

  try {
    fs.mkdirSync(dossier, { recursive: true });
    fs.appendFileSync(path.join(dossier, `${process.pid}.jsonl`), `${JSON.stringify(ligne)}\n`);
    lignesJournal += 1;
  } catch {
  }
}

function tronquerCorps(texte) {
  if (typeof texte !== 'string') {
    return null;
  }

  return texte.length > MaxCorpsJournal ? texte.slice(0, MaxCorpsJournal) : texte;
}

function cheminEtRequete(url) {
  try {
    const analyse = new URL(url);
    return `${analyse.pathname}${analyse.search}`;
  } catch {
    return url;
  }
}

function cheminSeul(url) {
  try {
    return new URL(url).pathname;
  } catch {
    return url;
  }
}

function memeOrigine(url, origine) {
  try {
    return origine !== null && new URL(url).origin === origine;
  } catch {
    return false;
  }
}

function origineDe(baseURL) {
  try {
    return new URL(baseURL || process.env.BASE_URL || '').origin;
  } catch {
    return null;
  }
}

function normaliser(texte) {
  return String(texte).replace(/\s+/g, ' ').trim();
}

function extrait(texte) {
  const propre = normaliser(texte);
  return propre.length > LongueurExtrait ? propre.slice(0, LongueurExtrait) : propre;
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

function installerDansLaPage(options) {
  if (window.top !== window || Object.prototype.hasOwnProperty.call(window, '__usineGarde')) {
    return;
  }

  const normaliser = (texte) => String(texte).replace(/\s+/g, ' ').trim();

  const signaler = (charge) => {
    try {
      const envoi = window.__usineSignaler(charge);
      if (envoi && typeof envoi.catch === 'function') {
        envoi.catch(() => {});
      }
    } catch {
    }
  };

  const Motifs = [
    ['zero-negatif', /(?:^|[\s(:=>«"'])[-−‑]0(?:[.,]0+)?(?![\d.,])/g],
    ['undefined', /\bundefined\b/g],
    ['NaN', /\bNaN\b/g],
    ['objet', /\[object (?:Object|Promise|Array|HTMLElement)\]/g],
    ['trace', /^\s*at (?:[^\n(]*\()?(?:file:\/\/|\/|[A-Za-z]:[\\/]|node:)[^\n]*:\d+:\d+\)?\s*$|Traceback \(most recent call last\)|^\s*File "[^"]+", line \d+|\bnode_modules\/|\.cs:line \d+/gm],
  ];

  const analyser = () => {
    const corps = document.body;
    if (!corps) {
      return [];
    }

    const texte = String(corps.innerText || '').slice(0, 200000);
    const trouvees = [];
    for (const [motif, expression] of Motifs) {
      expression.lastIndex = 0;
      let correspondance = expression.exec(texte);
      while (correspondance !== null) {
        const brut = correspondance[0];
        const jeton = (motif === 'zero-negatif' ? brut.replace(/^[\s(:=>«"']/, '') : brut).trim();
        const debutLigne = texte.lastIndexOf('\n', correspondance.index - 1) + 1;
        const finLigne = texte.indexOf('\n', correspondance.index + brut.length);
        const debut = Math.max(debutLigne, correspondance.index - 20);
        const fin = Math.min(finLigne < 0 ? texte.length : finLigne, correspondance.index + brut.length + 40);
        trouvees.push({ motif, jeton, extrait: texte.slice(debut, fin), chemin: location.pathname });
        if (correspondance[0].length === 0) {
          expression.lastIndex += 1;
        }

        correspondance = expression.exec(texte);
      }
    }

    return trouvees;
  };

  const vues = new Set();
  const lire = () => {
    try {
      for (const anomalie of analyser()) {
        const cle = `${anomalie.motif}|${anomalie.jeton}|${anomalie.extrait}`;
        if (!vues.has(cle)) {
          vues.add(cle);
          signaler({ type: 'anomalie', ...anomalie });
        }
      }
    } catch {
    }
  };

  Object.defineProperty(window, '__usineGarde', { value: () => {
    try {
      return analyser();
    } catch {
      return [];
    }
  }, enumerable: false });

  if (options.garde) {
    let minuterie = null;
    const planifier = () => {
      if (minuterie === null) {
        minuterie = setTimeout(() => {
          minuterie = null;
          lire();
        }, 100);
      }
    };
    try {
      new MutationObserver(planifier).observe(document, { subtree: true, childList: true, characterData: true });
    } catch {
    }

    document.addEventListener('DOMContentLoaded', planifier, true);
    window.addEventListener('load', planifier, true);
    document.addEventListener('input', (evenement) => {
      try {
        const cible = evenement.target;
        if (cible && typeof cible.value === 'string' && cible.type !== 'password') {
          signaler({ type: 'saisie', valeur: cible.value });
        }
      } catch {
      }
    }, true);
  }

  if (!options.journal) {
    return;
  }

  const selecteurDe = (element) => {
    if (element.id) {
      return `#${CSS.escape(element.id)}`;
    }

    const nom = element.getAttribute('name');
    if (nom) {
      return `${element.tagName.toLowerCase()}[name="${CSS.escape(nom)}"]`;
    }

    const parties = [];
    let courant = element;
    while (courant && courant.nodeType === 1 && courant !== document.body && courant !== document.documentElement) {
      if (courant.id) {
        parties.unshift(`#${CSS.escape(courant.id)}`);
        break;
      }

      const parent = courant.parentElement;
      const memes = parent ? Array.from(parent.children).filter((enfant) => enfant.tagName === courant.tagName) : [];
      const etiquette = courant.tagName.toLowerCase();
      parties.unshift(memes.length > 1 ? `${etiquette}:nth-of-type(${memes.indexOf(courant) + 1})` : etiquette);
      courant = parent;
    }

    return parties.join(' > ');
  };

  const libelleDe = (element) => {
    if (element.labels && element.labels.length > 0) {
      return normaliser(element.labels[0].innerText || '');
    }

    return element.getAttribute('aria-label') || null;
  };

  const nomAccessible = (element) => normaliser(element.getAttribute('aria-label') || element.innerText || element.value || element.getAttribute('title') || '');

  const roleDe = (element) => element.getAttribute('role') || (element.tagName === 'BUTTON' || element.tagName === 'INPUT' ? 'button' : element.tagName.toLowerCase());

  const saisis = new WeakSet();
  document.addEventListener('input', (evenement) => {
    try {
      if (evenement.target) {
        saisis.add(evenement.target);
      }
    } catch {
    }
  }, true);

  const champsDe = (declencheur) => {
    const formulaire = declencheur.form instanceof HTMLFormElement ? declencheur.form : declencheur.closest ? declencheur.closest('form') : null;
    const elements = formulaire ? Array.from(formulaire.elements) : Array.from(document.querySelectorAll('input, select, textarea'));
    return elements
      .filter((element) => ['INPUT', 'SELECT', 'TEXTAREA'].includes(element.tagName))
      .filter((element) => !['hidden', 'password', 'submit', 'button', 'reset', 'image'].includes(String(element.type).toLowerCase()))
      .map((element) => {
        const champ = { selecteur: selecteurDe(element), libelle: libelleDe(element), type: element.tagName === 'INPUT' ? element.type : element.tagName.toLowerCase(), valeur: element.value, parDefaut: !saisis.has(element), autreFormulaire: !formulaire && Boolean(element.form) };
        if (element.tagName === 'SELECT') {
          champ.option = element.selectedOptions && element.selectedOptions.length > 0 ? normaliser(element.selectedOptions[0].text) : null;
          const options = Array.from(element.options);
          const initiale = options.find((option) => option.defaultSelected) || options.find((option) => !option.disabled);
          champ.parDefaut = champ.parDefaut || element.value === (initiale ? initiale.value : '');
        }

        if (element.type === 'checkbox' || element.type === 'radio') {
          champ.coche = element.checked;
          champ.parDefaut = champ.parDefaut || element.checked === element.defaultChecked;
        }

        return champ;
      });
  };

  let dernierClic = null;
  const soumettre = (declencheur) => {
    try {
      signaler({
        type: 'soumission',
        url: `${location.pathname}${location.search}`,
        declencheur: { id: declencheur.id || null, role: roleDe(declencheur), nom: nomAccessible(declencheur) },
        champs: champsDe(declencheur),
      });
    } catch {
    }
  };

  document.addEventListener('click', (evenement) => {
    try {
      const cible = evenement.target && evenement.target.closest
        ? evenement.target.closest('button, input[type=submit], input[type=button], [role=button]')
        : null;
      if (cible) {
        dernierClic = cible;
        soumettre(cible);
      }
    } catch {
    }
  }, true);

  document.addEventListener('submit', (evenement) => {
    try {
      const declencheur = evenement.submitter || evenement.target;
      if (declencheur !== dernierClic) {
        soumettre(declencheur);
      }

      dernierClic = null;
    } catch {
    }
  }, true);
}

function typeContenuEnvoye(url, options) {
  if (!options) {
    return typeof url === 'string' ? null : url.headers()['content-type'] || null;
  }

  const enTetes = options.headers || {};
  const cle = Object.keys(enTetes).find((nom) => nom.toLowerCase() === 'content-type');
  if (cle) {
    return enTetes[cle];
  }

  if (options.form) {
    return 'application/x-www-form-urlencoded';
  }

  if (options.multipart) {
    return 'multipart/form-data';
  }

  return options.data !== undefined && options.data !== null && typeof options.data === 'object' && !Buffer.isBuffer(options.data) ? 'application/json' : null;
}

function journaliserRequetes(request, testInfo) {
  return new Proxy(request, {
    get(cible, nom) {
      const valeur = Reflect.get(cible, nom, cible);
      if (typeof valeur !== 'function') {
        return valeur;
      }

      if (!MethodesJournalisees.has(nom)) {
        return valeur.bind(cible);
      }

      return async (url, options) => {
        const reponse = await valeur.call(cible, url, options);
        try {
          const methode = String((options && options.method) || (nom === 'fetch' ? (typeof url === 'string' ? 'GET' : url.method()) : nom)).toUpperCase();
          if (methode !== 'GET') {
            const donnees = options ? (options.data ?? options.form ?? options.multipart) : (typeof url === 'string' ? undefined : url.postData());
            let texteReponse = null;
            try {
              texteReponse = await reponse.text();
            } catch {
            }

            journaliser({
              type: 'requete',
              test: titreComplet(testInfo),
              methode,
              url: cheminEtRequete(reponse.url()),
              contentType: typeContenuEnvoye(url, options),
              corps: donnees === undefined || donnees === null ? null : tronquerCorps(typeof donnees === 'string' ? donnees : Buffer.isBuffer(donnees) ? donnees.toString('utf8') : JSON.stringify(donnees)),
              statut: reponse.status(),
              typeReponse: reponse.headers()['content-type'] || null,
              reponse: tronquerCorps(texteReponse),
            });
          }
        } catch {
        }

        return reponse;
      };
    },
  });
}

const test = base.test.extend({
  context: async ({ context, baseURL }, use, testInfo) => {
    const messages = [];
    let ignores = 0;
    const noter = (message) => {
      if (messages.length < MaxMessages) {
        messages.push(couper(message));
      } else {
        ignores += 1;
      }
    };
    const garde = gardeActive();
    const journal = dossierJournal() !== null;
    const origine = origineDe(baseURL);
    const anomalies = [];
    const saisies = new Set();
    const enCours = new Set();
    const enVol = new Set();
    const noterAnomalie = (anomalie) => {
      if (anomalies.length < 200) {
        anomalies.push(anomalie);
      }
    };

    context.on('weberror', (erreur) => {
      noter(`Erreur JavaScript : ${decrireErreur(erreur.error())}`);
      if (garde) {
        try {
          const page = erreur.page();
          const message = erreur.error().message || String(erreur.error());
          noterAnomalie({ motif: 'exception', jeton: '', extrait: message, chemin: page ? cheminSeul(page.url()) : '/' });
        } catch {
        }
      }
    });
    context.on('console', (message) => {
      if (message.type() === 'error') {
        const lieu = message.location();
        const source = lieu && lieu.url ? ` (${lieu.url}:${lieu.lineNumber + 1})` : '';
        noter(`console.error : ${message.text()}${source}`);
      }
    });

    if (garde || journal) {
      context.on('request', (requete) => enVol.add(requete));
      context.on('requestfinished', (requete) => enVol.delete(requete));
      context.on('requestfailed', (requete) => enVol.delete(requete));
      try {
        await context.exposeBinding('__usineSignaler', (source, charge) => {
          try {
            if (!charge || typeof charge !== 'object') {
              return;
            }

            if (charge.type === 'anomalie' && garde) {
              noterAnomalie({ motif: String(charge.motif), jeton: String(charge.jeton || ''), extrait: String(charge.extrait || ''), chemin: String(charge.chemin || '/') });
            } else if (charge.type === 'saisie' && garde) {
              if (saisies.size < 500) {
                saisies.add(String(charge.valeur));
              }
            } else if (charge.type === 'soumission' && journal) {
              journaliser({ type: 'soumission', test: titreComplet(testInfo), url: charge.url, declencheur: charge.declencheur, champs: charge.champs });
            }
          } catch {
          }
        });
        await context.addInitScript(installerDansLaPage, { garde, journal });
      } catch {
      }
    }

    if (garde) {
      context.on('response', (reponse) => {
        try {
          if (reponse.status() >= 500 && memeOrigine(reponse.url(), origine)) {
            const chemin = cheminSeul(reponse.url());
            noterAnomalie({ motif: 'reponse-5xx', jeton: '', extrait: `${reponse.request().method()} ${chemin} → ${reponse.status()}`, chemin });
          }
        } catch {
        }
      });
    }

    if (journal) {
      context.on('requestfinished', (requete) => {
        const traitement = (async () => {
          try {
            if (requete.method() === 'GET' || !memeOrigine(requete.url(), origine)) {
              return;
            }

            const reponse = await requete.response();
            let texteReponse = null;
            try {
              texteReponse = reponse ? await reponse.text() : null;
            } catch {
            }

            journaliser({
              type: 'requete',
              test: titreComplet(testInfo),
              methode: requete.method(),
              url: cheminEtRequete(requete.url()),
              contentType: (await requete.allHeaders())['content-type'] || null,
              corps: tronquerCorps(requete.postData()),
              statut: reponse ? reponse.status() : null,
              typeReponse: reponse ? reponse.headers()['content-type'] || null : null,
              reponse: tronquerCorps(texteReponse),
            });
          } catch {
          }
        })();
        enCours.add(traitement);
        traitement.finally(() => enCours.delete(traitement));
      });
    }

    await use(context);

    if (garde || journal) {
      const limite = Date.now() + 2000;
      await attendre(150);
      while (enVol.size > 0 && Date.now() < limite) {
        await attendre(50);
      }
    }

    if (journal && enCours.size > 0) {
      await Promise.race([Promise.allSettled([...enCours]), attendre(2000)]);
    }

    if (garde) {
      try {
        await Promise.race([
          Promise.allSettled(context.pages().map(async (page) => {
            const trouvees = await page.evaluate(() => (typeof window.__usineGarde === 'function' ? window.__usineGarde() : []));
            for (const anomalie of trouvees) {
              noterAnomalie(anomalie);
            }
          })),
          attendre(2000),
        ]);
      } catch {
      }

      try {
        const lignes = [];
        const vues = new Set();
        for (const anomalie of anomalies) {
          const texte = extrait(anomalie.extrait);
          const cle = `${anomalie.motif}|${texte}`;
          if (vues.has(cle) || [...saisies].some((valeur) => contientJeton(valeur, anomalie.jeton))) {
            continue;
          }

          vues.add(cle);
          lignes.push(`${anomalie.motif} | ${normaliser(anomalie.chemin) || '/'} | ${texte}`);
        }

        if (lignes.length > 0) {
          await testInfo.attach(NomPieceGarde, { body: lignes.slice(0, MaxLignesGarde).join('\n'), contentType: 'text/plain' });
        }
      } catch {
      }
    }

    if (messages.length > 0 && testInfo.status !== testInfo.expectedStatus) {
      if (ignores > 0) {
        messages.push(`… et ${ignores} autre(s) message(s) non joint(s).`);
      }
      await testInfo.attach(NomPiece, { body: messages.join('\n'), contentType: 'text/plain' });
    }
  },
  request: async ({ request }, use, testInfo) => {
    await use(dossierJournal() === null ? request : journaliserRequetes(request, testInfo));
  },
});

module.exports = Object.assign(test, {
  chromium: base.chromium,
  firefox: base.firefox,
  webkit: base.webkit,
  selectors: base.selectors,
  devices: base.devices,
  errors: base.errors,
  request: base.request,
  _electron: base._electron,
  _android: base._android,
  _baseTest: base._baseTest,
  _utilityTest: base._utilityTest,
  test,
  expect: base.expect,
  defineConfig: base.defineConfig,
  mergeTests: base.mergeTests,
  mergeExpects: base.mergeExpects,
});
