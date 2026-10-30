import { DemandeDto, LivraisonDto, lireAttente, lireEtapeAffichee, lireStatut } from '../api/types';

export type Situation =
  | 'annulee'
  | 'annulation'
  | 'arret'
  | 'questions'
  | 'compris'
  | 'plan'
  | 'attente-externe'
  | 'preparation'
  | 'fabrication'
  | 'pret'
  | 'terminee'
  | 'inabouti'
  | 'inconnue';

const EN_COURS: ReadonlySet<Situation> = new Set<Situation>([
  'questions',
  'compris',
  'plan',
  'attente-externe',
  'preparation',
  'fabrication',
  'annulation',
  'arret',
]);

const ATTENDENT_L_UTILISATEUR: ReadonlySet<Situation> = new Set<Situation>([
  'questions',
  'compris',
  'plan',
  'attente-externe',
  'arret',
]);

export const estEnCours = (situation: Situation) => EN_COURS.has(situation);

export const attendLUtilisateur = (situation: Situation) => ATTENDENT_L_UTILISATEUR.has(situation);

export interface ContexteSituation {
  readonly annulationDemandee: boolean;
  readonly projetPublie: boolean;
  readonly estCreation: boolean;
  readonly livraison: LivraisonDto | null | undefined;
}

function situationTerminee(contexte: ContexteSituation): Situation {
  const { livraison } = contexte;
  if (livraison !== undefined) {
    return livraison?.succes && livraison.publie ? 'pret' : 'inabouti';
  }
  if (contexte.estCreation) {
    return contexte.projetPublie ? 'pret' : 'inabouti';
  }
  return 'terminee';
}

function situationConnue(demande: DemandeDto, contexte: ContexteSituation): Situation | null {
  const statut = lireStatut(demande.statut);
  if (statut === 'Annulee') {
    return 'annulee';
  }
  if (statut === 'EnCours' && contexte.annulationDemandee) {
    return 'annulation';
  }
  if (statut === 'Echouee') {
    return 'arret';
  }
  if (demande.attente !== null) {
    switch (lireAttente(demande.attente)) {
      case 'reponses':
        return 'questions';
      case 'besoin':
        return 'compris';
      case 'backlog':
        return 'plan';
      case 'tests':
      case 'livraison':
        return 'attente-externe';
      case null:
        return null;
    }
  }
  if (statut === 'EnCours') {
    switch (lireEtapeAffichee(demande.etapeAffichee)) {
      case 'comprendre':
      case 'plan':
        return 'preparation';
      case 'fabrication':
      case 'pret':
        return 'fabrication';
      case null:
        return null;
    }
  }
  return statut === 'Terminee' ? situationTerminee(contexte) : null;
}

export function situation(demande: DemandeDto, contexte: ContexteSituation): Situation {
  const connue = situationConnue(demande, contexte);
  if (connue === null) {
    console.warn('Situation inconnue', demande.statut, demande.etapeAffichee, demande.attente);
    return 'inconnue';
  }
  return connue;
}
