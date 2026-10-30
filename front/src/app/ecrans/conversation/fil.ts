import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DemandeDetailDto, lireEtapeAffichee, lireStatut } from '../../api/types';
import { Message } from '../../commun/message';
import { SeparateurJour } from '../../commun/separateur-jour';
import { jour, jourEnMinuscules, memeJour } from '../../textes/dates';
import { NatureEvenement, ligneDuDemandeur } from '../../textes/historique';
import { libelles } from '../../textes/libelles';
import { Besoin } from './besoin';

export type ElementFil =
  | { readonly genre: 'jour'; readonly jour: string; readonly recapitulatif: string }
  | { readonly genre: 'toi'; readonly moment: Date; readonly texte: string }
  | {
      readonly genre: 'usine';
      readonly moment: Date | null;
      readonly texte: string;
      readonly besoin: string;
      readonly fonctions: readonly string[];
    };

type MessageDuFil = Exclude<ElementFil, { genre: 'jour' }>;

export interface ContexteFil {
  readonly ajout: boolean;
  readonly fige: boolean;
  readonly vue: Date | null;
  readonly maintenant: Date;
}

const NATURES_DE_LA_REVUE: ReadonlySet<NatureEvenement> = new Set<NatureEvenement>([
  'reponses',
  'besoin-approuve',
  'besoin-refuse',
  'plan-refuse',
]);

const usine = (
  moment: Date | null,
  texte: string,
  besoin = '',
  fonctions: readonly string[] = [],
): MessageDuFil => ({ genre: 'usine', moment, texte, besoin, fonctions });

function messages(detail: DemandeDetailDto, { ajout, fige }: ContexteFil): MessageDuFil[] {
  const etape = lireEtapeAffichee(detail.etapeAffichee);
  const revue = fige || etape === 'comprendre' || etape === 'plan';
  const lignes = detail.evenementsDemandeur.flatMap(({ date, message }) => {
    const ligne = ligneDuDemandeur(message, ajout);
    return ligne !== null && (revue || !NATURES_DE_LA_REVUE.has(ligne.nature))
      ? [{ ...ligne, moment: new Date(date) }]
      : [];
  });
  const besoin =
    detail.besoin !== null && (fige || etape === 'plan')
      ? usine(null, libelles.compris.introduction(ajout), detail.besoin.resume)
      : null;
  const rangDuBesoin = lignes.map(({ nature }) => nature).lastIndexOf('besoin-approuve');
  const resultat: MessageDuFil[] = revue
    ? [{ genre: 'toi', moment: new Date(detail.creeLe), texte: detail.texte }]
    : [];
  lignes.forEach(({ nature, texte, moment }, rang) => {
    if (besoin !== null && rang === rangDuBesoin) {
      resultat.push(besoin);
    }
    if (fige && nature === 'plan-approuve' && detail.backlog !== null) {
      const fonctions = detail.backlog.stories.map(({ titre }) => titre);
      resultat.push(usine(null, libelles.fil.planValide(fonctions.length, ajout), '', fonctions));
    }
    resultat.push({ genre: 'toi', moment, texte });
  });
  const annulee = lireStatut(detail.statut) === 'Annulee';
  if (fige && annulee && !lignes.some(({ nature }) => nature === 'annulation')) {
    resultat.push(usine(new Date(detail.misAJourLe), libelles.fil.demandeAnnulee));
  }
  return resultat;
}

function premierSeparateur(
  detail: DemandeDetailDto,
  date: Date,
  { ajout, fige, maintenant }: ContexteFil,
): ElementFil {
  const etape = lireEtapeAffichee(detail.etapeAffichee);
  const suite = !fige && (etape === 'fabrication' || etape === 'pret');
  return {
    genre: 'jour',
    jour:
      ajout && !fige
        ? libelles.fil.nouvelAjout(jourEnMinuscules(date, maintenant))
        : jour(date, maintenant),
    recapitulatif: suite ? libelles.fil.recapitulatif(ajout, etape === 'pret') : '',
  };
}

function dateDuDernier(detail: DemandeDetailDto, nature: NatureEvenement): number[] {
  const dernier = detail.evenementsDemandeur
    .filter(({ message }) => ligneDuDemandeur(message, false)?.nature === nature)
    .at(-1);
  return dernier === undefined ? [] : [Date.parse(dernier.date)];
}

export function momentDuDepart(detail: DemandeDetailDto): Date {
  const departs = [...dateDuDernier(detail, 'plan-approuve'), ...dateDuDernier(detail, 'reprise')];
  return new Date(departs.length === 0 ? detail.misAJourLe : Math.max(...departs));
}

export function momentDeLAnnulation(detail: DemandeDetailDto): Date {
  return new Date(Math.max(Date.parse(detail.misAJourLe), ...dateDuDernier(detail, 'annulation')));
}

export function elementsDuFil(detail: DemandeDetailDto, contexte: ContexteFil): ElementFil[] {
  const liste = messages(detail, contexte);
  const datesDesJours = [...liste.map(({ moment }) => moment), contexte.vue];
  const resultat: ElementFil[] = [];
  let precedent: Date | null = null;
  datesDesJours.forEach((_, rang) => {
    const date = datesDesJours.slice(rang).find((suivant) => suivant !== null) ?? null;
    if (date !== null && (precedent === null || !memeJour(precedent, date))) {
      resultat.push(
        precedent === null
          ? premierSeparateur(detail, date, contexte)
          : { genre: 'jour', jour: jour(date, contexte.maintenant), recapitulatif: '' },
      );
      precedent = date;
    }
    if (rang < liste.length) {
      resultat.push(liste[rang]);
    }
  });
  return resultat;
}

@Component({
  selector: 'usine-fil',
  imports: [Besoin, Message, SeparateurJour],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (element of elements(); track $index) {
      @switch (element.genre) {
        @case ('jour') {
          <usine-separateur-jour [jour]="element.jour" [recapitulatif]="element.recapitulatif" />
        }
        @case ('toi') {
          <usine-message auteur="toi" [moment]="element.moment">
            <span class="lignes">{{ element.texte }}</span>
          </usine-message>
        }
        @case ('usine') {
          <usine-message auteur="usine" [moment]="element.moment">
            <p class="lignes">{{ element.texte }}</p>
            @if (element.besoin) {
              <usine-besoin [resume]="element.besoin" />
            }
            @if (element.fonctions.length > 0) {
              <ul>
                @for (fonction of element.fonctions; track $index) {
                  <li>{{ fonction }}</li>
                }
              </ul>
            }
          </usine-message>
        }
      }
    }
  `,
  styles: `
    :host {
      display: contents;
    }

    .lignes {
      white-space: pre-line;
    }

    ul {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding-left: 20px;
      color: var(--texte-neutre);
      font-size: 16px;
      line-height: 1.5;
    }
  `,
})
export class Fil {
  readonly elements = input.required<readonly ElementFil[]>();
}
