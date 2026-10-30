import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  effect,
  input,
  output,
} from '@angular/core';
import { DemandeDetailDto, lireEtapeAffichee } from '../../../api/types';
import { ActionSoulignee } from '../../../commun/action-soulignee';
import { BoutonPlein } from '../../../commun/bouton-plein';
import { FocusApresAppel } from '../../../commun/focus-ecran';
import { Message } from '../../../commun/message';
import { libelles } from '../../../textes/libelles';

@Component({
  selector: 'usine-arret',
  imports: [ActionSoulignee, BoutonPlein, FocusApresAppel, Message],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-message auteur="usine" [moment]="moment()">
      <p>{{ texte() }}</p>
      <div class="actions">
        <button
          type="button"
          usine-bouton-plein
          [disabled]="occupee()"
          [attr.aria-busy]="occupee() || null"
          [usineFocusApresAppel]="occupee()"
          (click)="reprendre.emit()"
        >
          {{ libelles.arret.reprendre }}
        </button>
        <button
          type="button"
          usine-action-soulignee
          danger
          [disabled]="annulation()"
          [attr.aria-busy]="annulation() || null"
          [usineFocusApresAppel]="annulation()"
          (click)="annuler.emit()"
        >
          {{ libelles.panneau.annuler(ajout()) }}
        </button>
      </div>
      @if (erreur()) {
        <p class="alerte" role="alert">{{ erreur() }}</p>
      }
    </usine-message>
  `,
  styles: `
    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 22px;
    }

    .alerte {
      color: var(--erreur);
      font-size: 15px;
      line-height: 1.55;
    }
  `,
})
export class Arret {
  readonly detail = input.required<DemandeDetailDto>();
  readonly ajout = input(false, { transform: booleanAttribute });
  readonly occupee = input(false, { transform: booleanAttribute });
  readonly annulation = input(false, { transform: booleanAttribute });
  readonly erreur = input('');
  readonly reprendre = output();
  readonly annuler = output();

  protected readonly libelles = libelles;
  protected readonly moment = computed(() => new Date(this.detail().misAJourLe));
  protected readonly texte = computed(() => {
    const etape = lireEtapeAffichee(this.detail().etapeAffichee);
    return libelles.arret.message(etape === null ? null : libelles.arret.phases[etape]);
  });

  constructor() {
    effect(() => {
      const erreur = this.detail().erreur;
      if (erreur !== null) {
        console.warn('Arrêt de la demande', erreur);
      }
    });
  }
}
