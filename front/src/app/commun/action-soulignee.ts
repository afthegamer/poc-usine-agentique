import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';

@Component({
  selector: 'button[usine-action-soulignee], a[usine-action-soulignee]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.danger]': 'danger()' },
  template: `<ng-content />`,
  styles: `
    :host,
    :host(:hover) {
      display: inline-flex;
      align-items: center;
      box-sizing: border-box;
      min-height: 46px;
      padding: 0 2px;
      border: none;
      background: none;
      color: var(--texte);
      font: inherit;
      font-size: 16px;
      text-decoration: underline;
      text-underline-offset: 5px;
      cursor: pointer;
    }

    :host(.danger),
    :host(.danger:hover) {
      min-height: 44px;
      padding: 0 4px;
      color: var(--erreur);
      font-size: 15px;
    }
  `,
})
export class ActionSoulignee {
  readonly danger = input(false, { transform: booleanAttribute });
}
