import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { DemandeDetailDto } from '../../../api/types';
import { BoutonPlein } from '../../../commun/bouton-plein';
import { FocusApresAppel } from '../../../commun/focus-ecran';
import { Message } from '../../../commun/message';
import { libelles } from '../../../textes/libelles';

type Issue = 'reussi' | 'disponible' | 'echec' | 'nonPublie' | 'sansDemande';

function issueDeLaDemande(detail: DemandeDetailDto | null): Issue {
  if (detail === null) {
    return 'sansDemande';
  }
  const livraison = detail.livraison;
  if (livraison === null || livraison.succes) {
    return livraison?.publie ? 'reussi' : 'nonPublie';
  }
  return livraison.publie ? 'disponible' : 'echec';
}

@Component({
  selector: 'usine-pret',
  imports: [BoutonPlein, FocusApresAppel, Message, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-message auteur="usine" [moment]="moment()">
      <p>{{ texte() }}</p>
      @if (recommencable()) {
        <div class="actions">
          <button
            type="button"
            usine-bouton-plein
            [disabled]="occupee()"
            [attr.aria-busy]="occupee() || null"
            [usineFocusApresAppel]="occupee()"
            (click)="recommencer.emit()"
          >
            {{ libelles.pret.recommencer }}
          </button>
        </div>
      } @else if (ouvrir(); as adresse) {
        <div class="actions">
          <a usine-bouton-plein [routerLink]="adresse">{{ libelles.panneau.ouvrirSite }}</a>
        </div>
      }
      @if (remarques().length > 0) {
        <details>
          <summary>{{ libelles.pret.remarques(remarques().length) }}</summary>
          <ul>
            @for (remarque of remarques(); track $index) {
              <li>{{ remarque }}</li>
            }
          </ul>
        </details>
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

    summary {
      padding: 11px 0;
      color: var(--texte-secondaire);
      font-size: 15px;
      line-height: 22px;
      cursor: pointer;
    }

    ul {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 0;
      list-style: none;
      color: var(--texte-neutre);
      font-size: 15px;
      line-height: 1.55;
    }
  `,
})
export class Pret {
  readonly detail = input<DemandeDetailDto | null>(null);
  readonly ajout = input(false, { transform: booleanAttribute });
  readonly moment = input<Date | null>(null);
  readonly lien = input<string | null>(null);
  readonly occupee = input(false, { transform: booleanAttribute });
  readonly peutRecommencer = input(true, { transform: booleanAttribute });
  readonly recommencer = output();

  protected readonly libelles = libelles;
  private readonly issue = computed(() => issueDeLaDemande(this.detail()));
  protected readonly texte = computed(() => {
    const issue = this.issue();
    return issue === 'sansDemande' ? libelles.pret.sansDemande : libelles.pret[issue](this.ajout());
  });
  protected readonly recommencable = computed(
    () =>
      !this.ajout() &&
      this.peutRecommencer() &&
      (this.issue() === 'echec' || this.issue() === 'nonPublie'),
  );
  protected readonly ouvrir = computed(() => {
    const issue = this.issue();
    const siteEnLigne =
      issue === 'reussi' ||
      issue === 'disponible' ||
      issue === 'sansDemande' ||
      (issue === 'echec' && this.ajout());
    return siteEnLigne ? this.lien() : null;
  });
  protected readonly remarques = computed(() =>
    this.issue() === 'reussi' ? (this.detail()?.remarquesQa ?? []) : [],
  );
}
