import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import { DemandeDetailDto, ValidationDto } from '../../../api/types';
import { ActionSoulignee } from '../../../commun/action-soulignee';
import { BoutonPlein } from '../../../commun/bouton-plein';
import { FocusApresAppel } from '../../../commun/focus-ecran';
import { Message } from '../../../commun/message';
import { cleBrouillon } from '../../../etat/brouillons';
import { libelles } from '../../../textes/libelles';
import { Besoin } from '../besoin';

const REFERENCE_DU_BESOIN = 'Besoin';

@Component({
  selector: 'usine-compris',
  imports: [ActionSoulignee, Besoin, BoutonPlein, FocusApresAppel, Message],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-message auteur="usine" [moment]="moment()">
      <p>{{ libelles.compris.introduction(ajout()) }}</p>
      <usine-besoin [resume]="detail().besoin?.resume ?? ''" />
      <div class="actions">
        <button
          type="button"
          usine-bouton-plein
          [disabled]="occupee()"
          [attr.aria-busy]="occupee() || null"
          [usineFocusApresAppel]="occupee()"
          (click)="envoi.emit({ etape: 'besoin', approuver: true })"
        >
          {{ libelles.compris.oui }}
        </button>
        <button type="button" usine-action-soulignee (click)="corriger()">
          {{ libelles.compris.non }}
        </button>
      </div>
    </usine-message>
  `,
  styles: `
    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 22px;
      padding-top: 4px;
    }
  `,
})
export class Compris {
  readonly detail = input.required<DemandeDetailDto>();
  readonly ajout = input(false, { transform: booleanAttribute });
  readonly occupee = input(false, { transform: booleanAttribute });
  readonly envoi = output<ValidationDto>();
  readonly versLaSaisie = output();

  protected readonly libelles = libelles;
  protected readonly moment = computed(() => new Date(this.detail().misAJourLe));
  private readonly correction = linkedSignal({
    source: computed(() => cleBrouillon(this.detail(), 'besoin')),
    computation: () => false,
  });
  readonly saisieDeRemarque = this.correction.asReadonly();

  saisir(texte: string): void {
    this.envoi.emit({
      etape: 'besoin',
      approuver: false,
      remarques: [{ reference: REFERENCE_DU_BESOIN, texte }],
    });
  }

  protected corriger(): void {
    this.correction.set(true);
    this.versLaSaisie.emit();
  }
}
