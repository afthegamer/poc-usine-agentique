import { libelles } from '../textes/libelles';

const textes = libelles.discussion;
export const SANS_REPONSE = '(pas de réponse)';
export const APPELS_CONSERVES = "(les appels d'outils ci-dessus ont bien eu lieu";
const QUESTIONS: ReadonlyMap<string, string> = new Map([
  ['valider_etape', textes.questions.avis],
  ['annuler_demande', textes.questions.annulation],
  ['reprendre_demande', textes.questions.reprise],
]);

export function question(texte: string): string {
  const outil = /appeler ([^(]*)\(/.exec(texte)?.[1]?.trim() ?? '';
  return QUESTIONS.get(outil) ?? textes.questions.autre;
}
