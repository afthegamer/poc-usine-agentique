import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { heure } from '../textes/dates';
import { libelles } from '../textes/libelles';

@Component({
  selector: 'usine-message',
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.toi]': "auteur() === 'toi'" },
  template: `
    @if (auteur() === 'usine') {
      <span class="pastille" aria-hidden="true">{{ libelles.initialeUsine }}</span>
    }
    <div class="corps">
      @if (auteur() === 'usine') {
        <ng-container [ngTemplateOutlet]="signature" />
      }
      <div class="contenu"><ng-content /></div>
      @if (auteur() === 'toi') {
        <ng-container [ngTemplateOutlet]="signature" />
      }
    </div>

    <ng-template #signature>
      @if (moment(); as instant) {
        <time [attr.datetime]="instant.toISOString()">{{
          libelles.signature(nom(), heure(instant))
        }}</time>
      } @else {
        <span class="signature">{{ libelles.signature(nom(), null) }}</span>
      }
    </ng-template>
  `,
  styles: `
    :host {
      display: flex;
      gap: 12px;
      align-items: flex-start;
    }

    :host(.toi) {
      align-self: flex-end;
      max-width: 80%;
    }

    .pastille {
      flex: none;
      display: flex;
      align-items: center;
      justify-content: center;
      box-sizing: border-box;
      width: 30px;
      height: 30px;
      border-width: 1.5px;
      border-style: solid;
      border-color: var(--accent);
      border-radius: 50%;
      color: var(--accent);
      font-size: 14px;
      font-weight: 700;
    }

    .corps {
      flex: 1 1 auto;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: var(--ecart-message, 12px);
    }

    :host(.toi) .corps {
      align-items: flex-end;
      gap: 4px;
    }

    time,
    .signature {
      color: var(--texte-secondaire);
      font-size: 13px;
    }

    .contenu {
      display: flex;
      flex-direction: column;
      gap: var(--ecart-message, 12px);
      font-size: 17px;
      line-height: 1.6;
      overflow-wrap: anywhere;
    }

    :host(.toi) .contenu {
      font-weight: 500;
      line-height: 1.5;
      text-align: right;
    }
  `,
})
export class Message {
  readonly auteur = input.required<'toi' | 'usine'>();
  readonly moment = input<Date | null>(null);

  protected readonly libelles = libelles;
  protected readonly heure = heure;
  protected readonly nom = computed(() =>
    this.auteur() === 'toi' ? libelles.toi : libelles.marque,
  );
}
