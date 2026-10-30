import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { Acces } from '../api/acces';
import { ClientUsine } from '../api/client-usine';
import { DemandeDto, ProjetDto } from '../api/types';
import { Situation, estEnCours, situation } from './situation';

export type Connexion = 'connecte' | 'reconnexion' | 'acces-refuse';

export interface ProjetAffiche {
  readonly cle: string;
  readonly titre: string;
  readonly demandes: readonly DemandeDto[];
  readonly lien: string;
  readonly situation: Situation;
}

const DELAIS_RECONNEXION = [2000, 4000, 8000, 16000, 30000];

export function delaiReconnexion(essai: number): number {
  return DELAIS_RECONNEXION[Math.min(essai, DELAIS_RECONNEXION.length - 1)];
}

export function estAccesRefuse(erreur: unknown): boolean {
  return erreur instanceof HttpErrorResponse && erreur.status === HttpStatusCode.Unauthorized;
}

@Injectable({ providedIn: 'root' })
export class EtatUsine {
  private readonly client = inject(ClientUsine);
  private readonly acces = inject(Acces);

  private readonly lesDemandes = signal<ReadonlyMap<string, DemandeDto>>(new Map());
  private readonly lOrdre = signal<readonly string[]>([]);
  private readonly lesProjets = signal<readonly ProjetDto[]>([]);
  private readonly lesAnnulations = signal<ReadonlySet<string>>(new Set());
  private readonly lesEssaisArretes = signal<ReadonlyMap<string, number>>(new Map());
  private readonly etatConnexion = signal<'connecte' | 'reconnexion'>('connecte');
  private readonly demandesLues = signal(false);
  private readonly projetsLus = signal(false);

  readonly demandes = this.lesDemandes.asReadonly();
  readonly ordreDemandes = this.lOrdre.asReadonly();
  readonly projets = this.lesProjets.asReadonly();
  readonly annulationsDemandees = this.lesAnnulations.asReadonly();
  readonly essaisArretes = this.lesEssaisArretes.asReadonly();
  readonly charge = computed(() => this.demandesLues() && this.projetsLus());
  readonly connexion = computed<Connexion>(() =>
    this.acces.refuse() ? 'acces-refuse' : this.etatConnexion(),
  );
  readonly attendUtilisateur = computed(() =>
    [...this.lesDemandes().values()].some((demande) => demande.attente !== null),
  );

  readonly projetsAffiches = computed<readonly ProjetAffiche[]>(() => {
    if (!this.charge()) {
      return [];
    }
    const demandes = this.lOrdre().flatMap((id) => this.lesDemandes().get(id) ?? []);
    const noms = new Set(this.lesProjets().map((projet) => projet.nom));
    const situationDe = (demande: DemandeDto) =>
      situation(demande, {
        annulationDemandee: this.lesAnnulations().has(demande.id),
        projetPublie: demande.projet !== null && noms.has(demande.projet),
        estCreation: demande.projet === null || this.estCreation(demande),
        livraison: undefined,
      });
    const projets = this.lesProjets().map((projet) => {
      const siennes = demandes.filter((demande) => demande.projet === projet.nom);
      const recente = siennes.find((demande) => demande.statut !== 'Annulee');
      const enCours = recente === undefined ? null : situationDe(recente);
      return {
        element: {
          cle: `projet:${projet.nom}`,
          titre: projet.titre,
          demandes: siennes,
          lien: `/projets/${projet.nom}`,
          situation: enCours !== null && estEnCours(enCours) ? enCours : 'pret',
        },
        activite: siennes.length
          ? Math.max(...siennes.map((demande) => Date.parse(demande.misAJourLe)))
          : Date.parse(projet.derniereMiseAJour),
      };
    });
    const creations = demandes
      .filter(
        (demande) =>
          (demande.projet === null || !noms.has(demande.projet)) && demande.statut !== 'Annulee',
      )
      .map((demande) => ({
        element: {
          cle: `demande:${demande.id}`,
          titre: demande.titre,
          demandes: [demande],
          lien: `/demandes/${demande.id}`,
          situation: situationDe(demande),
        },
        activite: Date.parse(demande.misAJourLe),
      }));
    return [...projets, ...creations]
      .sort((premier, second) => second.activite - premier.activite)
      .map(({ element }) => element);
  });

  demarrer(): void {
    this.lire(this.client.demandes(), (demandes) => {
      this.lesDemandes.set(new Map(demandes.map((demande) => [demande.id, demande])));
      this.lOrdre.set(demandes.map((demande) => demande.id));
      this.demandesLues.set(true);
    });
    this.lire(this.client.projets(), (projets) => {
      this.lesProjets.set(projets);
      this.projetsLus.set(true);
    });
  }

  definirConnexion(connexion: 'connecte' | 'reconnexion'): void {
    this.etatConnexion.set(connexion);
  }

  noterAnnulationDemandee(id: string): void {
    this.lesAnnulations.update((ids) => new Set(ids).add(id));
  }

  recevoir(demande: DemandeDto): void {
    const avant = this.lesDemandes().get(demande.id);
    this.lesDemandes.update((demandes) => new Map(demandes).set(demande.id, demande));
    if (!avant) {
      this.lOrdre.update((ordre) => [demande.id, ...ordre]);
    }
    const passeATerminee = demande.statut === 'Terminee' && avant?.statut !== 'Terminee';
    const projetInconnu =
      demande.projet !== null && !this.lesProjets().some(({ nom }) => nom === demande.projet);
    if (passeATerminee || projetInconnu) {
      this.relireProjets();
    }
    if (passeATerminee && demande.projet !== null && !this.estCreation(demande)) {
      this.arreterEssai(demande.projet);
    }
  }

  relireDemandes(): void {
    if (!this.charge()) {
      return;
    }
    this.client.demandes().subscribe({
      next: (demandes) => [...demandes].reverse().forEach((demande) => this.recevoir(demande)),
      error: () => undefined,
    });
  }

  relireProjets(): void {
    this.client.projets().subscribe({
      next: (projets) => this.lesProjets.set(projets),
      error: () => undefined,
    });
  }

  private estCreation(demande: DemandeDto): boolean {
    const siennes = this.lOrdre().filter(
      (id) => this.lesDemandes().get(id)?.projet === demande.projet,
    );
    return siennes.at(-1) === demande.id;
  }

  private arreterEssai(projet: string): void {
    this.client.arreterEssai(projet).subscribe({
      next: () =>
        this.lesEssaisArretes.update((arrets) =>
          new Map(arrets).set(projet, (arrets.get(projet) ?? 0) + 1),
        ),
      error: (erreur: unknown) => console.warn("Arrêt de l'essai", projet, erreur),
    });
  }

  private lire<T>(lecture: Observable<T>, appliquer: (valeur: T) => void, essai = 0): void {
    lecture.subscribe({
      next: appliquer,
      error: (erreur: unknown) => {
        if (estAccesRefuse(erreur)) {
          return;
        }
        this.etatConnexion.set('reconnexion');
        setTimeout(() => this.lire(lecture, appliquer, essai + 1), delaiReconnexion(essai));
      },
    });
  }
}
