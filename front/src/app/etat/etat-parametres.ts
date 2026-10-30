import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { ClientUsine } from '../api/client-usine';
import { texteErreur } from '../api/erreurs';
import { ChoixDto, CleDto, Ia, ModeleDto, ParametresDto, RolesDto } from '../api/types';
import { libelles } from '../textes/libelles';

export type Resultat<T> = { readonly valeur: T } | { readonly erreur: string };

async function appeler<T>(appel: Observable<T>): Promise<Resultat<T>> {
  try {
    return { valeur: await firstValueFrom(appel) };
  } catch (erreur: unknown) {
    return {
      erreur:
        erreur instanceof HttpErrorResponse
          ? texteErreur('parametres', erreur)
          : libelles.erreurs.probleme,
    };
  }
}

@Injectable({ providedIn: 'root' })
export class EtatParametres {
  private readonly client = inject(ClientUsine);

  readonly etat = signal<ParametresDto | null>(null);
  readonly echec = signal(false);
  readonly actions = signal(0);

  async charger(): Promise<ParametresDto | null> {
    try {
      const etat = await firstValueFrom(this.client.parametres());
      this.etat.set(etat);
      this.echec.set(false);
      return etat;
    } catch {
      this.echec.set(true);
      return null;
    }
  }

  enregistrerCle(ia: Ia, corps: CleDto): Promise<Resultat<ParametresDto>> {
    return this.agir(this.client.enregistrerCle(ia, corps));
  }

  retirerCle(ia: Ia): Promise<Resultat<ParametresDto>> {
    return this.agir(this.client.retirerCle(ia));
  }

  choisir(choix: ChoixDto): Promise<Resultat<ParametresDto>> {
    return this.agir(this.client.choisir(choix));
  }

  reglerRoles(roles: RolesDto): Promise<Resultat<ParametresDto>> {
    return this.agir(this.client.reglerRoles(roles));
  }

  reglerOllama(adresse: string): Promise<Resultat<ParametresDto>> {
    return this.agir(this.client.reglerOllama({ adresse }));
  }

  modelesAutre(): Promise<Resultat<ModeleDto[]>> {
    return appeler(this.client.modelesAutre());
  }

  nouvelleAction(): void {
    this.actions.update((nombre) => nombre + 1);
  }

  private async agir(appel: Observable<ParametresDto>): Promise<Resultat<ParametresDto>> {
    this.nouvelleAction();
    const resultat = await appeler(appel);
    if ('valeur' in resultat) {
      this.etat.set(resultat.valeur);
    }
    return resultat;
  }
}
