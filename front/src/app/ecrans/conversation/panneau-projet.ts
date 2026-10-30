import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { BarresEtapes } from '../../commun/barres-etapes';
import { BoutonPlein } from '../../commun/bouton-plein';
import { FocusApresAppel } from '../../commun/focus-ecran';
import { PhraseAttente } from '../../commun/phrase-attente';
import { libelles } from '../../textes/libelles';

export type BlocSite =
  | { readonly forme: 'a-venir' }
  | {
      readonly forme: 'publie' | 'ajout' | 'ajout-en-fabrication';
      readonly lien: string;
      readonly ouvert: boolean;
    };

export interface LigneHistorique {
  readonly id: string;
  readonly texte: string;
  readonly carre: 'attente' | 'accent' | 'texte';
}

let numero = 0;

@Component({
  selector: 'usine-panneau-projet',
  imports: [ActionSoulignee, BarresEtapes, BoutonPlein, FocusApresAppel, PhraseAttente, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <aside [attr.aria-labelledby]="titre() ? idTitre : null">
      <div class="entete" [class.espacee]="publie() || description() !== ''">
        <span class="intitule">{{ libelles.panneau.intitule }}</span>
        @if (titre()) {
          <h2 [id]="idTitre">{{ titre() }}</h2>
        }
        @if (description()) {
          <p class="note">{{ description() }}</p>
        }
        @if (pret()) {
          <span class="pret">{{ libelles.panneau.pret }}</span>
        }
      </div>
      <div class="etapes">
        @let titreDeLAjout = titreAjout();
        @if (titreDeLAjout !== null) {
          <span class="ajout">{{ libelles.panneau.ajout(titreDeLAjout) }}</span>
        }
        <usine-barres-etapes
          [etapes]="etapes()"
          [courante]="etape()"
          [derniereAtteinte]="pret()"
          [sansEtapeCourante]="sansEtapeCourante()"
        />
      </div>
      @if (phrase()) {
        <usine-phrase-attente [attend]="attend()">{{ phrase() }}</usine-phrase-attente>
      }
      @if (site(); as bloc) {
        @if (bloc.forme === 'a-venir') {
          <div class="apercu a-venir">{{ libelles.panneau.siteAVenir }}</div>
        } @else {
          <div>
            <div class="apercu" [class.pendant-ajout]="bloc.forme !== 'publie'">{{ apercu() }}</div>
            <div class="actions">
              @if (bloc.forme !== 'publie') {
                <p class="note">{{ siteActuel() }}</p>
                <a usine-action-soulignee [routerLink]="bloc.lien">{{
                  libelles.panneau.ouvrirSite
                }}</a>
              } @else {
                <a usine-bouton-plein [routerLink]="bloc.lien">{{ libelles.panneau.ouvrirSite }}</a>
                <button type="button" usine-action-soulignee (click)="modification.emit()">
                  {{ libelles.panneau.demanderModification }}
                </button>
              }
            </div>
          </div>
        }
      }
      @if (historique().length > 0) {
        <div class="historique">
          <span class="intitule">{{ libelles.panneau.historique }}</span>
          <ul>
            @for (ligne of historique(); track ligne.id) {
              <li>
                <span class="carre" [class]="ligne.carre" aria-hidden="true"></span>
                <span>{{ ligne.texte }}</span>
              </li>
            }
          </ul>
        </div>
      }
      @if (annulable()) {
        <div class="annulation">
          <button
            type="button"
            usine-action-soulignee
            danger
            [disabled]="annulation()"
            [attr.aria-busy]="annulation() || null"
            [usineFocusApresAppel]="annulation()"
            (click)="annuler.emit()"
          >
            {{ libelles.panneau.annuler(titreAjout() !== null) }}
          </button>
          @if (titreAjout() !== null) {
            <span class="note">{{ libelles.panneau.siteIntact }}</span>
          }
          @if (erreurAnnulation()) {
            <p class="alerte" role="alert">{{ erreurAnnulation() }}</p>
          }
        </div>
      }
    </aside>
  `,
  styles: `
    aside,
    .entete,
    .etapes,
    .historique,
    .annulation,
    ul {
      display: flex;
      flex-direction: column;
    }

    aside {
      gap: 22px;
    }

    .entete {
      gap: 4px;
    }

    .entete.espacee {
      gap: 8px;
    }

    .pret {
      align-self: flex-start;
      color: var(--accent);
      font-size: 13px;
      font-weight: 600;
    }

    .etapes {
      gap: 12px;
    }

    .intitule {
      color: var(--texte-secondaire);
      font-size: 13px;
    }

    h2 {
      font-size: 22px;
      font-weight: 700;
      letter-spacing: -0.2px;
      overflow-wrap: anywhere;
    }

    .ajout {
      font-size: 15px;
      font-weight: 600;
      overflow-wrap: anywhere;
    }

    .apercu {
      display: flex;
      align-items: center;
      justify-content: center;
      height: 180px;
      padding: 16px;
      background: var(--bloc);
      color: var(--texte-secondaire);
      font-size: 14px;
      line-height: 1.5;
      text-align: center;
    }

    .apercu.a-venir {
      height: 220px;
      padding: 20px;
    }

    .apercu.pendant-ajout {
      height: 140px;
    }

    .actions {
      display: flex;
      flex-direction: column;
      gap: 10px;
      padding-top: 14px;
    }

    .note {
      color: var(--texte-secondaire);
      font-size: 14px;
      line-height: 1.5;
    }

    .historique,
    ul {
      gap: 10px;
    }

    ul {
      padding: 0;
      list-style: none;
      font-size: 14px;
    }

    li {
      display: flex;
      gap: 10px;
      align-items: center;
    }

    .carre {
      flex: none;
      width: 8px;
      height: 8px;
      background-color: var(--texte);
    }

    .carre.attente {
      background-color: var(--attente);
    }

    .carre.accent {
      background-color: var(--accent);
    }

    .annulation {
      align-items: flex-start;
      gap: 2px;
    }

    .annulation .note {
      padding: 0 4px;
      font-size: 13px;
    }

    .alerte {
      color: var(--erreur);
      font-size: 15px;
      line-height: 1.55;
    }
  `,
})
export class PanneauProjet {
  readonly titre = input.required<string>();
  readonly description = input('');
  readonly etape = input.required<number | null>();
  readonly pret = input(false, { transform: booleanAttribute });
  readonly sansEtapeCourante = input(false, { transform: booleanAttribute });
  readonly titreAjout = input<string | null>(null);
  readonly progression = input('');
  readonly phrase = input('');
  readonly attend = input(false, { transform: booleanAttribute });
  readonly site = input<BlocSite | null>(null);
  readonly historique = input<readonly LigneHistorique[]>([]);
  readonly annulable = input(false, { transform: booleanAttribute });
  readonly annulation = input(false, { transform: booleanAttribute });
  readonly erreurAnnulation = input('');
  readonly modification = output();
  readonly annuler = output();

  protected readonly libelles = libelles;
  protected readonly idTitre = `panneau-${++numero}`;
  protected readonly apercu = computed(() => {
    const bloc = this.site();
    return bloc !== null && bloc.forme !== 'a-venir' && bloc.ouvert
      ? libelles.panneau.siteOuvert
      : libelles.panneau.siteAOuvrir;
  });
  protected readonly siteActuel = computed(() =>
    this.site()?.forme === 'ajout-en-fabrication'
      ? libelles.panneau.siteActuelFabrication
      : libelles.panneau.siteActuel,
  );
  protected readonly publie = computed(() => {
    const bloc = this.site();
    return bloc !== null && bloc.forme !== 'a-venir';
  });
  protected readonly etapes = computed(() =>
    libelles.etapes.liste.map((etape, rang) => (rang === 1 ? etape + this.progression() : etape)),
  );
}
