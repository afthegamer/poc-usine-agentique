import { ChangeDetectionStrategy, Component } from '@angular/core';
import { libelles } from '../../textes/libelles';

@Component({
  selector: 'usine-acces-refuse',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header>
      <p class="marque">{{ libelles.marque }}</p>
    </header>
    <main>
      <h1>{{ libelles.accesRefuse.titre }}</h1>
      <p class="consigne">{{ libelles.accesRefuse.consigne }}</p>
    </main>
  `,
  styles: `
    header {
      padding: 12px 28px;
      border-bottom-width: 1px;
      border-bottom-style: solid;
      border-bottom-color: var(--filet);
    }

    .marque {
      display: flex;
      align-items: center;
      min-height: 44px;
      font-size: 21px;
      font-weight: 700;
      letter-spacing: -0.3px;
    }

    main {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 22px;
      padding: 64px 24px;
      text-align: center;
    }

    h1,
    .consigne {
      max-width: 720px;
    }

    h1 {
      font-size: 38px;
      font-weight: 700;
      line-height: 1.2;
      letter-spacing: -0.6px;
    }

    .consigne {
      font-size: 15px;
      line-height: 1.55;
      color: var(--texte-secondaire);
    }
  `,
})
export class AccesRefuse {
  protected readonly libelles = libelles;
}
