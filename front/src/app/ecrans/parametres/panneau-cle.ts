import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { BoutonPlein } from '../../commun/bouton-plein';
import { FocusApresAppel, focaliserSiPerdu } from '../../commun/focus-ecran';
import { EtatParametres } from '../../etat/etat-parametres';
import { libelles } from '../../textes/libelles';
import { ChoixModele } from './choix-modele';
import { IaAffichee, nomIa } from './ias';

@Component({
  selector: 'usine-panneau-cle',
  imports: [ActionSoulignee, BoutonPlein, FocusApresAppel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './panneau-cle.html',
  styleUrl: './panneau.css',
})
export class PanneauCle {
  readonly ia = input.required<IaAffichee>();
  readonly retraitEnCours = input(false);
  readonly retirer = output();

  private readonly parametres = inject(EtatParametres);
  private readonly injecteur = inject(Injector);
  private readonly titreCle = viewChild<ElementRef<HTMLElement>>('titreCle');
  private readonly champ = viewChild<ElementRef<HTMLInputElement>>('champ');

  protected readonly libelles = libelles;
  protected readonly nom = computed(() => nomIa(this.ia()));
  protected readonly remplacement = signal(false);
  protected readonly saisie = computed(() => !this.ia().dto.connectee || this.remplacement());
  protected readonly envoi = signal(false);
  protected readonly erreurCle = signal('');
  protected readonly choix = new ChoixModele(this.ia);

  protected async enregistrer(champ: HTMLInputElement): Promise<void> {
    this.erreurCle.set('');
    this.envoi.set(true);
    const resultat = await this.parametres.enregistrerCle(this.ia().id, {
      cle: champ.value,
      service: null,
      adresse: null,
    });
    this.envoi.set(false);
    if ('erreur' in resultat) {
      this.erreurCle.set(resultat.erreur);
      focaliserSiPerdu(this.injecteur, () => champ);
      return;
    }
    champ.value = '';
    this.fermerSaisie();
  }

  protected remplacer(): void {
    this.parametres.nouvelleAction();
    this.remplacement.set(true);
    afterNextRender(() => this.champ()?.nativeElement.focus(), { injector: this.injecteur });
  }

  protected fermerSaisie(): void {
    this.remplacement.set(false);
    this.erreurCle.set('');
    focaliserSiPerdu(this.injecteur, () => this.titreCle()?.nativeElement);
  }
}
