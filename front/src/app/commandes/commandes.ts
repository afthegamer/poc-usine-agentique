import { Situation } from '../etat/situation';
import { libelles } from '../textes/libelles';

const FORME_COMMANDE = /^\/[\p{L}-]*$/u;

export type NomCommande = '/nouveau' | '/ouvrir' | '/annuler' | '/reprendre';

export interface Commande {
  readonly nom: NomCommande;
  readonly description: string;
}

export interface ContexteCommandes {
  readonly ecran: 'accueil' | 'applications' | 'application' | 'conversation';
  readonly projetPublie: boolean;
  readonly situation: Situation | null;
  readonly ajout: boolean;
}

const ANNULABLES: ReadonlySet<Situation> = new Set<Situation>([
  'questions',
  'compris',
  'plan',
  'attente-externe',
  'preparation',
  'fabrication',
]);

export function estFormeCommande(texte: string): boolean {
  return FORME_COMMANDE.test(texte.trim());
}

export function commandesPossibles(contexte: ContexteCommandes): readonly Commande[] {
  if (contexte.ecran !== 'conversation') {
    return [];
  }
  const { descriptions } = libelles.commandes;
  const commandes: Commande[] = [{ nom: '/nouveau', description: descriptions.nouveau }];
  if (contexte.projetPublie) {
    commandes.push({ nom: '/ouvrir', description: descriptions.ouvrir });
  }
  if (contexte.situation !== null && ANNULABLES.has(contexte.situation)) {
    commandes.push({ nom: '/annuler', description: libelles.panneau.annuler(contexte.ajout) });
  }
  if (contexte.situation === 'arret') {
    commandes.push({ nom: '/reprendre', description: descriptions.reprendre });
  }
  return commandes;
}

export function commandeTapee(
  texte: string,
  commandes: readonly Commande[],
): NomCommande | undefined {
  const tapee = texte.trim().toLowerCase();
  return commandes.find(({ nom }) => nom === tapee)?.nom;
}
