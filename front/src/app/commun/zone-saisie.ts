import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  booleanAttribute,
  input,
  linkedSignal,
  model,
  output,
  viewChild,
} from '@angular/core';
import { Commande, NomCommande, commandeTapee, estFormeCommande } from '../commandes/commandes';
import { MenuCommandes } from '../commandes/menu-commandes';
import { libelles } from '../textes/libelles';
import { CibleFocusEcran } from './focus-ecran';

let numero = 0;

@Component({
  selector: 'usine-zone-saisie',
  imports: [CibleFocusEcran, MenuCommandes],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-menu-commandes #menu [(texte)]="texte" [commandes]="commandes()" />
    <form [class.desactivee]="desactivee()" (submit)="$event.preventDefault(); envoyer()">
      <label class="masque" [for]="idChamp">{{ etiquette() }}</label>
      <div class="champ">
        @if (menu.suggestion()) {
          <div class="suggestion" aria-hidden="true">
            <span class="tape">{{ texte() }}</span>
            <span>{{ menu.suggestion() }}</span>
          </div>
        }
        <textarea
          #champ
          rows="1"
          usineCibleFocus="saisie"
          aria-autocomplete="list"
          [id]="idChamp"
          [value]="texte()"
          [disabled]="desactivee()"
          [placeholder]="indication()"
          [attr.aria-controls]="menu.ouvert() ? menu.idListe : null"
          [attr.aria-activedescendant]="menu.idChoisie()"
          [attr.aria-describedby]="idErreur"
          (input)="texte.set(champ.value)"
          (keydown)="menu.touche($event); touche($event)"
        ></textarea>
      </div>
      <button
        type="submit"
        [disabled]="occupee() || desactivee()"
        [attr.aria-busy]="occupee() || null"
      >
        {{ libelles.saisie.envoyer }}
      </button>
    </form>
    @let message = refus() || erreur();
    <p role="status" [id]="idErreur" [class.vide]="!message">{{ message }}</p>
  `,
  styles: `
    :host {
      position: relative;
      display: flex;
      flex-direction: column;
      width: 100%;
      max-width: 720px;
    }

    form {
      display: flex;
      gap: 14px;
      align-items: center;
      padding: 6px 0 0;
      border-top-width: 1.5px;
      border-top-style: solid;
      border-top-color: var(--texte);
    }

    form:has(textarea:focus) {
      border-top-width: 2px;
      border-top-color: var(--accent);
    }

    .champ {
      position: relative;
      display: flex;
      flex: 1 1 auto;
      min-width: 0;
    }

    .suggestion {
      position: absolute;
      inset: 0;
      padding: 12px 0;
      overflow: hidden;
      color: var(--texte-secondaire);
      font-size: 17px;
      line-height: 24px;
      white-space: pre-wrap;
      pointer-events: none;
    }

    .tape {
      color: transparent;
    }

    textarea {
      position: relative;
      flex: 1 1 auto;
      min-width: 0;
      max-height: 144px;
      padding: 12px 0;
      border: none;
      outline: none;
      resize: none;
      field-sizing: content;
      background: transparent;
      color: var(--texte);
      font: inherit;
      font-size: 17px;
      line-height: 24px;
    }

    button {
      flex: none;
      min-height: 44px;
      padding: 0 4px;
      border: none;
      background: none;
      color: var(--accent);
      font: inherit;
      font-size: 16px;
      font-weight: 600;
      cursor: pointer;
    }

    button:disabled {
      cursor: default;
    }

    form.desactivee {
      border-top-color: var(--barre-a-venir);
    }

    form.desactivee textarea {
      color: var(--texte-secondaire);
    }

    form.desactivee button {
      color: var(--texte-desactive);
    }

    p {
      min-height: 20px;
      margin-top: 8px;
      color: var(--erreur);
      font-size: 14px;
      line-height: 20px;
    }

    p.vide {
      min-height: 0;
      margin-top: 0;
    }
  `,
})
export class ZoneSaisie {
  readonly etiquette = input.required<string>();
  readonly indication = input('');
  readonly erreur = input('');
  readonly occupee = input(false, { transform: booleanAttribute });
  readonly desactivee = input(false, { transform: booleanAttribute });
  readonly commandes = input<readonly Commande[]>([]);
  readonly texte = model('');
  readonly envoi = output<string>();
  readonly commande = output<NomCommande>();

  protected readonly libelles = libelles;
  protected readonly refus = linkedSignal({ source: this.texte, computation: () => '' });
  private readonly champ = viewChild.required<ElementRef<HTMLTextAreaElement>>('champ');
  protected readonly idChamp = `saisie-${++numero}`;
  protected readonly idErreur = `${this.idChamp}-erreur`;

  focaliser(): void {
    this.champ().nativeElement.focus();
  }

  protected touche(evenement: KeyboardEvent): void {
    if (evenement.key !== 'Enter' || evenement.shiftKey || evenement.isComposing) {
      return;
    }
    evenement.preventDefault();
    this.envoyer();
  }

  protected envoyer(): void {
    const texte = this.texte();
    if (texte.trim() === '' || this.desactivee()) {
      return;
    }
    if (estFormeCommande(texte)) {
      this.focaliser();
      this.executer(texte);
    } else if (!this.occupee()) {
      this.focaliser();
      this.envoi.emit(texte);
    }
  }

  private executer(texte: string): void {
    const commande = commandeTapee(texte, this.commandes());
    if (commande === undefined) {
      this.refus.set(libelles.commandeImpossible(texte.trim()));
    } else {
      this.texte.set('');
      this.commande.emit(commande);
    }
  }
}
