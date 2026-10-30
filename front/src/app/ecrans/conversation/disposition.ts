import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Cadre } from '../../commun/cadre';

@Component({
  selector: 'usine-disposition',
  imports: [Cadre],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-cadre [courant]="courant()">
      <div class="zone">
        <main>
          <ng-content select="h1" />
          <div class="fil">
            <div class="contenu" role="log" aria-live="polite"><ng-content /></div>
          </div>
          <div class="saisie"><ng-content select="usine-zone-saisie" /></div>
        </main>
        <div class="panneau"><ng-content select="usine-panneau-projet" /></div>
      </div>
    </usine-cadre>
    <ng-content select="usine-dialogue-confirmation" />
  `,
  styles: `
    .zone {
      flex: 1 1 auto;
      display: flex;
      flex-wrap: wrap;
    }

    main {
      flex: 999 1 520px;
      min-width: 0;
      display: flex;
      flex-direction: column;
    }

    .fil {
      flex: 1 1 auto;
      display: flex;
      justify-content: center;
      padding: 32px 24px;
    }

    .contenu {
      width: 100%;
      max-width: 720px;
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .saisie {
      position: sticky;
      bottom: 0;
      display: flex;
      justify-content: center;
      padding: 0 24px 24px;
      background-color: var(--fond);
    }

    .panneau {
      flex: 1 1 300px;
      box-sizing: border-box;
      padding: 28px 22px;
      border-left-width: 1px;
      border-left-style: solid;
      border-left-color: var(--filet);
    }

    @media (min-width: 1060px) {
      .zone {
        flex-wrap: nowrap;
      }

      .panneau {
        position: sticky;
        top: 0;
        align-self: flex-start;
        height: 100vh;
        overflow-y: auto;
      }
    }
  `,
})
export class Disposition {
  readonly courant = input<string | null>(null);
}
