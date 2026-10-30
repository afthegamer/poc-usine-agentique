import {
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpResponse,
  HttpStatusCode,
} from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { tap } from 'rxjs';

const CLE_JETON = 'usine.jeton';
const PARAMETRE_JETON = 'jeton';
const CHEMIN_USINE = /^\/(api(\/|\?|$)|openapi\/)/;

function sansErreur<T>(action: () => T, repli: T): T {
  try {
    return action();
  } catch {
    return repli;
  }
}

@Injectable({ providedIn: 'root' })
export class Acces {
  private readonly jetonConnu = signal<string | null>(null);
  private readonly accesRefuse = signal(false);
  private jetonARanger: string | null = null;

  readonly jeton = this.jetonConnu.asReadonly();
  readonly refuse = this.accesRefuse.asReadonly();

  demarrer(): void {
    const adresse = new URL(location.href);
    const recu = adresse.searchParams.get(PARAMETRE_JETON);
    if (recu !== null) {
      adresse.searchParams.delete(PARAMETRE_JETON);
      history.replaceState(history.state, '', adresse.pathname + adresse.search + adresse.hash);
    }
    if (recu) {
      this.jetonARanger = recu;
      this.jetonConnu.set(recu);
    } else {
      this.jetonConnu.set(sansErreur(() => localStorage.getItem(CLE_JETON), null));
    }
  }

  adresseFlux(chemin: string): string {
    const jeton = this.jetonConnu();
    return jeton === null ? chemin : `${chemin}?${PARAMETRE_JETON}=${encodeURIComponent(jeton)}`;
  }

  ranger(envoye: string): void {
    if (envoye === this.jetonARanger) {
      sansErreur(() => localStorage.setItem(CLE_JETON, envoye), undefined);
      this.jetonARanger = null;
    }
  }

  refuser(): void {
    const envoye = this.jetonConnu();
    sansErreur(() => {
      if (localStorage.getItem(CLE_JETON) === envoye) {
        localStorage.removeItem(CLE_JETON);
      }
    }, undefined);
    this.jetonConnu.set(null);
    this.accesRefuse.set(true);
  }
}

export const intercepteurAcces: HttpInterceptorFn = (requete, suivant) => {
  const acces = inject(Acces);
  const jeton = acces.jeton();
  const envoye = jeton !== null && CHEMIN_USINE.test(requete.url) ? jeton : null;
  const envoyee =
    envoye === null
      ? requete
      : requete.clone({ setHeaders: { Authorization: `Bearer ${envoye}` } });
  return suivant(envoyee).pipe(
    tap({
      next: (evenement) => {
        if (envoye !== null && evenement instanceof HttpResponse) {
          acces.ranger(envoye);
        }
      },
      error: (erreur: unknown) => {
        if (erreur instanceof HttpErrorResponse && erreur.status === HttpStatusCode.Unauthorized) {
          acces.refuser();
        }
      },
    }),
  );
};
