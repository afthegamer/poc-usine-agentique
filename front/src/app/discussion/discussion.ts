import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { DOCUMENT, DestroyRef, Injectable, effect, inject, signal } from '@angular/core';
import { Acces } from '../api/acces';
import { ClientUsine } from '../api/client-usine';
import { Action, demandeDisparue, texteErreur } from '../api/erreurs';
import { LancementDiscussionDto } from '../api/types';
import { estAccesRefuse } from '../etat/etat-usine';
import { FABRIQUE_EVENT_SOURCE, SourceDeFlux } from '../etat/flux-demandes';
import { HORLOGE } from '../etat/horloge';
import { libelles } from '../textes/libelles';
import { APPELS_CONSERVES, SANS_REPONSE, question } from './evenements-discussion';

export type ElementDiscussion =
  | { readonly genre: 'toi' | 'usine'; readonly moment: Date; readonly texte: string }
  | { readonly genre: 'nouvelle' };

export interface Confirmation {
  readonly rang: number;
  readonly envoi: boolean;
  readonly erreur: string;
}

export interface SuitesEnvoi {
  readonly envoye: () => void;
  readonly disparu: () => void;
}

interface Envoi {
  readonly texte: string;
  readonly corps: LancementDiscussionDto;
  readonly moment: Date;
  readonly suites: SuitesEnvoi;
  readonly generation: number;
}

const textes = libelles.discussion;

function texteDe(action: Action, erreur: unknown): string {
  return erreur instanceof HttpErrorResponse
    ? texteErreur(action, erreur)
    : libelles.erreurs.discussionImpossible;
}

const statutDe = (erreur: unknown) => (erreur instanceof HttpErrorResponse ? erreur.status : null);

@Injectable()
export class Discussion {
  private readonly client = inject(ClientUsine);
  private readonly acces = inject(Acces);
  private readonly fabrique = inject(FABRIQUE_EVENT_SOURCE);
  private readonly horloge = inject(HORLOGE);

  private readonly lesElements = signal<readonly ElementDiscussion[]>([]);
  private readonly laConfirmation = signal<Confirmation | null>(null);
  private readonly lIndicateur = signal('');
  private readonly lOccupation = signal(false);
  private readonly lErreur = signal('');

  readonly elements = this.lesElements.asReadonly();
  readonly confirmation = this.laConfirmation.asReadonly();
  readonly indicateur = this.lIndicateur.asReadonly();
  readonly occupee = this.lOccupation.asReadonly();
  readonly erreur = this.lErreur.asReadonly();

  private id: string | null = null;
  private avecProjet = false;
  private terminee = false;
  private source: SourceDeFlux | null = null;
  private generation = 0;

  constructor() {
    const fenetre = inject(DOCUMENT).defaultView;
    const surDepart = () => {
      if (this.id !== null) {
        this.recommencer(true);
      }
    };
    fenetre?.addEventListener('pagehide', surDepart);
    inject(DestroyRef).onDestroy(() => {
      fenetre?.removeEventListener('pagehide', surDepart);
      this.repartir();
    });
    effect(() => {
      if (this.acces.refuse()) {
        this.fermerLeFlux();
      }
    });
  }

  repartir(): void {
    this.generation++;
    this.oublier(true);
    this.lesElements.set([]);
    this.laConfirmation.set(null);
    this.lIndicateur.set('');
    this.lOccupation.set(false);
    this.lErreur.set('');
  }

  oublierLErreur(): void {
    this.lErreur.set('');
  }

  envoyer(texte: string, corps: LancementDiscussionDto, suites: SuitesEnvoi): void {
    if (this.lOccupation()) {
      return;
    }
    this.lOccupation.set(true);
    this.lErreur.set('');
    const envoi = { texte, corps, suites, moment: this.horloge(), generation: this.generation };
    if (this.terminee || (this.id !== null && !this.avecProjet && corps.projet)) {
      this.recommencer(true);
    }
    if (this.id === null) {
      this.ouvrir(envoi, false);
    } else {
      this.transmettre(this.id, envoi, false);
    }
  }

  confirmer(accord: boolean): void {
    const attente = this.laConfirmation();
    const id = this.id;
    if (attente === null || id === null) {
      return;
    }
    const envoyee = { ...attente, envoi: true, erreur: '' };
    const toujoursLaMeme = () => id === this.id && this.laConfirmation() === envoyee;
    this.laConfirmation.set(envoyee);
    this.lErreur.set('');
    this.client.confirmer(id, accord).subscribe({
      next: () => {
        if (toujoursLaMeme()) {
          this.laConfirmation.set(null);
          this.lIndicateur.set(accord ? textes.sEnOccupe : textes.reflechit);
        }
      },
      error: (erreur: unknown) => {
        if (!toujoursLaMeme()) {
          return;
        }
        const texte = texteDe('discussion', erreur);
        const statut = statutDe(erreur);
        if (statut === HttpStatusCode.NotFound) {
          this.recommencer(false);
        } else if (statut === HttpStatusCode.Conflict && texte === '') {
          this.laConfirmation.set(null);
        } else {
          this.laConfirmation.set({
            ...attente,
            envoi: false,
            erreur:
              statut === HttpStatusCode.Unauthorized
                ? ''
                : texte || libelles.erreurs.discussionImpossible,
          });
        }
      },
    });
  }

  private ouvrir(envoi: Envoi, renvoi: boolean): void {
    this.client.ouvrirDiscussion(envoi.corps).subscribe({
      next: ({ id }) => {
        if (envoi.generation !== this.generation) {
          this.supprimer(id);
          return;
        }
        this.id = id;
        this.avecProjet = Boolean(envoi.corps.projet);
        this.ecouter(id);
        this.transmettre(id, envoi, renvoi);
      },
      error: (erreur: unknown) => {
        if (envoi.generation !== this.generation) {
          return;
        }
        this.lOccupation.set(false);
        const texte = texteDe('ouverture-discussion', erreur);
        const disparue =
          erreur instanceof HttpErrorResponse && demandeDisparue('ouverture-discussion', erreur);
        if (disparue) {
          envoi.suites.disparu();
        } else {
          this.lErreur.set(texte);
        }
      },
    });
  }

  private transmettre(id: string, envoi: Envoi, renvoi: boolean): void {
    this.client.envoyerMessage(id, envoi.texte).subscribe({
      next: () => {
        if (envoi.generation !== this.generation) {
          return;
        }
        this.lOccupation.set(false);
        if (id === this.id) {
          this.ajouter({ genre: 'toi', moment: envoi.moment, texte: envoi.texte });
          this.lIndicateur.set(textes.reflechit);
          envoi.suites.envoye();
        }
      },
      error: (erreur: unknown) => {
        if (envoi.generation !== this.generation) {
          return;
        }
        if (id !== this.id) {
          this.lOccupation.set(false);
          return;
        }
        const texte = texteDe('discussion', erreur);
        const statut = statutDe(erreur);
        const accesRefuse = statut === HttpStatusCode.Unauthorized;
        if (!renvoi && !accesRefuse && texte === '') {
          this.recommencer(statut !== HttpStatusCode.NotFound);
          this.ouvrir(envoi, true);
          return;
        }
        this.lOccupation.set(false);
        this.lErreur.set(accesRefuse ? '' : texte || libelles.erreurs.discussionImpossible);
      },
    });
  }

  private ecouter(id: string): void {
    const source = this.fabrique(
      this.acces.adresseFlux(`/api/discussions/${encodeURIComponent(id)}/flux`),
    );
    this.source = source;
    const sur = (type: string, traiter: (texte: string) => void) =>
      source.addEventListener(type, ({ data }) => {
        if (typeof data === 'string') {
          traiter(data);
        }
      });
    sur('message', (texte) => this.recevoir(texte));
    sur('outil', () => this.lIndicateur.set(textes.sEnOccupe));
    sur('erreur', (texte) => {
      console.warn('Discussion', texte);
      this.repondre(textes.erreur);
    });
    sur('confirmation', (texte) => {
      this.repondre(question(texte));
      this.laConfirmation.set({ rang: this.lesElements().length - 1, envoi: false, erreur: '' });
    });
    sur('fin', () => {
      this.oublier(true);
      this.terminee = true;
      this.laConfirmation.set(null);
      this.lIndicateur.set('');
    });
    source.addEventListener('error', () => this.surCoupure(source));
  }

  private recevoir(texte: string): void {
    if (!texte.startsWith(APPELS_CONSERVES)) {
      this.repondre(texte === SANS_REPONSE ? textes.sansReponse : texte);
    }
  }

  private repondre(texte: string): void {
    this.ajouter({ genre: 'usine', moment: this.horloge(), texte });
    this.lIndicateur.set('');
  }

  private surCoupure(source: SourceDeFlux): void {
    if (source.readyState !== source.CLOSED) {
      return;
    }
    const toujoursLaMeme = () => source === this.source;
    this.client.sante().subscribe({
      next: () => {
        if (toujoursLaMeme()) {
          this.recommencer(true);
        }
      },
      error: (erreur: unknown) => {
        if (toujoursLaMeme() && !estAccesRefuse(erreur)) {
          this.recommencer(true);
        }
      },
    });
  }

  private recommencer(supprimer: boolean): void {
    this.oublier(supprimer);
    this.laConfirmation.set(null);
    this.lIndicateur.set('');
    this.ajouter({ genre: 'nouvelle' });
  }

  private oublier(supprimer: boolean): void {
    const id = this.id;
    this.fermerLeFlux();
    this.id = null;
    this.terminee = false;
    if (supprimer && id !== null) {
      this.supprimer(id);
    }
  }

  private fermerLeFlux(): void {
    this.source?.close();
    this.source = null;
  }

  private supprimer(id: string): void {
    this.client
      .fermerDiscussion(id)
      .catch((erreur: unknown) => console.warn('Fermeture de la discussion', id, erreur));
  }

  private ajouter(element: ElementDiscussion): void {
    this.lesElements.update((elements) => [...elements, element]);
  }
}
