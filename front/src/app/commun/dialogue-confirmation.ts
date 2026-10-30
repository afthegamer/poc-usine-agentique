import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  booleanAttribute,
  input,
  model,
  output,
  viewChild,
} from '@angular/core';
import { ActionSoulignee } from './action-soulignee';
import { BoutonPlein } from './bouton-plein';

let numero = 0;

@Component({
  selector: 'usine-dialogue-confirmation',
  imports: [ActionSoulignee, BoutonPlein],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog #dialogue [attr.aria-labelledby]="idTitre" (close)="fermeture()">
      <h2 [id]="idTitre">{{ titre() }}</h2>
      <p>{{ phrase() }}</p>
      <div class="actions">
        <button type="button" usine-bouton-plein [danger]="danger()" (click)="repondre(true)">
          {{ oui() }}
        </button>
        <button #refus type="button" usine-action-soulignee (click)="repondre(false)">
          {{ non() }}
        </button>
      </div>
    </dialog>
  `,
  styles: `
    dialog {
      box-sizing: border-box;
      width: 440px;
      max-width: calc(100% - 32px);
      padding: 24px;
      border-width: 1px;
      border-style: solid;
      border-color: var(--filet);
      border-radius: 6px;
      background-color: var(--blanc);
      box-shadow: 0 12px 32px rgba(22, 22, 22, 0.1);
      color: var(--texte);
      font: inherit;
    }

    dialog::backdrop {
      background: rgba(22, 22, 22, 0.24);
    }

    h2 {
      font-size: 22px;
      font-weight: 700;
      letter-spacing: -0.2px;
    }

    p {
      margin-top: 12px;
      font-size: 17px;
      line-height: 1.6;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 16px;
      margin-top: 24px;
    }
  `,
})
export class DialogueConfirmation {
  readonly titre = input.required<string>();
  readonly phrase = input.required<string>();
  readonly oui = input.required<string>();
  readonly non = input.required<string>();
  readonly danger = input(false, { transform: booleanAttribute });
  readonly ouvert = model(false);
  readonly reponse = output<boolean>();

  protected readonly idTitre = `dialogue-${++numero}`;
  private readonly dialogue = viewChild.required<ElementRef<HTMLDialogElement>>('dialogue');
  private readonly refus = viewChild.required<ActionSoulignee, ElementRef<HTMLButtonElement>>(
    'refus',
    { read: ElementRef },
  );

  constructor() {
    afterRenderEffect(() => {
      const dialogue = this.dialogue().nativeElement;
      if (this.ouvert() && !dialogue.open) {
        dialogue.showModal();
        this.refus().nativeElement.focus();
      } else if (!this.ouvert() && dialogue.open) {
        dialogue.close();
      }
    });
  }

  protected repondre(accord: boolean): void {
    this.ouvert.set(false);
    this.reponse.emit(accord);
  }

  protected fermeture(): void {
    if (this.ouvert()) {
      this.repondre(false);
    }
  }
}
