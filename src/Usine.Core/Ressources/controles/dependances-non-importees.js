const fs = require('fs');
const path = require('path');

const dossiersIgnores = new Set(['node_modules', '.git', '.angular', '.next', '.nuxt', '.cache', '.venv', 'venv', '__pycache__']);
const manifestes = new Set(['package.json', 'package-lock.json', 'npm-shrinkwrap.json', 'yarn.lock', 'pnpm-lock.yaml']);
const tailleMax = 2 * 1024 * 1024;

function parcourir(dossier, visiter) {
  let entrees;
  try {
    entrees = fs.readdirSync(dossier, { withFileTypes: true });
  } catch {
    return;
  }

  for (const entree of entrees.sort((a, b) => a.name.localeCompare(b.name))) {
    const chemin = path.join(dossier, entree.name);
    if (entree.isDirectory()) {
      if (!dossiersIgnores.has(entree.name)) {
        parcourir(chemin, visiter);
      }
    } else if (entree.isFile()) {
      visiter(chemin, entree.name);
    }
  }
}

function echapper(texte) {
  return texte.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&');
}

const packages = [];
parcourir('.', (chemin, nom) => {
  if (nom === 'package.json') {
    packages.push(chemin);
  }
});

const signalees = [];
packages.sort((a, b) => a.split(path.sep).length - b.split(path.sep).length || a.localeCompare(b));
for (const fichierPackage of packages) {
  let dependances;
  try {
    dependances = Object.keys(JSON.parse(fs.readFileSync(fichierPackage, 'utf8')).dependencies || {});
  } catch {
    continue;
  }

  const restantes = new Map(dependances.map((nom) => [nom, new RegExp(`['"\`]${echapper(nom)}(?:/[^'"\`]*)?['"\`]|node_modules/${echapper(nom)}(?![\\w.-])`)]));
  const racine = path.dirname(fichierPackage);
  parcourir(racine, (chemin, nom) => {
    if (restantes.size === 0 || manifestes.has(nom)) {
      return;
    }

    let contenu;
    try {
      if (fs.statSync(chemin).size > tailleMax) {
        return;
      }

      contenu = fs.readFileSync(chemin, 'utf8');
    } catch {
      return;
    }

    for (const [nomDependance, motif] of restantes) {
      if (motif.test(contenu)) {
        restantes.delete(nomDependance);
      }
    }
  });

  for (const nomDependance of restantes.keys()) {
    signalees.push(racine === '.' ? nomDependance : `${nomDependance} (${path.join(racine, 'package.json')})`);
  }
}

process.stdout.write(signalees.join('\n'));
