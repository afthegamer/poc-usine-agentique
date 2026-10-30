import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';

@Component({
  selector: 'button[usine-bouton-plein], a[usine-bouton-plein]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.danger]': 'danger()' },
  template: `<ng-content />`,
  styles: `
    :host,
    :host(:hover) {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      box-sizing: border-box;
      min-height: 46px;
      padding: 0 22px;
      border: none;
      border-radius: 6px;
      background-color: var(--accent);
      color: var(--blanc);
      font: inherit;
      font-size: 16px;
      font-weight: 600;
      text-decoration: none;
      cursor: pointer;
    }

    :host(.danger),
    :host(.danger:hover) {
      background-color: var(--erreur);
    }

    :host(:disabled) {
      cursor: default;
    }
  `,
})
export class BoutonPlein {
  readonly danger = input(false, { transform: booleanAttribute });
}
