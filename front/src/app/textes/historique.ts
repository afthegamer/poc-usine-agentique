import { libelles } from './libelles';

export type NatureEvenement =
  | 'reponses'
  | 'besoin-approuve'
  | 'besoin-refuse'
  | 'plan-refuse'
  | 'plan-approuve'
  | 'reprise'
  | 'annulation';

export interface LigneDuDemandeur {
  readonly nature: NatureEvenement;
  readonly texte: string;
}

interface Regle {
  readonly debut: string;
  readonly nature: NatureEvenement;
  readonly texte: (remarques: readonly string[], ajout: boolean) => string;
}

const textes = libelles.fil;

const avecRemarques =
  (entete: string, retoucher: (ligne: string) => string) => (remarques: readonly string[]) =>
    [entete, ...remarques.map(retoucher)].join('\n');

const sansReferenceBesoin = (ligne: string) => ligne.replace(/^Besoin : /, '');

const remarqueDuPlan = (ligne: string) =>
  ligne
    .replace(/^Backlog : /, '')
    .replace(/^S(\d+) : /, (_, numero: string) => `${textes.fonction(numero)} `);

const REGLES: readonly Regle[] = [
  { debut: 'Réponses transmises au PO.', nature: 'reponses', texte: () => textes.reponsesDonnees },
  {
    debut: 'Étape « besoin » approuvée.',
    nature: 'besoin-approuve',
    texte: () => textes.besoinApprouve,
  },
  {
    debut: 'Étape « besoin » refusée : ',
    nature: 'besoin-refuse',
    texte: avecRemarques(textes.besoinRefuse, sansReferenceBesoin),
  },
  {
    debut: 'Étape « backlog » refusée : ',
    nature: 'plan-refuse',
    texte: avecRemarques(textes.planRefuse, remarqueDuPlan),
  },
  {
    debut: 'Étape « backlog » approuvée.',
    nature: 'plan-approuve',
    texte: () => textes.planApprouve,
  },
  { debut: "Reprise après l'échec : ", nature: 'reprise', texte: () => textes.reprise },
  {
    debut: 'Demande annulée.',
    nature: 'annulation',
    texte: (_, ajout) => textes.annulation(ajout),
  },
  {
    debut: 'Annulation demandée : ',
    nature: 'annulation',
    texte: (_, ajout) => textes.annulation(ajout),
  },
];

export function ligneDuDemandeur(message: string, ajout: boolean): LigneDuDemandeur | null {
  const regle = REGLES.find(({ debut }) => message.startsWith(debut));
  if (regle === undefined) {
    return null;
  }
  const remarques = message.slice(regle.debut.length).split('\n');
  return { nature: regle.nature, texte: regle.texte(remarques, ajout) };
}
