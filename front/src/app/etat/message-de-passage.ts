import { Injectable } from '@angular/core';

export type NaturePassage = 'erreur' | 'information';

export interface Passage {
  readonly texte: string;
  readonly nature: NaturePassage;
}

@Injectable({ providedIn: 'root' })
export class MessageDePassage {
  private passage: Passage | null = null;

  deposer(texte: string, nature: NaturePassage = 'erreur'): void {
    this.passage = { texte, nature };
  }

  prendre(): Passage | null {
    const passage = this.passage;
    this.passage = null;
    return passage;
  }
}
