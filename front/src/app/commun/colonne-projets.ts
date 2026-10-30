import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { EtatUsine } from '../etat/etat-usine';
import { attendLUtilisateur } from '../etat/situation';
import { libelles } from '../textes/libelles';
import { VERS_LA_SAISIE } from './focus-ecran';

@Component({
  selector: 'usine-colonne-projets',
  imports: [NgTemplateOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav [attr.aria-label]="libelles.colonne.navigation">
      <a routerLink="/" class="marque">{{ libelles.marque }}</a>
      <a
        routerLink="/"
        [state]="versLaSaisie"
        class="nouveau"
        routerLinkActive="actif"
        ariaCurrentWhenActive="page"
        [routerLinkActiveOptions]="{ exact: true }"
        >{{ libelles.colonne.nouveauProjet }}</a
      >
      <a routerLink="/applications" routerLinkActive="actif" ariaCurrentWhenActive="page">{{
        libelles.titres.applications
      }}</a>
      @if (replie()) {
        <details>
          <summary>{{ libelles.colonne.titreReplie(lignes().length) }}</summary>
          <ng-container [ngTemplateOutlet]="liste" />
        </details>
      } @else {
        <div class="projets">
          <span class="intitule">{{ libelles.colonne.titre }}</span>
          <ng-container [ngTemplateOutlet]="liste" />
        </div>
      }
      <a
        routerLink="/parametres"
        class="parametres"
        routerLinkActive="actif"
        ariaCurrentWhenActive="page"
        >{{ libelles.colonne.parametres }}</a
      >
    </nav>

    <ng-template #liste>
      @for (ligne of lignes(); track ligne.cle) {
        <a
          class="projet"
          [routerLink]="ligne.lien"
          [class.courant]="ligne.cle === courant()"
          [attr.aria-current]="ligne.cle === courant() ? 'page' : null"
        >
          <span class="titre" [title]="ligne.titre">{{ ligne.titre }}</span>
          @if (ligne.mot) {
            <span
              class="etat"
              [class.attend]="ligne.attend"
              [attr.aria-label]="ligne.description"
              >{{ ligne.mot }}</span
            >
          }
        </a>
      }
      @if (etat.charge() && lignes().length === 0) {
        <p class="vide">{{ libelles.colonne.aucunProjet }}</p>
      }
    </ng-template>
  `,
  styles: `
    :host {
      flex: 1 1 240px;
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      padding: 20px 14px;
      border-right-width: 1px;
      border-right-style: solid;
      border-right-color: var(--filet);
    }

    nav {
      flex: 1 1 auto;
      min-height: 0;
      display: flex;
      flex-direction: column;
      gap: 18px;
    }

    a,
    a:hover {
      display: flex;
      align-items: center;
      box-sizing: border-box;
      min-height: 44px;
      padding: 0 6px;
      color: var(--texte);
      font-size: 15px;
      text-decoration: none;
    }

    .marque,
    .marque:hover {
      font-size: 21px;
      font-weight: 700;
      letter-spacing: -0.3px;
    }

    .nouveau,
    .nouveau:hover {
      color: var(--accent);
      font-weight: 600;
    }

    .actif,
    .actif:hover {
      font-weight: 600;
      text-decoration-line: underline;
      text-decoration-color: var(--accent);
      text-decoration-thickness: 2px;
      text-underline-offset: 6px;
    }

    .parametres {
      margin-top: auto;
    }

    .projets {
      display: flex;
      flex-direction: column;
      gap: 2px;
      margin: -4px;
      padding: 4px;
      overflow-y: auto;
    }

    .intitule {
      padding: 0 6px 6px;
      color: var(--texte-secondaire);
      font-size: 13px;
    }

    summary {
      padding: 0 6px;
      color: var(--texte-secondaire);
      font-size: 13px;
      line-height: 44px;
      cursor: pointer;
    }

    .projet,
    .projet:hover {
      flex: none;
      justify-content: space-between;
      gap: 8px;
      padding: 12px 6px;
      line-height: 20px;
    }

    .courant,
    .courant:hover {
      font-weight: 600;
    }

    .titre {
      display: -webkit-box;
      min-width: 0;
      overflow: clip;
      overflow-wrap: anywhere;
      -webkit-box-orient: vertical;
      -webkit-line-clamp: 2;
      line-clamp: 2;
    }

    .courant .titre {
      overflow-clip-margin: 4px;
      text-decoration-line: underline;
      text-decoration-color: var(--accent);
      text-decoration-thickness: 2px;
      text-underline-offset: 6px;
    }

    .etat {
      flex: none;
      color: var(--texte-secondaire);
      font-size: 13px;
      font-weight: 400;
    }

    .etat.attend {
      color: var(--attente);
    }

    .vide {
      padding: 12px 6px;
      color: var(--texte-secondaire);
      font-size: 15px;
      line-height: 20px;
    }
  `,
})
export class ColonneProjets {
  readonly courant = input<string | null>(null);

  protected readonly etat = inject(EtatUsine);
  protected readonly libelles = libelles;
  protected readonly versLaSaisie = VERS_LA_SAISIE;
  protected readonly lignes = computed(() =>
    this.etat.projetsAffiches().map(({ cle, titre, lien, situation }) => {
      const { mot, description } = libelles.situations[situation];
      return { cle, titre, lien, mot, description, attend: attendLUtilisateur(situation) };
    }),
  );
  private readonly ecranEtroit = matchMedia('(max-width: 759.98px)');
  protected readonly replie = signal(this.ecranEtroit.matches);

  constructor() {
    const suivre = (changement: MediaQueryListEvent) => this.replie.set(changement.matches);
    this.ecranEtroit.addEventListener('change', suivre);
    inject(DestroyRef).onDestroy(() => this.ecranEtroit.removeEventListener('change', suivre));
  }
}
