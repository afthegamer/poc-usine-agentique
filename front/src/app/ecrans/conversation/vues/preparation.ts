import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DemandeDetailDto, lireEtapeAffichee } from '../../../api/types';
import { Message } from '../../../commun/message';
import { libelles } from '../../../textes/libelles';

@Component({
  selector: 'usine-preparation',
  imports: [Message],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-message auteur="usine" [moment]="moment()">
      <p class="neutre">{{ texte() }}</p>
    </usine-message>
  `,
  styles: `
    .neutre {
      color: var(--texte-neutre);
      font-style: italic;
    }
  `,
})
export class Preparation {
  readonly detail = input.required<DemandeDetailDto>();

  protected readonly moment = computed(() => new Date(this.detail().misAJourLe));
  protected readonly texte = computed(() =>
    lireEtapeAffichee(this.detail().etapeAffichee) === 'plan'
      ? libelles.conversation.preparationPlan
      : libelles.conversation.lectureDemande,
  );
}
