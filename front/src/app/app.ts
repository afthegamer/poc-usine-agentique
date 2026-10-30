import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterOutlet } from '@angular/router';
import { Acces } from './api/acces';
import { FocusEcran } from './commun/focus-ecran';
import { PhraseAttente } from './commun/phrase-attente';
import { AccesRefuse } from './ecrans/acces/acces-refuse';
import { EtatUsine } from './etat/etat-usine';
import { FluxDemandes } from './etat/flux-demandes';
import { libelles } from './textes/libelles';

@Component({
  imports: [RouterOutlet, AccesRefuse, PhraseAttente],
  selector: 'usine-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly acces = inject(Acces);
  protected readonly etat = inject(EtatUsine);
  protected readonly libelles = libelles;

  constructor() {
    const titre = inject(Title);
    const flux = inject(FluxDemandes);
    inject(FocusEcran).demarrer();
    effect(() => titre.setTitle(libelles.titreOnglet(this.etat.attendUtilisateur())));
    effect(() => {
      if (this.etat.charge()) {
        untracked(() => flux.demarrer());
      }
    });
    this.etat.demarrer();
  }
}
