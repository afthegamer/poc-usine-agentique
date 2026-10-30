import { Signal, computed, inject, linkedSignal, signal } from '@angular/core';
import { EtatParametres } from '../../etat/etat-parametres';
import { IaAffichee } from './ias';

export class ChoixModele {
  private readonly parametres = inject(EtatParametres);

  readonly enCours = signal<string | null>(null);
  readonly erreur = linkedSignal({ source: this.parametres.actions, computation: () => '' });
  readonly choisi: Signal<string | null>;

  constructor(private readonly ia: Signal<IaAffichee>) {
    this.choisi = computed(() => {
      const { id, dto } = this.ia();
      const generale = dto.connectee && this.parametres.etat()?.ia === id;
      return this.enCours() ?? (generale ? dto.modele : null);
    });
  }

  async choisir(modele: string): Promise<void> {
    this.enCours.set(modele);
    const resultat = await this.parametres.choisir({ ia: this.ia().id, modele });
    this.enCours.set(null);
    if ('erreur' in resultat) {
      this.erreur.set(resultat.erreur);
    }
  }
}
