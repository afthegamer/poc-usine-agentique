import { Signal, computed, linkedSignal } from '@angular/core';
import { Attente, DemandeDetailDto } from '../api/types';

export type Revue = Extract<Attente, 'reponses' | 'besoin' | 'backlog'>;

export interface Saisie {
  readonly texte: string;
  readonly heure: string;
}

export function estSaisies(valeur: unknown): valeur is readonly Saisie[] {
  return (
    Array.isArray(valeur) &&
    valeur.every(
      (saisie: unknown) =>
        typeof saisie === 'object' &&
        saisie !== null &&
        'texte' in saisie &&
        typeof saisie.texte === 'string' &&
        'heure' in saisie &&
        typeof saisie.heure === 'string',
    )
  );
}

const PREFIXE = 'usine.brouillon';

function sansErreur<T>(action: () => T, repli: T): T {
  try {
    return action();
  } catch {
    return repli;
  }
}

export function empreinte(texte: string): string {
  let condense = 0x811c9dc5;
  for (const octet of new TextEncoder().encode(texte)) {
    condense = Math.imul(condense ^ octet, 0x01000193) >>> 0;
  }
  return condense.toString(16).padStart(8, '0');
}

function contenuRevu(detail: DemandeDetailDto, revue: Revue): unknown {
  switch (revue) {
    case 'reponses':
      return detail.questions;
    case 'besoin':
      return detail.besoin;
    case 'backlog':
      return detail.backlog?.stories.map(({ id, titre }) => [id, titre]) ?? null;
  }
}

const debutDesCles = (id: string, revue: Revue) => `${PREFIXE}.${id}.${revue}.`;

export const cleBrouillon = (detail: DemandeDetailDto, revue: Revue) =>
  debutDesCles(detail.id, revue) + empreinte(JSON.stringify(contenuRevu(detail, revue)));

function clesDe(id: string, revue: Revue): string[] {
  const debut = debutDesCles(id, revue);
  return Array.from({ length: localStorage.length }, (_, rang) => localStorage.key(rang)).filter(
    (candidate): candidate is string => candidate?.startsWith(debut) ?? false,
  );
}

export function lireBrouillon<T>(
  detail: DemandeDetailDto,
  revue: Revue,
  garde: (valeur: unknown) => valeur is T,
): T | null {
  const attendue = cleBrouillon(detail, revue);
  return sansErreur(() => {
    for (const perimee of clesDe(detail.id, revue).filter((autre) => autre !== attendue)) {
      localStorage.removeItem(perimee);
    }
    const texte = localStorage.getItem(attendue);
    const valeur: unknown = texte === null ? null : JSON.parse(texte);
    return garde(valeur) ? valeur : null;
  }, null);
}

export function ecrireBrouillon(detail: DemandeDetailDto, revue: Revue, contenu: unknown): void {
  sansErreur(
    () => localStorage.setItem(cleBrouillon(detail, revue), JSON.stringify(contenu)),
    undefined,
  );
}

export function supprimerBrouillon(id: string, revue: Revue): void {
  sansErreur(
    () => clesDe(id, revue).forEach((perimee) => localStorage.removeItem(perimee)),
    undefined,
  );
}

export interface RevueUneParUne {
  readonly saisies: Signal<readonly Saisie[]>;
  garder(saisies: readonly Saisie[]): void;
  revenir(): string;
}

export function revueUneParUne(detail: () => DemandeDetailDto, revue: Revue): RevueUneParUne {
  const saisies = linkedSignal({
    source: computed(() => cleBrouillon(detail(), revue)),
    computation: () => lireBrouillon(detail(), revue, estSaisies) ?? [],
  });
  const garder = (suivantes: readonly Saisie[]) => {
    saisies.set(suivantes);
    ecrireBrouillon(detail(), revue, suivantes);
  };
  return {
    saisies: saisies.asReadonly(),
    garder,
    revenir: () => {
      const actuelles = saisies();
      garder(actuelles.slice(0, -1));
      return actuelles.at(-1)?.texte ?? '';
    },
  };
}
