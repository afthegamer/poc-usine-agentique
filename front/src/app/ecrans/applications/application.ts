import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  DestroyRef,
  ElementRef,
  Injector,
  Renderer2,
  afterNextRender,
  afterRenderEffect,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  EMPTY,
  Observable,
  Subscription,
  catchError,
  exhaustMap,
  filter,
  from,
  map,
  merge,
  take,
  timer,
} from 'rxjs';
import { ClientUsine } from '../../api/client-usine';
import { Action, texteErreur } from '../../api/erreurs';
import { EssaiDto } from '../../api/types';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { BoutonPlein } from '../../commun/bouton-plein';
import { Cadre } from '../../commun/cadre';
import { CibleFocusEcran, FocusApresAppel } from '../../commun/focus-ecran';
import { PhraseAttente } from '../../commun/phrase-attente';
import { EtatUsine } from '../../etat/etat-usine';
import { MessageDePassage } from '../../etat/message-de-passage';
import { libelles } from '../../textes/libelles';

type EtatEssai =
  | { readonly etape: 'lecture' }
  | { readonly etape: 'demarrage' }
  | { readonly etape: 'pret'; readonly adresse: string }
  | { readonly etape: 'echec'; readonly texte: string; readonly modifier: boolean };

const ADRESSE_ESSAI = /^http:\/\/localhost:\d{1,5}$/;
const INTERVALLE_SONDE = 2000;
const INTERVALLE_ESSAI = 5000;
const ATTENTE_MAXIMALE = 10 * 60 * 1000;

const echec = (texte: string, modifier = false): EtatEssai => ({ etape: 'echec', texte, modifier });

const repond = (adresse: string) =>
  from(
    fetch(adresse, { mode: 'no-cors', cache: 'no-store' }).then(
      () => true,
      () => false,
    ),
  );

@Component({
  selector: 'usine-application',
  imports: [
    ActionSoulignee,
    BoutonPlein,
    Cadre,
    CibleFocusEcran,
    FocusApresAppel,
    PhraseAttente,
    RouterLink,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-cadre>
      <header>
        <a routerLink="/applications" class="retour">
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path d="m15 18-6-6 6-6" />
          </svg>
          {{ libelles.titres.applications }}
        </a>
        <h1 #titrePrincipal usineCibleFocus="titre">{{ titre() }}</h1>
        <div class="actions">
          <a class="modifier" [routerLink]="['/projets', nom()]">{{
            libelles.application.modifier
          }}</a>
          @if (essai().etape === 'pret') {
            <button
              type="button"
              usine-action-soulignee
              [disabled]="fermeture()"
              (click)="relancer()"
            >
              {{ libelles.application.relancer }}
            </button>
            <button
              type="button"
              usine-action-soulignee
              [disabled]="fermeture()"
              [attr.aria-busy]="fermeture() || null"
              [usineFocusApresAppel]="fermeture()"
              (click)="fermer()"
            >
              {{ libelles.application.fermer }}
            </button>
          }
        </div>
        @if (erreurFermeture()) {
          <p class="alerte" role="alert">{{ erreurFermeture() }}</p>
        }
      </header>
      <main [attr.aria-label]="libelles.titres.application">
        @let etat = essai();
        @if (etat.etape === 'demarrage') {
          <div class="message">
            <usine-phrase-attente attend>{{ libelles.application.demarrage }}</usine-phrase-attente>
          </div>
        } @else if (etat.etape === 'pret') {
          <div class="onglet">
            <a usine-action-soulignee [href]="etat.adresse" target="_blank" rel="noopener">{{
              libelles.application.nouvelOnglet
            }}</a>
          </div>
          <iframe
            #site
            [title]="libelles.titres.application"
            sandbox="allow-scripts allow-forms allow-same-origin allow-popups allow-downloads allow-modals"
          ></iframe>
        } @else if (etat.etape === 'echec') {
          <div class="message">
            <p class="alerte" role="alert">{{ etat.texte }}</p>
            <div class="actions">
              <button type="button" usine-bouton-plein (click)="reessayer()">
                {{ libelles.application.reessayer }}
              </button>
              @if (etat.modifier) {
                <a class="modifier" [routerLink]="['/projets', nom()]">{{
                  libelles.application.modifier
                }}</a>
              }
            </div>
          </div>
        }
      </main>
    </usine-cadre>
  `,
  styles: `
    header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 6px 20px;
      border-bottom-width: 1px;
      border-bottom-style: solid;
      border-bottom-color: var(--filet);
    }

    .retour {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      min-height: 44px;
      font-size: 15px;
      font-weight: 600;
      text-decoration: none;
    }

    h1 {
      font-size: 16px;
      font-weight: 700;
      letter-spacing: -0.2px;
      overflow-wrap: anywhere;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 16px;
    }

    .modifier,
    .modifier:hover {
      display: inline-flex;
      align-items: center;
      box-sizing: border-box;
      min-height: 44px;
      padding: 0 2px;
      color: var(--texte);
      font-size: 15px;
      text-decoration: underline;
      text-underline-offset: 5px;
    }

    .alerte {
      flex-basis: 100%;
      color: var(--erreur);
      font-size: 15px;
      line-height: 1.55;
    }

    main {
      flex: 1 1 auto;
      display: flex;
      flex-direction: column;
      min-height: 100vh;
    }

    @media (min-width: 760px) {
      main {
        min-height: auto;
      }
    }

    .message {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      padding: 48px 24px;
      text-align: center;
    }

    .onglet {
      display: flex;
      justify-content: flex-end;
      padding: 0 20px;
    }

    iframe {
      flex: 1 1 auto;
      width: 100%;
      border: none;
      border-top-width: 1px;
      border-top-style: solid;
      border-top-color: var(--filet);
    }
  `,
})
export class Application {
  readonly nom = input.required<string>();

  private readonly client = inject(ClientUsine);
  private readonly etatUsine = inject(EtatUsine);
  private readonly passage = inject(MessageDePassage);
  private readonly router = inject(Router);
  private readonly renderer = inject(Renderer2);
  private readonly site = viewChild<ElementRef<HTMLIFrameElement>>('site');
  private readonly titrePrincipal = viewChild.required<ElementRef<HTMLElement>>('titrePrincipal');
  private readonly injecteur = inject(Injector);
  private readonly page = inject(DOCUMENT);
  private taches = new Subscription();
  private apresUnClic = false;

  protected readonly libelles = libelles;
  protected readonly essai = signal<EtatEssai>({ etape: 'lecture' });
  protected readonly fermeture = signal(false);
  protected readonly erreurFermeture = signal('');
  protected readonly titre = computed(
    () => this.etatUsine.projets().find(({ nom }) => nom === this.nom())?.titre ?? '',
  );
  private readonly arretsDeLEssai = computed(
    () => this.etatUsine.essaisArretes().get(this.nom()) ?? 0,
  );

  constructor() {
    effect(() => {
      const nom = this.nom();
      this.arretsDeLEssai();
      untracked(() => this.demarrer(nom));
    });
    afterRenderEffect(() => {
      const site = this.site();
      const essai = this.essai();
      if (site !== undefined && essai.etape === 'pret') {
        this.renderer.setAttribute(site.nativeElement, 'src', essai.adresse);
      }
    });
    inject(DestroyRef).onDestroy(() => this.taches.unsubscribe());
  }

  protected reessayer(): void {
    this.demarrer(this.nom());
    this.apresUnClic = true;
  }

  protected relancer(): void {
    this.recommencer({ etape: 'demarrage' });
    this.apresUnClic = true;
    this.appeler(this.client.arreterEssai(this.nom()), 'fermeture-essai', () => this.lancer());
  }

  protected fermer(): void {
    this.fermeture.set(true);
    this.erreurFermeture.set('');
    this.appeler(
      this.client.arreterEssai(this.nom()),
      'fermeture-essai',
      () => void this.router.navigate(['/applications']),
      (texte) => {
        this.fermeture.set(false);
        this.erreurFermeture.set(texte);
      },
    );
  }

  private demarrer(nom: string): void {
    this.recommencer({ etape: 'lecture' });
    this.appeler(this.client.essai(nom), 'lancement-essai', (essai) =>
      essai.enCours ? this.attendre(essai) : this.lancer(),
    );
  }

  private lancer(): void {
    this.essai.set({ etape: 'demarrage' });
    this.appeler(this.client.lancerEssai(this.nom()), 'lancement-essai', (essai) =>
      this.attendre(essai),
    );
  }

  private attendre(essai: EssaiDto): void {
    const adresse = essai.adresse;
    if (adresse === null || !ADRESSE_ESSAI.test(adresse)) {
      console.warn("Adresse de l'essai refusée", adresse);
      this.essai.set(echec(libelles.erreurs.probleme));
      return;
    }
    this.essai.set({ etape: 'demarrage' });
    if (this.apresUnClic) {
      this.apresUnClic = false;
      afterNextRender(
        () => {
          const actif = this.page.activeElement;
          if (actif === null || actif === this.page.body) {
            this.titrePrincipal().nativeElement.focus();
          }
        },
        { injector: this.injecteur },
      );
    }
    const pret = timer(0, INTERVALLE_SONDE).pipe(
      exhaustMap(() => repond(adresse)),
      filter((repondu) => repondu),
      map((): EtatEssai => ({ etape: 'pret', adresse })),
    );
    const disparu = timer(INTERVALLE_ESSAI, INTERVALLE_ESSAI).pipe(
      exhaustMap(() =>
        this.client.essai(this.nom()).pipe(
          catchError((erreur: unknown) => {
            this.surErreur(erreur, 'lancement-essai', () => undefined);
            return EMPTY;
          }),
        ),
      ),
      filter(({ enCours }) => !enCours),
      map(() => echec(libelles.application.disparu, true)),
    );
    const lent = timer(ATTENTE_MAXIMALE).pipe(map(() => echec(libelles.application.lent)));
    this.taches.add(
      merge(pret, disparu, lent)
        .pipe(take(1))
        .subscribe((etat) => this.essai.set(etat)),
    );
  }

  private appeler<T>(
    appel: Observable<T>,
    action: Action,
    suite: (reponse: T) => void,
    surEchec: (texte: string) => void = (texte) => this.essai.set(echec(texte)),
  ): void {
    this.taches.add(
      appel.subscribe({
        next: suite,
        error: (erreur: unknown) => this.surErreur(erreur, action, surEchec),
      }),
    );
  }

  private surErreur(erreur: unknown, action: Action, surEchec: (texte: string) => void): void {
    const texte =
      erreur instanceof HttpErrorResponse ? texteErreur(action, erreur) : libelles.erreurs.probleme;
    if (texte === libelles.erreurs.projetDisparu) {
      this.passage.deposer(texte);
      void this.router.navigate(['/applications']);
    } else {
      surEchec(texte);
    }
  }

  private recommencer(etat: EtatEssai): void {
    this.taches.unsubscribe();
    this.taches = new Subscription();
    this.apresUnClic = false;
    this.fermeture.set(false);
    this.erreurFermeture.set('');
    this.essai.set(etat);
  }
}
