import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DemandeDetailDto } from '../../../api/types';
import { Message } from '../../../commun/message';
import { libelles } from '../../../textes/libelles';

@Component({
  selector: 'usine-attente-externe',
  imports: [Message],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-message auteur="usine" [moment]="moment()">
      <p>{{ libelles.conversation.validationAilleurs }}</p>
    </usine-message>
  `,
})
export class AttenteExterne {
  readonly detail = input.required<DemandeDetailDto>();

  protected readonly libelles = libelles;
  protected readonly moment = computed(() => new Date(this.detail().misAJourLe));
}
