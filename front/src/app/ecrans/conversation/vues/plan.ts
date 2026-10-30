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
  linkedSignal,
  model,
  output,
  viewChild,
} from '@angular/core';
import { DemandeDetailDto, ValidationDto } from '../../../api/types';
import { ActionSoulignee } from '../../../commun/action-soulignee';
import { BoutonPlein } from '../../../commun/bouton-plein';
import { FocusApresAppel } from '../../../commun/focus-ecran';
import { Message } from '../../../commun/message';
import { Saisie, revueUneParUne } from '../../../etat/brouillons';
import { HORLOGE } from '../../../etat/horloge';
import { heure } from '../../../textes/dates';
import { libelles } from '../../../textes/libelles';

@Component({
  selector: 'usine-plan',
  imports: [ActionSoulignee, BoutonPlein, FocusApresAppel, Message],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-message auteur="usine" [moment]="moment()">
      <p>{{ libelles.plan.introduction(stories().length, ajout()) }}</p>
      @if (dejaVues().length > 0) {
        <ul [attr.aria-label]="libelles.plan.dejaVu">
          @for (vue of dejaVues(); track $index) {
            <li>
              <svg
                width="20"
                height="20"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2.4"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <path d="m5 12 5 5L20 7" />
              </svg>
              <span class="fonction">
                <span>{{ vue.titre }}</span>
                @if (vue.remarque) {
                  <span class="remarque">{{ vue.remarque }}</span>
                }
              </span>
              <time [attr.datetime]="vue.datetime" [class.a-changer]="vue.remarque">{{
                vue.avis
              }}</time>
            </li>
          }
        </ul>
      }
      @if (stories()[rang()]; as story) {
        <div class="courante">
          <h2 #titre tabindex="-1">
            <span class="numero">{{ libelles.plan.position(rang() + 1, stories().length) }}</span>
            <span>{{ story.titre }}</span>
          </h2>
          <p class="description">{{ story.description }}</p>
          <div class="actions">
            <button
              type="button"
              usine-bouton-plein
              [disabled]="occupee()"
              [attr.aria-busy]="occupee() || null"
              [usineFocusApresAppel]="occupee()"
              (click)="$event.detail > 1 || donner('')"
            >
              {{ libelleAccord() }}
            </button>
            <button type="button" usine-action-soulignee (click)="changer()">
              {{ libelles.plan.changer }}
            </button>
            @if (rang() > 0) {
              <button
                type="button"
                usine-action-soulignee
                [disabled]="occupee()"
                (click)="revenir()"
              >
                {{ libelles.plan.precedente }}
              </button>
            }
          </div>
        </div>
      }
    </usine-message>
  `,
  styleUrl: './revue-une-par-une.css',
  styles: `
    ul {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 0;
      list-style: none;
    }

    li {
      display: flex;
      gap: 12px;
      align-items: flex-start;
      color: var(--texte-secondaire);
      font-size: 16px;
      line-height: 1.5;
    }

    svg {
      flex: none;
      margin-top: 2px;
      color: var(--accent);
    }

    .fonction {
      flex: 1 1 auto;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .remarque {
      color: var(--texte-neutre);
      white-space: pre-line;
    }

    time {
      flex: none;
      font-size: 13px;
      line-height: 24px;
    }

    .a-changer {
      color: var(--attente);
    }

    .description {
      color: var(--texte-neutre);
      font-size: 16px;
      line-height: 1.6;
    }
  `,
})
export class Plan {
  readonly detail = input.required<DemandeDetailDto>();
  readonly ajout = input(false, { transform: booleanAttribute });
  readonly occupee = input(false, { transform: booleanAttribute });
  readonly texte = model('');
  readonly envoi = output<ValidationDto>();
  readonly versLaSaisie = output();
  readonly passage = output();

  private readonly horloge = inject(HORLOGE);
  private readonly injecteur = inject(Injector);
  private readonly titre = viewChild<ElementRef<HTMLElement>>('titre');

  protected readonly libelles = libelles;
  protected readonly stories = computed(() => this.detail().backlog?.stories ?? []);
  protected readonly moment = computed(() => new Date(this.detail().misAJourLe));
  private readonly revue = revueUneParUne(() => this.detail(), 'backlog');
  protected readonly avis = this.revue.saisies;
  protected readonly rang = computed(() => this.avis().length);
  private readonly correction = linkedSignal({ source: this.avis, computation: () => false });
  readonly saisieDeRemarque = this.correction.asReadonly();
  readonly position = computed(() => ({ rang: this.rang() + 1, total: this.stories().length }));
  protected readonly dejaVues = computed(() =>
    this.avis().map((saisie, rang) => ({
      titre: this.stories()[rang]?.titre ?? '',
      remarque: saisie.texte,
      datetime: saisie.heure,
      avis: (saisie.texte ? libelles.plan.aChanger : libelles.plan.accord)(
        heure(new Date(saisie.heure)),
      ),
    })),
  );
  protected readonly libelleAccord = computed(() => {
    const { rang, total } = this.position();
    if (rang < total) {
      return libelles.plan.daccord;
    }
    return this.avis().some(({ texte }) => texte !== '')
      ? libelles.plan.envoyerRemarques
      : libelles.plan.lancer;
  });

  saisir(texte: string): void {
    this.donner(texte);
  }

  protected donner(remarque: string): void {
    const avis = [...this.avis(), { texte: remarque, heure: this.horloge().toISOString() }];
    if (avis.length < this.stories().length) {
      this.garder(avis);
      this.texte.set('');
    } else {
      this.envoi.emit(this.validation(avis));
    }
  }

  protected changer(): void {
    this.correction.set(true);
    this.versLaSaisie.emit();
  }

  protected revenir(): void {
    const remarque = this.revue.revenir();
    this.texte.set(remarque);
    this.correction.set(remarque !== '');
    this.passer();
  }

  private validation(avis: readonly Saisie[]): ValidationDto {
    const remarques = this.stories().flatMap(({ id }, rang) =>
      avis[rang]?.texte ? [{ reference: id, texte: avis[rang].texte }] : [],
    );
    return remarques.length === 0
      ? { etape: 'backlog', approuver: true }
      : { etape: 'backlog', approuver: false, remarques };
  }

  private garder(avis: readonly Saisie[]): void {
    this.revue.garder(avis);
    this.passer();
  }

  private passer(): void {
    this.passage.emit();
    afterNextRender(() => this.titre()?.nativeElement.focus(), { injector: this.injecteur });
  }
}
