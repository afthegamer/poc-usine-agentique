import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ColonneProjets } from './colonne-projets';

@Component({
  selector: 'usine-cadre',
  imports: [ColonneProjets],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-colonne-projets [courant]="courant()" />
    <div class="principal"><ng-content /></div>
  `,
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      min-height: 100vh;
    }

    .principal {
      flex: 999 1 520px;
      min-width: 0;
      display: flex;
      flex-direction: column;
    }

    @media (min-width: 760px) {
      :host {
        flex-wrap: nowrap;
      }

      usine-colonne-projets {
        position: sticky;
        top: 0;
        align-self: flex-start;
        height: 100vh;
        overflow-y: auto;
      }
    }
  `,
})
export class Cadre {
  readonly courant = input<string | null>(null);
}
