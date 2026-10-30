import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { ModeleDto } from '../../api/types';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { BoutonPlein } from '../../commun/bouton-plein';
import { FocusApresAppel, focaliserSiPerdu } from '../../commun/focus-ecran';
import { EtatParametres } from '../../etat/etat-parametres';
import { libelles } from '../../textes/libelles';
import { ChoixModele } from './choix-modele';
import { IaAffichee } from './ias';

@Component({
  selector: 'usine-panneau-ollama',
  imports: [ActionSoulignee, BoutonPlein, FocusApresAppel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './panneau-ollama.html',
  styleUrl: './panneau.css',
})
export class PanneauOllama {
  readonly ia = input.required<IaAffichee>();

  private readonly parametres = inject(EtatParametres);
  private readonly injecteur = inject(Injector);
  private readonly champ = viewChild<ElementRef<HTMLInputElement>>('champ');
  private readonly changer = viewChild('changer', { read: ElementRef<HTMLElement> });

  protected readonly libelles = libelles;
  protected readonly changement = signal(false);
  protected readonly envoi = signal(false);
  protected readonly erreurAdresse = signal('');
  protected readonly choix = new ChoixModele(this.ia);

  protected ouvrirChangement(): void {
    this.changement.set(true);
    afterNextRender(() => this.champ()?.nativeElement.focus(), { injector: this.injecteur });
  }

  protected fermerChangement(): void {
    this.changement.set(false);
    this.erreurAdresse.set('');
    focaliserSiPerdu(this.injecteur, () => this.changer()?.nativeElement);
  }

  protected preparerDeNouveau(modele: ModeleDto): void {
    if (modele.id === this.choix.choisi() && modele.prepare === false) {
      void this.choix.choisir(modele.id);
    }
  }

  protected async enregistrer(champ: HTMLInputElement): Promise<void> {
    this.erreurAdresse.set('');
    this.envoi.set(true);
    const resultat = await this.parametres.reglerOllama(champ.value);
    this.envoi.set(false);
    if ('erreur' in resultat) {
      this.erreurAdresse.set(resultat.erreur);
      focaliserSiPerdu(this.injecteur, () => champ);
      return;
    }
    this.fermerChangement();
  }
}
