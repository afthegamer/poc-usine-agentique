import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  computed,
  input,
  linkedSignal,
  model,
  viewChild,
} from '@angular/core';
import { libelles } from '../textes/libelles';
import { Commande, estFormeCommande } from './commandes';

let numero = 0;

@Component({
  selector: 'usine-menu-commandes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (ouvert()) {
      <div #menu class="menu">
        <span class="titre">{{ libelles.commandes.titre }}</span>
        <div
          class="liste"
          role="listbox"
          [id]="idListe"
          [attr.aria-label]="libelles.commandes.liste"
        >
          @for (commande of trouvees(); track commande.nom; let rang = $index) {
            <button
              type="button"
              role="option"
              tabindex="-1"
              [id]="idOption(commande)"
              [attr.aria-selected]="rang === choix()"
              (mouseenter)="choix.set(rang)"
              (mousedown)="$event.preventDefault()"
              (click)="accepter(commande)"
            >
              <span class="nom">{{ commande.nom }}</span>
              <span class="description">{{ commande.description }}</span>
            </button>
          }
        </div>
        @if (commandes().length === 0) {
          <span class="aucune">{{ libelles.commandes.aucuneIci }}</span>
        } @else if (trouvees().length === 0) {
          <span class="aucune">{{ libelles.commandes.aucuneQuiCommencePar(mot()) }}</span>
        }
        <span class="pied">{{ libelles.commandes.pied }}</span>
      </div>
    }
  `,
  styles: `
    :host {
      position: absolute;
      left: 0;
      right: 0;
      bottom: calc(100% + 8px);
    }

    .menu {
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: 8px;
      border-width: 1px;
      border-style: solid;
      border-color: var(--filet);
      border-radius: 6px;
      background-color: var(--blanc);
      box-shadow: 0 12px 32px rgba(22, 22, 22, 0.1);
    }

    .titre {
      padding: 6px 12px 8px;
      color: var(--texte-secondaire);
      font-size: 13px;
    }

    .liste {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    button {
      display: flex;
      align-items: center;
      gap: 14px;
      min-height: 48px;
      padding: 0 12px;
      border: none;
      border-radius: 0;
      background-color: transparent;
      color: var(--texte);
      font: inherit;
      text-align: left;
      cursor: pointer;
    }

    button[aria-selected='true'] {
      background-color: var(--accent-pale);
    }

    .nom {
      flex: none;
      width: 92px;
      font-size: 15px;
      font-weight: 600;
    }

    .description,
    .aucune {
      color: var(--texte-secondaire);
      font-size: 15px;
    }

    .aucune {
      padding: 12px;
    }

    .pied {
      margin-top: 4px;
      padding: 8px 12px 4px;
      border-top-width: 1px;
      border-top-style: solid;
      border-top-color: var(--filet);
      color: var(--texte-secondaire);
      font-size: 13px;
    }
  `,
})
export class MenuCommandes {
  readonly texte = model.required<string>();
  readonly commandes = input<readonly Commande[]>([]);

  protected readonly libelles = libelles;
  readonly idListe = `commandes-${++numero}`;
  private readonly menu = viewChild<ElementRef<HTMLElement>>('menu');
  private readonly ferme = linkedSignal({ source: this.texte, computation: () => false });
  readonly ouvert = computed(() => estFormeCommande(this.texte()) && !this.ferme());
  protected readonly mot = computed(() => this.texte().trim().slice(1));
  protected readonly trouvees = computed(() => {
    const debut = `/${this.mot().toLowerCase()}`;
    return this.ouvert() ? this.commandes().filter(({ nom }) => nom.startsWith(debut)) : [];
  });
  protected readonly choix = linkedSignal({ source: this.trouvees, computation: () => 0 });
  private readonly choisie = computed<Commande | undefined>(() => this.trouvees()[this.choix()]);
  readonly idChoisie = computed(() => {
    const choisie = this.choisie();
    return choisie === undefined ? null : this.idOption(choisie);
  });
  readonly suggestion = computed(() => {
    const texte = this.texte();
    const choisie = this.choisie();
    return choisie === undefined || texte !== texte.trimEnd()
      ? ''
      : choisie.nom.slice(this.mot().length + 1);
  });

  constructor() {
    afterRenderEffect(() => {
      const menu = this.menu()?.nativeElement;
      if (menu !== undefined && menu.getBoundingClientRect().top < 0) {
        menu.scrollIntoView({ block: 'nearest' });
      }
    });
  }

  touche(evenement: KeyboardEvent): void {
    if (!this.ouvert() || evenement.isComposing) {
      return;
    }
    const nombre = this.trouvees().length;
    if (evenement.key === 'Escape') {
      this.ferme.set(true);
    } else if (nombre > 0 && evenement.key === 'ArrowDown') {
      this.choix.update((choix) => (choix + 1) % nombre);
    } else if (nombre > 0 && evenement.key === 'ArrowUp') {
      this.choix.update((choix) => (choix - 1 + nombre) % nombre);
    } else if (nombre > 0 && evenement.key === 'Tab' && !evenement.shiftKey) {
      this.accepter(this.trouvees()[this.choix()]);
    } else {
      return;
    }
    evenement.preventDefault();
  }

  protected accepter(commande: Commande): void {
    this.texte.set(commande.nom);
  }

  protected idOption(commande: Commande): string {
    return `${this.idListe}-${commande.nom.slice(1)}`;
  }
}
