import { chromium } from 'playwright';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const dossier = process.argv[2] ?? '/opt/echantillons';

const pages = [
  [
    'Relevé de notes de Zoé Lefèvre',
    'Page 1 sur 3 : matières littéraires',
    "Français : 15,5 sur 20, à l'aise à l'oral",
  ],
  [
    'Page 2 sur 3 : mathématiques et sciences',
    'Mathématiques : 18 sur 20, très bon trimestre',
    'Physique-chimie : 12 sur 20, des progrès à faire',
  ],
  [
    'Page 3 sur 3 : appréciation générale',
    'Élève sérieuse et curieuse, bravo !',
    'Côté vie scolaire : aucune absence, ça continue.',
  ],
];

const pagesDocument2 = [
  [
    'Compte rendu de la réunion d’équipe',
    'Page 1 sur 2 : rapport annuel',
    'Hélène présente le rapport, l’équipe l’approuve.',
  ],
  [
    'Page 2 sur 2 : décisions et calendrier',
    'Le port de Saint-Malo reçoit l’équipe en été.',
    'Prochaine réunion : 12 mars 2027, à 9 h 30.',
  ],
];

const echapper = (texte) => texte.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');

const html = (pages) => `<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<style>
body { font-family: sans-serif; font-size: 16pt; font-variant-ligatures: none; font-kerning: none; }
section { break-after: page; }
section:last-child { break-after: auto; }
p { margin: 0 0 12pt 0; }
</style>
</head>
<body>
${pages.map((lignes) => `<section>${lignes.map((ligne) => `<p>${echapper(ligne)}</p>`).join('')}</section>`).join('\n')}
</body>
</html>`;

mkdirSync(dossier, { recursive: true });
const navigateur = await chromium.launch();
try {
  const page = await navigateur.newPage();
  await page.setContent(html(pages));
  await page.pdf({ path: join(dossier, 'document.pdf'), format: 'A4' });
  await page.setContent(html(pagesDocument2));
  await page.pdf({ path: join(dossier, 'document2.pdf'), format: 'A4' });
  await page.setContent('<!doctype html><html lang="fr"><body></body></html>');
  await page.pdf({ path: join(dossier, 'sans-texte.pdf'), format: 'A4' });
} finally {
  await navigateur.close();
}

const document = readFileSync(join(dossier, 'document.pdf'));
writeFileSync(join(dossier, 'tronque.pdf'), document.subarray(0, Math.floor(document.length / 2)));
writeFileSync(join(dossier, 'document-texte-attendu.txt'), `${pages.flat().join('\n')}\n`);
writeFileSync(join(dossier, 'document2-texte-attendu.txt'), `${pagesDocument2.flat().join('\n')}\n`);
writeFileSync(join(dossier, 'pas-un-pdf.txt'), "Ceci est un simple fichier texte, ce n'est pas un PDF.\n");
writeFileSync(join(dossier, 'faux.pdf'), "Ce fichier porte l'extension .pdf, mais ce n'est pas un PDF.\n");
