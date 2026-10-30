import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'usine-separateur-jour',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="jour">{{ jour() }}</span>
    @if (recapitulatif()) {
      <span>{{ recapitulatif() }}</span>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 4px;
      background-image: linear-gradient(var(--filet), var(--filet));
      background-position: center 8px;
      background-size: 100% 1px;
      background-repeat: no-repeat;
      color: var(--texte-secondaire);
      font-size: 13px;
      line-height: 17px;
      text-align: center;
    }

    .jour {
      padding: 0 12px;
      background-color: var(--fond);
    }
  `,
})
export class SeparateurJour {
  readonly jour = input.required<string>();
  readonly recapitulatif = input('');
}
