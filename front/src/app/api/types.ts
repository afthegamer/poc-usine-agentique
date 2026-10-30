import type { components } from './schema';

type Schemas = components['schemas'];

export type DemandeDto = Schemas['DemandeDto'];
export type DemandeDetailDto = Schemas['DemandeDetailDto'];
export type ProjetDto = Schemas['ProjetDto'];
export type EssaiDto = Schemas['EssaiDto'];
export type LancementDto = Schemas['LancementDto'];
export type ReponsesDto = Schemas['ReponsesDto'];
export type ValidationDto = Schemas['ValidationDto'];
export type RemarqueDto = Schemas['RemarqueDto'];
export type LancementDiscussionDto = Schemas['LancementDiscussionDto'];
export type DiscussionDto = Schemas['DiscussionDto'];
export type MessageDto = Schemas['MessageDto'];
export type ConfirmationDto = Schemas['ConfirmationDto'];
export type StoryDto = Schemas['StoryDto'];
export type CritereDto = Schemas['CritereDto'];
export type BesoinDto = Schemas['BesoinDto'];
export type BacklogDto = Schemas['BacklogDto'];
export type LivraisonDto = Schemas['LivraisonDto'];
export type EvenementDto = Schemas['EvenementDto'];
export type ParametresDto = Schemas['ParametresDto'];
export type IaDto = Schemas['IaDto'];
export type ModeleDto = Schemas['ModeleDto'];
export type CleDto = Schemas['CleDto'];
export type ChoixDto = Schemas['ChoixDto'];
export type RolesDto = Schemas['RolesDto'];
export type OllamaDto = Schemas['OllamaDto'];

const ETAPES_AFFICHEES = ['comprendre', 'plan', 'fabrication', 'pret'] as const;
const ATTENTES = ['reponses', 'besoin', 'backlog', 'tests', 'livraison'] as const;
const SOUS_ETAPES_FABRICATION = [
  'preparation-verifications',
  'fabrication',
  'verifications',
] as const;
const STATUTS = [
  'EnCours',
  'AttenteReponses',
  'AttenteValidation',
  'Terminee',
  'Echouee',
  'Annulee',
] as const;
const IAS = ['gemini', 'anthropic', 'autre', 'ollama'] as const;
export const SERVICES = ['mistral', 'groq', 'openrouter', 'adresse'] as const;

export type EtapeAffichee = (typeof ETAPES_AFFICHEES)[number];
export type Attente = (typeof ATTENTES)[number];
export type SousEtapeFabrication = (typeof SOUS_ETAPES_FABRICATION)[number];
export type Statut = (typeof STATUTS)[number];
export type Ia = (typeof IAS)[number];
export type Service = (typeof SERVICES)[number];

function lireParmi<T extends string>(valeurs: readonly T[], valeur: string): T | null {
  return valeurs.find((connue) => connue === valeur) ?? null;
}

export const lireEtapeAffichee = (valeur: string): EtapeAffichee | null =>
  lireParmi(ETAPES_AFFICHEES, valeur);

export const lireAttente = (valeur: string): Attente | null => lireParmi(ATTENTES, valeur);

export const lireSousEtapeFabrication = (valeur: string): SousEtapeFabrication | null =>
  lireParmi(SOUS_ETAPES_FABRICATION, valeur);

export const lireStatut = (valeur: string): Statut | null => lireParmi(STATUTS, valeur);

export const lireIa = (valeur: string): Ia | null => lireParmi(IAS, valeur);

export const lireService = (valeur: string): Service | null => lireParmi(SERVICES, valeur);
