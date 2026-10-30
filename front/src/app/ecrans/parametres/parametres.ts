import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  computed,
  inject,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Ia, ParametresDto, lireIa } from '../../api/types';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { BoutonPlein } from '../../commun/bouton-plein';
import { Cadre } from '../../commun/cadre';
import { DialogueConfirmation } from '../../commun/dialogue-confirmation';
import { CibleFocusEcran, FocusApresAppel, focaliserSiPerdu } from '../../commun/focus-ecran';
import { EtatParametres } from '../../etat/etat-parametres';
import { libelles } from '../../textes/libelles';
import { IaAffichee, iasAffichees, modeleApresRetrait, nomIa, nomModele } from './ias';
import { PanneauAutre } from './panneau-autre';
import { PanneauCle } from './panneau-cle';
import { PanneauOllama } from './panneau-ollama';

interface Retrait {
  readonly ia: Ia;
  readonly titre: string;
  readonly phrase: string;
}

@Component({
  selector: 'usine-parametres',
  imports: [
    ActionSoulignee,
    BoutonPlein,
    Cadre,
    CibleFocusEcran,
    DialogueConfirmation,
    FocusApresAppel,
    PanneauAutre,
    PanneauCle,
    PanneauOllama,
    RouterLink,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './parametres.html',
  styleUrl: './parametres.css',
})
export class Parametres {
  private readonly parametres = inject(EtatParametres);
  private readonly injecteur = inject(Injector);
  private readonly titre = viewChild<ElementRef<HTMLElement>>('titre');
  private readonly legende = viewChild<ElementRef<HTMLElement>>('legende');

  protected readonly libelles = libelles;
  protected readonly arrivee = signal<ParametresDto | null>(null);
  protected readonly etat = computed(() => (this.arrivee() ? this.parametres.etat() : null));
  protected readonly ias = computed(() => {
    const etat = this.etat();
    return etat ? iasAffichees(etat) : [];
  });
  protected readonly selection = linkedSignal<Ia | null>(() => {
    const ia = this.arrivee()?.ia;
    return ia ? lireIa(ia) : null;
  });
  protected readonly iaChoisie = computed(
    () => this.ias().find(({ id }) => id === this.selection()) ?? null,
  );
  protected readonly bandeau = computed(() => {
    const etat = this.etat();
    const ia = this.ias().find(({ id }) => id === etat?.ia);
    const modele = ia ? nomModele(ia) : null;
    return this.arrivee()?.pret === false && etat?.pret && ia && modele
      ? { nom: nomIa(ia), modele }
      : null;
  });
  protected readonly retrait = signal<Retrait | null>(null);
  protected readonly dialogue = signal(false);
  protected readonly retraitEnCours = signal(false);
  protected readonly erreurRetrait = linkedSignal({
    source: this.parametres.actions,
    computation: () => '',
  });
  protected readonly chargement = signal(false);
  protected readonly echec = signal(false);

  constructor() {
    void this.charger();
  }

  protected async charger(): Promise<void> {
    const reprise = this.echec();
    this.chargement.set(true);
    const etat = await this.parametres.charger();
    this.chargement.set(false);
    this.echec.set(!etat);
    if (etat) {
      this.arrivee.set(etat);
      if (reprise) {
        focaliserSiPerdu(this.injecteur, () => this.titre()?.nativeElement);
      }
    }
  }

  protected selectionner(ia: Ia): void {
    this.selection.set(ia);
    this.erreurRetrait.set('');
  }

  protected etatCarte(ia: IaAffichee): string {
    const textes = libelles.parametres;
    if (ia.id === 'ollama') {
      return ia.dto.connectee ? textes.detecte : textes.introuvable;
    }
    return ia.dto.connectee ? textes.connectee : textes.cleAAjouter;
  }

  protected demanderRetrait(ia: IaAffichee): void {
    const etat = this.etat();
    if (!etat) {
      return;
    }
    const nom = nomIa(ia);
    const modele = modeleApresRetrait(etat, ia.id);
    const textes = libelles.parametres.retrait;
    this.erreurRetrait.set('');
    this.retrait.set({
      ia: ia.id,
      titre: textes.titre(nom),
      phrase: modele === null ? textes.sansAutre(nom) : textes.versAutre(nom, modele),
    });
    this.dialogue.set(true);
  }

  protected async confirmerRetrait(accord: boolean): Promise<void> {
    const retrait = this.retrait();
    if (!accord || !retrait) {
      return;
    }
    this.retraitEnCours.set(true);
    const resultat = await this.parametres.retirerCle(retrait.ia);
    this.retraitEnCours.set(false);
    if ('erreur' in resultat) {
      this.erreurRetrait.set(resultat.erreur);
      return;
    }
    focaliserSiPerdu(this.injecteur, () => this.legende()?.nativeElement);
  }
}
