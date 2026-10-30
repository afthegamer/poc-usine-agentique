import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
} from '@angular/core';
import {
  DemandeDetailDto,
  SousEtapeFabrication,
  lireEtapeAffichee,
  lireSousEtapeFabrication,
} from '../../../api/types';
import { BarresEtapes } from '../../../commun/barres-etapes';
import { Message } from '../../../commun/message';
import { libelles } from '../../../textes/libelles';

const RANG_DES_SOUS_ETAPES: Readonly<Record<SousEtapeFabrication, number>> = {
  'preparation-verifications': 0,
  fabrication: 1,
  verifications: 2,
};

const textes = libelles.fabrication;

@Component({
  selector: 'usine-fabrication',
  imports: [BarresEtapes, Message],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-message auteur="usine" [moment]="moment()">
      <p>{{ textes.depart(ajout()) }}</p>
      <usine-barres-etapes [etapes]="sousEtapes()" [courante]="courante()" encadree />
    </usine-message>
  `,
})
export class Fabrication {
  readonly detail = input.required<DemandeDetailDto>();
  readonly ajout = input(false, { transform: booleanAttribute });
  readonly moment = input<Date | null>(null);

  protected readonly textes = textes;
  protected readonly courante = computed(() => {
    const { sousEtapeFabrication, etapeAffichee } = this.detail();
    const sousEtape =
      sousEtapeFabrication === null ? null : lireSousEtapeFabrication(sousEtapeFabrication);
    if (sousEtape !== null) {
      return RANG_DES_SOUS_ETAPES[sousEtape];
    }
    return lireEtapeAffichee(etapeAffichee) === 'pret' ? textes.sousEtapes.length : null;
  });
  protected readonly sousEtapes = computed(() => {
    const courante = this.courante();
    return textes.sousEtapes.map(({ faite, enCours, aVenir }, rang) => {
      const libelle =
        courante !== null && rang < courante ? faite : rang === courante ? enCours : aVenir;
      return this.ajout() && rang === textes.sousEtapes.length - 1
        ? textes.verificationsAjout(libelle)
        : libelle;
    });
  });
}
