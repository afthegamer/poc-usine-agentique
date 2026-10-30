import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { libelles } from '../textes/libelles';

export type Action =
  | 'demande'
  | 'detail'
  | 'lancement-essai'
  | 'fermeture-essai'
  | 'ouverture-discussion'
  | 'discussion'
  | 'parametres';

interface Traduction {
  readonly debut: string;
  readonly contient?: readonly string[];
  readonly texte: string;
}

const textes = libelles.erreurs;
const IDENTIFIANT_INVALIDE = 'Identifiant de demande invalide';
const PROJET_INCONNU = 'Projet inconnu';
const DEMANDE = 'La demande';
const CREEE_SANS_WORKER = "est créée, mais son worker n'a pas pu être lancé";
const STATUTS_DETAIL_PARAMETRES: readonly number[] = [
  HttpStatusCode.BadRequest,
  HttpStatusCode.NotFound,
  HttpStatusCode.BadGateway,
];
const CLE_D_ABORD = "Ajoute d'abord la clé de ce service.";
export const ADRESSE_INVALIDE = "Cette adresse n'est pas valide.";

const TRADUCTIONS: readonly Traduction[] = [
  { debut: 'Docker ne répond pas', texte: textes.docker },
  { debut: "L'image", texte: textes.image },
  { debut: 'Le port', texte: textes.siteNonDemarre },
  { debut: "Docker n'a pas pu lancer l'application", texte: textes.siteNonDemarre },
  { debut: "Docker n'a pas pu arrêter l'application", texte: textes.siteNonFerme },
  { debut: 'Le projet', contient: ['a déjà une évolution en cours'], texte: textes.ajoutEnCours },
  { debut: 'Le worker de la demande', texte: textes.arretEnCours },
  { debut: "L'espace de travail de la demande", texte: textes.repriseImpossible },
  { debut: DEMANDE, contient: [CREEE_SANS_WORKER], texte: textes.projetEnregistre },
  {
    debut: 'Les réponses de la demande',
    contient: ['bien enregistrées, mais'],
    texte: textes.reponsesEnregistrees,
  },
  {
    debut: "L'étape",
    contient: ['est bien approuvée', 'est bien refusée'],
    texte: textes.avisEnregistre,
  },
  { debut: 'Le message est vide', texte: textes.messageVide },
  { debut: 'La discussion est terminée.', texte: '' },
  { debut: "Aucune question n'attend ta confirmation", texte: '' },
  { debut: PROJET_INCONNU, texte: textes.projetDisparu },
];

const TEXTES_409: Readonly<Record<Action, string>> = {
  demande: textes.aJour,
  detail: textes.probleme,
  'lancement-essai': textes.siteNonDemarre,
  'fermeture-essai': textes.siteNonFerme,
  'ouverture-discussion': textes.discussionImpossible,
  discussion: textes.discussionImpossible,
  parametres: textes.probleme,
};

function lireDetail(corps: unknown): string | null {
  return typeof corps === 'object' &&
    corps !== null &&
    'detail' in corps &&
    typeof corps.detail === 'string'
    ? corps.detail
    : null;
}

function traduire(detail: string): string | null {
  const traduction = TRADUCTIONS.find(
    ({ debut, contient }) =>
      detail.startsWith(debut) && (!contient || contient.some((partie) => detail.includes(partie))),
  );
  return traduction ? traduction.texte : null;
}

function texteGenerique(action: Action, statut: number): string {
  const discussion = action === 'ouverture-discussion' || action === 'discussion';
  switch (statut) {
    case 0:
      return textes.sansReponse;
    case HttpStatusCode.BadRequest:
    case HttpStatusCode.PayloadTooLarge:
    case HttpStatusCode.UnsupportedMediaType:
      return textes.nonPrisEnCompte;
    case HttpStatusCode.Unauthorized:
      return '';
    case HttpStatusCode.Forbidden:
      return textes.pageRefusee;
    case HttpStatusCode.NotFound:
      if (action === 'parametres') {
        return textes.probleme;
      }
      return action === 'discussion' ? '' : textes.projetDisparu;
    case HttpStatusCode.Conflict:
      return TEXTES_409[action];
    default:
      return discussion ? textes.discussionImpossible : textes.probleme;
  }
}

export function demandeDisparue(action: Action, erreur: HttpErrorResponse): boolean {
  const detail = lireDetail(erreur.error) ?? '';
  switch (erreur.status) {
    case HttpStatusCode.NotFound:
      return action === 'demande' || action === 'detail' || action === 'ouverture-discussion';
    case HttpStatusCode.BadRequest:
      return (
        detail.startsWith(IDENTIFIANT_INVALIDE) ||
        (action === 'ouverture-discussion' && detail.startsWith(PROJET_INCONNU))
      );
    default:
      return false;
  }
}

export function demandeCreeeSansWorker(erreur: HttpErrorResponse): boolean {
  const detail = lireDetail(erreur.error) ?? '';
  return (
    erreur.status === HttpStatusCode.Conflict &&
    detail.startsWith(DEMANDE) &&
    detail.includes(CREEE_SANS_WORKER)
  );
}

export function texteErreur(action: Action, erreur: HttpErrorResponse): string {
  const detail = lireDetail(erreur.error);
  console.warn(action, erreur.status, detail ?? erreur.message);
  if (action === 'parametres') {
    const simple =
      STATUTS_DETAIL_PARAMETRES.includes(erreur.status) ||
      (erreur.status === HttpStatusCode.Conflict && detail === CLE_D_ABORD);
    return detail !== null && simple ? detail : texteGenerique(action, erreur.status);
  }
  if (demandeDisparue(action, erreur)) {
    return textes.projetDisparu;
  }
  return (detail === null ? null : traduire(detail)) ?? texteGenerique(action, erreur.status);
}
