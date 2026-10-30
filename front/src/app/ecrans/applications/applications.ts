import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Cadre } from '../../commun/cadre';
import { CibleFocusEcran } from '../../commun/focus-ecran';
import { Descriptions } from '../../etat/descriptions';
import { EtatUsine } from '../../etat/etat-usine';
import { MessageDePassage } from '../../etat/message-de-passage';
import { libelles } from '../../textes/libelles';

@Component({
  selector: 'usine-applications',
  imports: [Cadre, CibleFocusEcran, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-cadre>
      <main>
        <div class="page">
          <div class="entete">
            <h1 usineCibleFocus="titre">{{ libelles.titres.applications }}</h1>
            @if (passage()) {
              <p class="alerte" role="alert">{{ passage() }}</p>
            }
            <p>{{ libelles.applications.phrase }}</p>
          </div>
          @if (cartes().length > 0) {
            <div class="grille">
              @for (carte of cartes(); track carte.nom) {
                <a [routerLink]="['/applications', carte.nom]">
                  <div class="vignette" aria-hidden="true">
                    <svg
                      width="56"
                      height="56"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      stroke-width="1.5"
                      stroke-linecap="round"
                      stroke-linejoin="round"
                    >
                      <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" />
                      <path d="M14 3v5h5" />
                      <path d="M9 13h6" />
                      <path d="M9 17h4" />
                    </svg>
                  </div>
                  <div class="texte">
                    <span class="titre">{{ carte.titre }}</span>
                    @if (carte.description) {
                      <span class="description">{{ carte.description }}</span>
                    }
                  </div>
                </a>
              }
            </div>
          } @else if (etat.charge()) {
            <p>{{ libelles.applications.aucune }}</p>
          }
          <p class="pied">
            {{ libelles.applications.pied.avantLeLien
            }}<a routerLink="/">{{ libelles.applications.pied.lien }}</a
            >{{ libelles.applications.pied.apresLeLien }}
          </p>
        </div>
      </main>
    </usine-cadre>
  `,
  styles: `
    main {
      display: flex;
      justify-content: center;
      padding: 48px 24px 64px;
    }

    .page {
      width: 100%;
      max-width: 980px;
      display: flex;
      flex-direction: column;
      gap: 32px;
    }

    .entete {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    h1 {
      font-size: 34px;
      font-weight: 700;
      line-height: 1.2;
      letter-spacing: -0.5px;
    }

    p {
      color: var(--texte-secondaire);
      font-size: 16px;
      line-height: 1.55;
    }

    .alerte {
      color: var(--erreur);
    }

    .pied {
      font-size: 15px;
    }

    .grille {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
      gap: 32px 24px;
    }

    .grille a,
    .grille a:hover {
      display: flex;
      flex-direction: column;
      padding-top: 14px;
      border-top-width: 2px;
      border-top-style: solid;
      border-top-color: var(--texte);
      color: var(--texte);
      text-decoration: none;
    }

    .vignette {
      display: flex;
      align-items: center;
      justify-content: center;
      height: 150px;
      background-color: var(--bloc);
      color: var(--texte-neutre);
    }

    .texte {
      display: flex;
      flex-direction: column;
      gap: 6px;
      padding-top: 14px;
    }

    .titre {
      font-size: 18px;
      font-weight: 600;
    }

    .description {
      display: -webkit-box;
      overflow: hidden;
      -webkit-box-orient: vertical;
      -webkit-line-clamp: 2;
      line-clamp: 2;
      color: var(--texte-secondaire);
      font-size: 15px;
      line-height: 1.5;
    }
  `,
})
export class Applications {
  protected readonly etat = inject(EtatUsine);
  private readonly descriptions = inject(Descriptions);

  protected readonly libelles = libelles;
  protected readonly passage = signal(inject(MessageDePassage).prendre()?.texte ?? '');
  private readonly projets = computed(() => {
    if (!this.etat.charge()) {
      return [];
    }
    const elements = this.etat.projetsAffiches();
    return [...this.etat.projets()]
      .sort(
        (premier, second) =>
          Date.parse(second.derniereMiseAJour) - Date.parse(premier.derniereMiseAJour),
      )
      .map(({ nom, titre }) => ({
        nom,
        titre,
        element: elements.find(({ cle }) => cle === `projet:${nom}`),
      }));
  });
  protected readonly cartes = computed(() =>
    this.projets().map(({ nom, titre, element }) => ({
      nom,
      titre,
      description: this.descriptions.description(element),
    })),
  );

  constructor() {
    effect(() => {
      const elements = this.projets().flatMap(({ element }) => element ?? []);
      untracked(() => this.descriptions.charger(elements));
    });
  }
}
