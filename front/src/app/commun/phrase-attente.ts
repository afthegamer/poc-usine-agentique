import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';

@Component({
  selector: 'usine-phrase-attente',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'status', '[class.attend]': 'attend()' },
  template: `<ng-content />`,
  styles: `
    :host {
      display: block;
      color: var(--texte-neutre);
      font-size: 15px;
      font-style: italic;
      line-height: 1.5;
    }

    :host(.attend) {
      color: var(--attente);
    }
  `,
})
export class PhraseAttente {
  readonly attend = input(false, { transform: booleanAttribute });
}
