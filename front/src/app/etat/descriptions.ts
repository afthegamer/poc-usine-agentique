import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { DemandeDetailDto } from '../api/types';
import { DetailsDemandes } from './details-demandes';
import { ProjetAffiche } from './etat-usine';

const demandeDOrigine = (element: ProjetAffiche): string | undefined => element.demandes.at(-1)?.id;

@Injectable({ providedIn: 'root' })
export class Descriptions {
  private readonly details = inject(DetailsDemandes);
  private readonly lectures = signal<ReadonlyMap<string, Signal<DemandeDetailDto | null>>>(
    new Map(),
  );
  private readonly textes = computed<ReadonlyMap<string, string>>(
    () =>
      new Map(
        [...this.lectures()].map(([id, detail]): [string, string] => [id, detail()?.texte ?? '']),
      ),
  );

  charger(elements: readonly ProjetAffiche[]): void {
    const nouveaux = elements
      .flatMap((element) => demandeDOrigine(element) ?? [])
      .filter((id) => !this.lectures().has(id));
    if (nouveaux.length === 0) {
      return;
    }
    const lectures = new Map(this.lectures());
    for (const id of nouveaux) {
      lectures.set(id, this.details.detail(id));
      this.details.oublier(id);
    }
    this.lectures.set(lectures);
  }

  description(element: ProjetAffiche | undefined): string {
    const id = element && demandeDOrigine(element);
    return id === undefined ? '' : (this.textes().get(id) ?? '');
  }
}
