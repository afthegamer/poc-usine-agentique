import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ClientUsine } from '../../api/client-usine';
import { demandeCreeeSansWorker, texteErreur } from '../../api/erreurs';
import { DemandeDetailDto } from '../../api/types';
import { commandesPossibles } from '../../commandes/commandes';
import { BoutonPlein } from '../../commun/bouton-plein';
import { CibleFocusEcran } from '../../commun/focus-ecran';
import { Message } from '../../commun/message';
import { ZoneSaisie } from '../../commun/zone-saisie';
import { DetailsDemandes } from '../../etat/details-demandes';
import { EtatParametres } from '../../etat/etat-parametres';
import { EtatUsine } from '../../etat/etat-usine';
import { MessageDePassage, Passage } from '../../etat/message-de-passage';
import { libelles } from '../../textes/libelles';
import { Disposition } from '../conversation/disposition';
import { PanneauProjet } from '../conversation/panneau-projet';

@Component({
  selector: 'usine-accueil',
  imports: [
    BoutonPlein,
    CibleFocusEcran,
    Disposition,
    Message,
    PanneauProjet,
    RouterLink,
    ZoneSaisie,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './accueil.html',
  styleUrl: './accueil.css',
})
export class Accueil {
  private readonly etat = inject(EtatUsine);
  private readonly client = inject(ClientUsine);
  private readonly details = inject(DetailsDemandes);
  private readonly router = inject(Router);
  private readonly destruction = inject(DestroyRef);
  private readonly parametres = inject(EtatParametres);

  protected readonly libelles = libelles;
  protected readonly passage = signal<Passage | null>(inject(MessageDePassage).prendre());
  protected readonly information = computed(() => this.passage()?.nature === 'information');
  protected readonly texte = signal('');
  protected readonly commandes = commandesPossibles({
    ecran: 'accueil',
    projetPublie: false,
    situation: null,
    ajout: false,
  });
  protected readonly erreur = signal('');
  protected readonly sansIa = computed(
    () => !this.parametres.echec() && this.parametres.etat()?.pret === false,
  );
  protected readonly occupe = signal(false);

  constructor() {
    void this.parametres.charger();
  }

  protected saisir(texte: string): void {
    this.texte.set(texte);
    this.erreur.set('');
    this.passage.set(null);
  }

  protected commencer(texte: string): void {
    if (this.sansIa()) {
      return;
    }
    this.erreur.set('');
    this.occupe.set(true);
    this.client.lancer({ texte }).subscribe({
      next: (detail) => this.surCreation(detail),
      error: (erreur: unknown) => this.surEchec(erreur),
    });
  }

  private surCreation(detail: DemandeDetailDto): void {
    this.occupe.set(false);
    this.details.recevoirCreation(detail);
    if (!this.destruction.destroyed) {
      void this.router.navigate(['/demandes', detail.id]);
    }
  }

  private surEchec(erreur: unknown): void {
    this.occupe.set(false);
    const reponse = erreur instanceof HttpErrorResponse ? erreur : null;
    this.erreur.set(reponse === null ? libelles.erreurs.probleme : texteErreur('demande', reponse));
    if (reponse !== null && demandeCreeeSansWorker(reponse)) {
      this.texte.set('');
      this.etat.relireDemandes();
    }
  }
}
