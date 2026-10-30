import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  booleanAttribute,
  computed,
  inject,
  input,
  model,
  output,
  viewChild,
} from '@angular/core';
import { DemandeDetailDto } from '../../../api/types';
import { ActionSoulignee } from '../../../commun/action-soulignee';
import { FocusApresAppel } from '../../../commun/focus-ecran';
import { Message } from '../../../commun/message';
import { PhraseAttente } from '../../../commun/phrase-attente';
import { Saisie, revueUneParUne } from '../../../etat/brouillons';
import { HORLOGE } from '../../../etat/horloge';
import { libelles } from '../../../textes/libelles';

@Component({
  selector: 'usine-questions',
  imports: [ActionSoulignee, FocusApresAppel, Message, PhraseAttente],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-message auteur="usine" [moment]="moment()">
      <p>{{ libelles.questions.introduction(questions().length, ajout()) }}</p>
      @for (reponse of reponses(); track $index) {
        <div class="repondue">
          <span class="numero">{{ libelles.questions.numero($index + 1) }}</span>
          <span>{{ questions()[$index] }}</span>
          <usine-message auteur="toi" [moment]="dateDe(reponse)">{{
            reponse.texte || libelles.questions.reponseVide
          }}</usine-message>
        </div>
      }
      @if (questions()[rang()]; as question) {
        <div class="courante">
          <h2 #titre tabindex="-1">
            <span class="numero">{{
              libelles.questions.position(rang() + 1, questions().length)
            }}</span>
            <span class="question">{{ question }}</span>
          </h2>
          <div class="actions">
            <button
              type="button"
              usine-action-soulignee
              [disabled]="occupee()"
              [attr.aria-busy]="occupee() || null"
              [usineFocusApresAppel]="occupee()"
              (click)="$event.detail > 1 || repondre('')"
            >
              {{ libelles.questions.sansPreference }}
            </button>
            @if (rang() > 0) {
              <button
                type="button"
                usine-action-soulignee
                [disabled]="occupee()"
                (click)="revenir()"
              >
                {{ libelles.questions.precedente }}
              </button>
            }
          </div>
          @if (occupee()) {
            <usine-phrase-attente>{{ libelles.questions.envoi }}</usine-phrase-attente>
          }
        </div>
      }
    </usine-message>
  `,
  styleUrl: './revue-une-par-une.css',
  styles: `
    .repondue {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 15px;
      line-height: 1.5;
    }

    .question {
      white-space: pre-line;
    }
  `,
})
export class Questions {
  readonly detail = input.required<DemandeDetailDto>();
  readonly ajout = input(false, { transform: booleanAttribute });
  readonly occupee = input(false, { transform: booleanAttribute });
  readonly texte = model('');
  readonly envoi = output<string[]>();
  readonly passage = output();

  private readonly horloge = inject(HORLOGE);
  private readonly injecteur = inject(Injector);
  private readonly titre = viewChild<ElementRef<HTMLElement>>('titre');

  protected readonly libelles = libelles;
  protected readonly questions = computed(() => this.detail().questions);
  protected readonly moment = computed(() => new Date(this.detail().misAJourLe));
  private readonly revue = revueUneParUne(() => this.detail(), 'reponses');
  protected readonly reponses = this.revue.saisies;
  protected readonly rang = computed(() => this.reponses().length);

  saisir(texte: string): void {
    this.repondre(texte);
  }

  protected dateDe(saisie: Saisie): Date {
    return new Date(saisie.heure);
  }

  protected repondre(reponse: string): void {
    const reponses = [...this.reponses(), { texte: reponse, heure: this.horloge().toISOString() }];
    if (reponses.length < this.questions().length) {
      this.garder(reponses);
      this.texte.set('');
    } else {
      this.envoi.emit(reponses.map(({ texte }) => texte));
    }
  }

  protected revenir(): void {
    this.texte.set(this.revue.revenir());
    this.passer();
  }

  private garder(reponses: readonly Saisie[]): void {
    this.revue.garder(reponses);
    this.passer();
  }

  private passer(): void {
    this.passage.emit();
    afterNextRender(() => this.titre()?.nativeElement.focus(), { injector: this.injecteur });
  }
}
