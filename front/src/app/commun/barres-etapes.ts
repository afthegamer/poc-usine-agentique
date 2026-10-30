import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
} from '@angular/core';
import { libelles } from '../textes/libelles';

type EtatEtape = 'faite' | 'en-cours' | 'atteinte' | 'a-venir';

@Component({
  selector: 'usine-barres-etapes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol [class.encadree]="encadree()">
      @for (etape of lignes(); track $index) {
        <li
          [class]="etape.etat"
          [attr.aria-current]="
            etape.etat === 'en-cours' || etape.etat === 'atteinte' ? 'step' : null
          "
        >
          <span class="barre" aria-hidden="true"></span>
          <span>{{ etape.libelle }}</span>
          @if (etape.etat === 'faite') {
            <span class="masque">{{ libelles.etapes.faite }}</span>
          }
        </li>
      }
    </ol>
  `,
  styles: `
    ol {
      display: flex;
      flex-direction: column;
      gap: 12px;
      padding: 0;
      list-style: none;
      font-size: 15px;
    }

    ol.encadree {
      padding: 16px 0;
      border-top-width: 1px;
      border-top-style: solid;
      border-top-color: var(--filet);
      border-bottom-width: 1px;
      border-bottom-style: solid;
      border-bottom-color: var(--filet);
      font-size: 16px;
      line-height: 1.5;
    }

    li {
      display: flex;
      gap: 12px;
      align-items: center;
    }

    .encadree li {
      align-items: flex-start;
    }

    .encadree .barre {
      margin-top: 2px;
    }

    .barre {
      flex: none;
      width: 4px;
      height: 20px;
      background-color: var(--accent);
    }

    .en-cours,
    .atteinte {
      font-weight: 700;
    }

    .en-cours .barre {
      background-color: var(--texte);
    }

    .a-venir {
      color: var(--texte-secondaire);
    }

    .a-venir .barre {
      background-color: var(--barre-a-venir);
    }
  `,
})
export class BarresEtapes {
  readonly etapes = input.required<readonly string[]>();
  readonly courante = input.required<number | null>();
  readonly derniereAtteinte = input(false, { transform: booleanAttribute });
  readonly sansEtapeCourante = input(false, { transform: booleanAttribute });
  readonly encadree = input(false, { transform: booleanAttribute });

  protected readonly libelles = libelles;
  protected readonly lignes = computed(() => {
    const courante = this.courante();
    const derniere = this.etapes().length - 1;
    return this.etapes().map((libelle, rang) => ({
      libelle,
      etat: this.etatEtape(rang, courante, derniere),
    }));
  });

  private etatEtape(rang: number, courante: number | null, derniere: number): EtatEtape {
    if (courante === null || rang > courante) {
      return 'a-venir';
    }
    if (rang < courante) {
      return 'faite';
    }
    if (this.sansEtapeCourante()) {
      return 'a-venir';
    }
    return rang === derniere && this.derniereAtteinte() ? 'atteinte' : 'en-cours';
  }
}
