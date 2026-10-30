import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { Message } from '../../commun/message';
import { PhraseAttente } from '../../commun/phrase-attente';
import { SeparateurJour } from '../../commun/separateur-jour';
import { HORLOGE } from '../../etat/horloge';
import { jour, memeJour } from '../../textes/dates';
import { libelles } from '../../textes/libelles';
import { Discussion, ElementDiscussion } from '../../discussion/discussion';

type LigneDiscussion =
  | { readonly genre: 'jour'; readonly jour: string }
  | (ElementDiscussion & { readonly rang: number });

@Component({
  selector: 'usine-fil-discussion',
  imports: [ActionSoulignee, Message, PhraseAttente, SeparateurJour],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (ligne of lignes(); track $index) {
      @switch (ligne.genre) {
        @case ('jour') {
          <usine-separateur-jour [jour]="ligne.jour" />
        }
        @case ('nouvelle') {
          <usine-separateur-jour [jour]="libelles.discussion.nouvelle" />
        }
        @case ('toi') {
          <usine-message auteur="toi" [moment]="ligne.moment">
            <span class="lignes">{{ ligne.texte }}</span>
          </usine-message>
        }
        @case ('usine') {
          <usine-message auteur="usine" [moment]="ligne.moment">
            <p class="lignes">{{ ligne.texte }}</p>
            @if (discussion.confirmation(); as attente) {
              @if (attente.rang === ligne.rang) {
                <div class="actions">
                  <button
                    #oui
                    type="button"
                    usine-action-soulignee
                    [disabled]="attente.envoi"
                    [attr.aria-busy]="attente.envoi || null"
                    (click)="choisir(true)"
                  >
                    {{ libelles.discussion.oui }}
                  </button>
                  <button
                    #non
                    type="button"
                    usine-action-soulignee
                    [disabled]="attente.envoi"
                    [attr.aria-busy]="attente.envoi || null"
                    (click)="choisir(false)"
                  >
                    {{ libelles.discussion.non }}
                  </button>
                </div>
                @if (attente.erreur) {
                  <p class="alerte" role="alert">{{ attente.erreur }}</p>
                }
              }
            }
          </usine-message>
        }
      }
    }
    @if (discussion.indicateur(); as indicateur) {
      <usine-phrase-attente>{{ indicateur }}</usine-phrase-attente>
    }
  `,
  styles: `
    :host {
      display: contents;
    }

    .lignes {
      white-space: pre-line;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 22px;
    }

    .alerte {
      color: var(--erreur);
      font-size: 14px;
      line-height: 20px;
    }
  `,
})
export class FilDiscussion {
  readonly depuis = input<Date | null>(null);
  readonly boutonsRetires = output();

  protected readonly discussion = inject(Discussion);
  private readonly horloge = inject(HORLOGE);
  protected readonly libelles = libelles;
  private readonly oui = viewChild<ActionSoulignee, ElementRef<HTMLButtonElement>>('oui', {
    read: ElementRef,
  });
  private readonly non = viewChild<ActionSoulignee, ElementRef<HTMLButtonElement>>('non', {
    read: ElementRef,
  });
  private choix: boolean | null = null;

  protected readonly lignes = computed<readonly LigneDiscussion[]>(() => {
    let precedent = this.depuis();
    const maintenant = this.horloge();
    return this.discussion.elements().flatMap((element, rang): LigneDiscussion[] => {
      const ligne = { ...element, rang };
      if (element.genre === 'nouvelle') {
        return [ligne];
      }
      const autreJour = precedent === null || !memeJour(precedent, element.moment);
      precedent = element.moment;
      return autreJour
        ? [{ genre: 'jour', jour: jour(element.moment, maintenant) }, ligne]
        : [ligne];
    });
  });

  constructor() {
    const hote = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const page = inject(DOCUMENT);
    const injecteur = inject(Injector);
    effect(() => {
      const attente = this.discussion.confirmation();
      if (attente?.envoi) {
        return;
      }
      untracked(() => {
        const actif = page.activeElement;
        const dansLesBoutons = hote.querySelector('.actions')?.contains(actif) ?? false;
        const renduALaPage = actif === null || actif === page.body;
        const choix = this.choix;
        this.choix = null;
        if (attente === null && (dansLesBoutons || (choix !== null && renduALaPage))) {
          this.boutonsRetires.emit();
        } else if (attente !== null && choix !== null && (dansLesBoutons || renduALaPage)) {
          const bouton = choix ? this.oui : this.non;
          afterNextRender(() => bouton()?.nativeElement.focus(), { injector: injecteur });
        }
      });
    });
  }

  protected choisir(accord: boolean): void {
    this.choix = accord;
    this.discussion.confirmer(accord);
  }
}
