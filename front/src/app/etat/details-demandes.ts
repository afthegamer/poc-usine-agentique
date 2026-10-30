import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, Signal, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { ClientUsine } from '../api/client-usine';
import { DemandeDetailDto } from '../api/types';
import { EtatUsine } from './etat-usine';

export interface Lecture {
  readonly detail: DemandeDetailDto | null;
  readonly echec: HttpErrorResponse | null;
}

@Injectable({ providedIn: 'root' })
export class DetailsDemandes {
  private readonly client = inject(ClientUsine);
  private readonly etat = inject(EtatUsine);

  private readonly lectures = signal<ReadonlyMap<string, Lecture>>(new Map());
  private readonly suivis = new Map<string, number>();
  private readonly annonces = new Map<string, string | undefined>();
  private readonly enCours = new Map<string, Subscription>();
  private readonly suites = new Map<string, (relu: boolean) => void>();

  constructor() {
    effect(() => {
      const demandes = this.etat.demandes();
      untracked(() => {
        for (const id of this.suivis.keys()) {
          this.surAnnonce(id, demandes.get(id)?.misAJourLe);
        }
      });
    });
  }

  detail(id: string): Signal<DemandeDetailDto | null> {
    this.suivis.set(id, (this.suivis.get(id) ?? 0) + 1);
    const annonce = this.etat.demandes().get(id)?.misAJourLe;
    this.annonces.set(id, annonce);
    const lecture = this.lectures().get(id);
    const enLecture = this.enCours.get(id)?.closed === false;
    const perime =
      !lecture?.detail || lecture.echec || (annonce && annonce !== lecture.detail.misAJourLe);
    if (perime && !enLecture) {
      this.lire(id);
    }
    return computed(() => this.lectures().get(id)?.detail ?? null);
  }

  lecture(id: string): Lecture | undefined {
    return this.lectures().get(id);
  }

  oublier(id: string): void {
    const reste = (this.suivis.get(id) ?? 0) - 1;
    if (reste > 0) {
      this.suivis.set(id, reste);
    } else {
      this.suivis.delete(id);
      this.annonces.delete(id);
    }
  }

  relire(id: string, ensuite?: (relu: boolean) => void): void {
    if (ensuite !== undefined) {
      const avant = this.suites.get(id);
      this.suites.set(
        id,
        avant === undefined
          ? ensuite
          : (relu) => {
              avant(relu);
              ensuite(relu);
            },
      );
    }
    this.lire(id);
  }

  remplacer(detail: DemandeDetailDto): void {
    this.enCours.get(detail.id)?.unsubscribe();
    this.ecrire(detail.id, { detail, echec: null });
  }

  recevoirCreation(detail: DemandeDetailDto): void {
    this.remplacer(detail);
    const { id, titre, projet, statut, etape, etapeAffichee, attente, misAJourLe } = detail;
    this.etat.recevoir({ id, titre, projet, statut, etape, etapeAffichee, attente, misAJourLe });
  }

  private surAnnonce(id: string, annonce: string | undefined): void {
    if (annonce === undefined || annonce === this.annonces.get(id)) {
      return;
    }
    this.annonces.set(id, annonce);
    const lecture = this.lectures().get(id);
    if (!lecture?.echec && lecture?.detail?.misAJourLe !== annonce) {
      this.lire(id);
    }
  }

  private lire(id: string): void {
    this.enCours.get(id)?.unsubscribe();
    this.enCours.set(
      id,
      this.client.detail(id).subscribe({
        next: (detail) => this.ecrire(id, { detail, echec: null }),
        error: (erreur: unknown) =>
          this.ecrire(id, {
            detail: this.lectures().get(id)?.detail ?? null,
            echec:
              erreur instanceof HttpErrorResponse
                ? erreur
                : new HttpErrorResponse({ error: erreur }),
          }),
      }),
    );
  }

  private ecrire(id: string, lecture: Lecture): void {
    this.lectures.update((lectures) => new Map(lectures).set(id, lecture));
    const suite = this.suites.get(id);
    this.suites.delete(id);
    suite?.(lecture.echec === null);
  }
}

export function suivreDetails(ids: () => readonly string[]): void {
  const details = inject(DetailsDemandes);
  const suivis = computed(ids, {
    equal: (avant, apres) =>
      avant.length === apres.length && avant.every((id, rang) => id === apres[rang]),
  });
  effect((nettoyer) => {
    const liste = suivis();
    untracked(() => liste.forEach((id) => details.detail(id)));
    nettoyer(() => liste.forEach((id) => details.oublier(id)));
  });
}
