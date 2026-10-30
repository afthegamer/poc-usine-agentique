import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
} from '@angular/core';
import { texteErreur } from '../../api/erreurs';
import { DemandeDto } from '../../api/types';
import { DetailsDemandes, suivreDetails } from '../../etat/details-demandes';
import { HORLOGE } from '../../etat/horloge';
import { dateCourte } from '../../textes/dates';
import { libelles } from '../../textes/libelles';
import { Fil, elementsDuFil } from './fil';

let numero = 0;

@Component({
  selector: 'usine-revoir',
  imports: [Fil],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (ligne of lignes(); track ligne.id) {
      <div class="ligne">
        <span class="titre">
          <svg
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2.4"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path d="m5 12 5 5L20 7" />
          </svg>
          {{ ligne.texte }}
        </span>
        <button
          type="button"
          [attr.aria-expanded]="ligne.ouverte"
          [attr.aria-controls]="ligne.ouverte ? ligne.cible : null"
          (click)="basculer(ligne.id)"
        >
          {{ libelles.revoir.bouton }}
        </button>
      </div>
      @if (ligne.ouverte) {
        <div class="fige" [id]="ligne.cible">
          @if (ligne.echec) {
            <p class="alerte" role="alert">{{ ligne.echec }}</p>
          } @else {
            <usine-fil [elements]="ligne.elements" />
          }
        </div>
      }
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      padding: 4px 0;
      border-top-width: 1px;
      border-top-style: solid;
      border-top-color: var(--filet);
      border-bottom-width: 1px;
      border-bottom-style: solid;
      border-bottom-color: var(--filet);
    }

    .ligne {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
    }

    .titre {
      display: flex;
      align-items: center;
      gap: 10px;
      min-width: 0;
      color: var(--texte-neutre);
      font-size: 15px;
      overflow-wrap: anywhere;
    }

    svg {
      flex: none;
      color: var(--accent);
    }

    .ligne button {
      min-height: 44px;
      padding: 0 2px;
      border: none;
      background: none;
      color: var(--accent);
      font: inherit;
      font-size: 15px;
      font-weight: 600;
      text-decoration: underline;
      text-underline-offset: 5px;
      cursor: pointer;
    }

    .ligne button:hover {
      color: var(--accent-survol);
    }

    .fige {
      display: flex;
      flex-direction: column;
      gap: 24px;
      padding: 16px 0 20px;
    }

    .alerte {
      color: var(--erreur);
      font-size: 15px;
      line-height: 1.55;
    }
  `,
})
export class Revoir {
  readonly demandes = input.required<readonly DemandeDto[]>();
  readonly courante = input.required<string>();

  private readonly details = inject(DetailsDemandes);
  private readonly horloge = inject(HORLOGE);
  private readonly prefixe = `revoir-${++numero}`;

  protected readonly libelles = libelles;
  private readonly ouvertes = linkedSignal<string, ReadonlySet<string>>({
    source: this.courante,
    computation: () => new Set(),
  });
  protected readonly lignes = computed(() => {
    const maintenant = this.horloge();
    return this.demandes().map(({ id, titre, misAJourLe }, rang) => {
      const ouverte = this.ouvertes().has(id);
      const lecture = ouverte ? this.details.lecture(id) : undefined;
      const detail = lecture?.detail;
      return {
        id,
        texte: libelles.revoir.ligne(titre, dateCourte(new Date(misAJourLe), maintenant)),
        ouverte,
        cible: `${this.prefixe}-${rang}`,
        echec: lecture?.echec ? texteErreur('detail', lecture.echec) : '',
        elements: detail
          ? elementsDuFil(detail, { ajout: rang > 0, fige: true, vue: null, maintenant })
          : [],
      };
    });
  });

  constructor() {
    suivreDetails(() => [...this.ouvertes()]);
  }

  protected basculer(id: string): void {
    this.ouvertes.update((ouvertes) => {
      const suivantes = new Set(ouvertes);
      if (!suivantes.delete(id)) {
        suivantes.add(id);
      }
      return suivantes;
    });
  }
}
