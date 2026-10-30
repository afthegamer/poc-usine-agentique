import { DOCUMENT, DestroyRef, Injectable, InjectionToken, effect, inject } from '@angular/core';
import { Subscription } from 'rxjs';
import { Acces } from '../api/acces';
import { ClientUsine } from '../api/client-usine';
import { DemandeDto } from '../api/types';
import { EtatUsine, delaiReconnexion } from './etat-usine';

export interface SourceDeFlux {
  readonly readyState: number;
  readonly CLOSED: number;
  addEventListener(type: string, ecouteur: (evenement: MessageEvent<unknown>) => void): void;
  close(): void;
}

export const FABRIQUE_EVENT_SOURCE = new InjectionToken<(adresse: string) => SourceDeFlux>(
  'FABRIQUE_EVENT_SOURCE',
  { providedIn: 'root', factory: () => (adresse) => new EventSource(adresse) },
);

const INTERVALLE_SONDAGE = 30000;

const estTexte = (valeur: unknown) => typeof valeur === 'string';
const estTexteOuNul = (valeur: unknown) => valeur === null || typeof valeur === 'string';

export function estDemandeDto(valeur: unknown): valeur is DemandeDto {
  if (typeof valeur !== 'object' || valeur === null) {
    return false;
  }
  const champs: Partial<Record<keyof DemandeDto, unknown>> = valeur;
  return (
    estTexte(champs.id) &&
    estTexte(champs.titre) &&
    estTexteOuNul(champs.projet) &&
    estTexte(champs.statut) &&
    estTexte(champs.etape) &&
    estTexte(champs.etapeAffichee) &&
    estTexteOuNul(champs.attente) &&
    estTexte(champs.misAJourLe)
  );
}

function lireJson(donnees: unknown): unknown {
  if (typeof donnees !== 'string') {
    return donnees;
  }
  try {
    return JSON.parse(donnees);
  } catch {
    return donnees;
  }
}

@Injectable({ providedIn: 'root' })
export class FluxDemandes {
  private readonly etat = inject(EtatUsine);
  private readonly client = inject(ClientUsine);
  private readonly acces = inject(Acces);
  private readonly fabrique = inject(FABRIQUE_EVENT_SOURCE);
  private readonly document = inject(DOCUMENT);

  private source: SourceDeFlux | null = null;
  private minuterie: ReturnType<typeof setTimeout> | undefined;
  private sondage: ReturnType<typeof setInterval> | undefined;
  private lectureSondee: Subscription | undefined;
  private essai = 0;
  private demarre = false;
  private relireALOuverture = false;

  constructor() {
    effect(() => {
      if (this.acces.refuse()) {
        this.arreter();
      }
    });
    inject(DestroyRef).onDestroy(() => this.arreter());
  }

  demarrer(): void {
    if (this.demarre) {
      return;
    }
    this.demarre = true;
    this.document.addEventListener('visibilitychange', this.surVisibilite);
    this.surVisibilite();
  }

  private readonly surVisibilite = (): void => {
    if (this.document.visibilityState === 'hidden') {
      this.masquer();
    } else {
      this.ouvrir();
    }
  };

  private ouvrir(): void {
    this.suspendre();
    const source = this.fabrique(this.acces.adresseFlux('/api/flux'));
    this.source = source;
    source.addEventListener('open', () => {
      this.essai = 0;
      this.etat.definirConnexion('connecte');
      if (this.relireALOuverture) {
        this.etat.relireProjets();
      }
      this.relireALOuverture = true;
    });
    source.addEventListener('demande', (evenement: MessageEvent<unknown>) =>
      this.traiter(lireJson(evenement.data)),
    );
    source.addEventListener('error', () => this.surErreur(source));
  }

  private masquer(): void {
    this.suspendre();
    this.relireALOuverture = true;
    this.sondage = setInterval(() => this.sonder(), INTERVALLE_SONDAGE);
  }

  private surErreur(source: SourceDeFlux): void {
    if (source !== this.source) {
      return;
    }
    this.etat.definirConnexion('reconnexion');
    this.relireALOuverture = true;
    if (source.readyState !== source.CLOSED) {
      return;
    }
    this.suspendre();
    this.client.sante().subscribe({
      next: () => this.planifier(),
      error: () => this.planifier(),
    });
  }

  private planifier(): void {
    if (this.acces.refuse() || this.document.visibilityState === 'hidden' || this.source !== null) {
      return;
    }
    this.minuterie = setTimeout(() => this.ouvrir(), delaiReconnexion(this.essai));
    this.essai++;
  }

  private sonder(): void {
    this.lectureSondee?.unsubscribe();
    this.lectureSondee = this.client.demandes().subscribe({
      next: (demandes) => [...demandes].reverse().forEach((demande) => this.traiter(demande)),
      error: () => undefined,
    });
  }

  private traiter(valeur: unknown): void {
    if (estDemandeDto(valeur)) {
      this.etat.recevoir(valeur);
    } else {
      console.warn('Événement du flux ignoré', valeur);
    }
  }

  private suspendre(): void {
    clearTimeout(this.minuterie);
    clearInterval(this.sondage);
    this.lectureSondee?.unsubscribe();
    this.source?.close();
    this.source = null;
  }

  private arreter(): void {
    this.document.removeEventListener('visibilitychange', this.surVisibilite);
    this.suspendre();
  }
}
