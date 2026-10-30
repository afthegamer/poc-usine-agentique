import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Acces } from './acces';
import {
  ChoixDto,
  CleDto,
  ConfirmationDto,
  DemandeDetailDto,
  DemandeDto,
  DiscussionDto,
  EssaiDto,
  LancementDiscussionDto,
  LancementDto,
  MessageDto,
  ModeleDto,
  OllamaDto,
  ParametresDto,
  ProjetDto,
  ReponsesDto,
  RolesDto,
  ValidationDto,
} from './types';

const demande = (id: string) => `/api/demandes/${encodeURIComponent(id)}`;
const essai = (nom: string) => `/api/projets/${encodeURIComponent(nom)}/essai`;
const discussion = (id: string) => `/api/discussions/${encodeURIComponent(id)}`;
const cle = (ia: string) => `/api/parametres/cles/${encodeURIComponent(ia)}`;

@Injectable({ providedIn: 'root' })
export class ClientUsine {
  private readonly http = inject(HttpClient);
  private readonly acces = inject(Acces);

  projets(): Observable<ProjetDto[]> {
    return this.http.get<ProjetDto[]>('/api/projets');
  }

  demandes(): Observable<DemandeDto[]> {
    return this.http.get<DemandeDto[]>('/api/demandes');
  }

  detail(id: string): Observable<DemandeDetailDto> {
    return this.http.get<DemandeDetailDto>(demande(id));
  }

  lancer(corps: LancementDto): Observable<DemandeDetailDto> {
    return this.http.post<DemandeDetailDto>('/api/demandes', corps);
  }

  repondre(id: string, corps: ReponsesDto): Observable<DemandeDetailDto> {
    return this.http.post<DemandeDetailDto>(`${demande(id)}/reponses`, corps);
  }

  valider(id: string, corps: ValidationDto): Observable<DemandeDetailDto> {
    return this.http.post<DemandeDetailDto>(`${demande(id)}/validation`, corps);
  }

  annuler(id: string): Observable<DemandeDetailDto> {
    return this.http.post<DemandeDetailDto>(`${demande(id)}/annulation`, null);
  }

  reprendre(id: string): Observable<DemandeDetailDto> {
    return this.http.post<DemandeDetailDto>(`${demande(id)}/reprise`, null);
  }

  essai(nom: string): Observable<EssaiDto> {
    return this.http.get<EssaiDto>(essai(nom));
  }

  lancerEssai(nom: string): Observable<EssaiDto> {
    return this.http.post<EssaiDto>(essai(nom), null);
  }

  arreterEssai(nom: string): Observable<EssaiDto> {
    return this.http.delete<EssaiDto>(essai(nom));
  }

  ouvrirDiscussion(corps: LancementDiscussionDto): Observable<DiscussionDto> {
    return this.http.post<DiscussionDto>('/api/discussions', corps);
  }

  envoyerMessage(id: string, texte: string): Observable<void> {
    return this.http.post<void>(`${discussion(id)}/messages`, { texte } satisfies MessageDto);
  }

  confirmer(id: string, accord: boolean): Observable<void> {
    return this.http.post<void>(`${discussion(id)}/confirmation`, {
      accord,
    } satisfies ConfirmationDto);
  }

  fermerDiscussion(id: string): Promise<Response> {
    const jeton = this.acces.jeton();
    return fetch(discussion(id), {
      method: 'DELETE',
      keepalive: true,
      headers: jeton === null ? {} : { Authorization: `Bearer ${jeton}` },
    });
  }

  parametres(): Observable<ParametresDto> {
    return this.http.get<ParametresDto>('/api/parametres');
  }

  enregistrerCle(ia: string, corps: CleDto): Observable<ParametresDto> {
    return this.http.put<ParametresDto>(cle(ia), corps);
  }

  retirerCle(ia: string): Observable<ParametresDto> {
    return this.http.delete<ParametresDto>(cle(ia));
  }

  choisir(corps: ChoixDto): Observable<ParametresDto> {
    return this.http.put<ParametresDto>('/api/parametres/choix', corps);
  }

  modelesAutre(): Observable<ModeleDto[]> {
    return this.http.get<ModeleDto[]>('/api/parametres/autre/modeles');
  }

  reglerRoles(corps: RolesDto): Observable<ParametresDto> {
    return this.http.put<ParametresDto>('/api/parametres/roles', corps);
  }

  reglerOllama(corps: OllamaDto): Observable<ParametresDto> {
    return this.http.put<ParametresDto>('/api/parametres/ollama', corps);
  }

  sante(): Observable<string> {
    return this.http.get('/api/sante', { responseType: 'text' });
  }
}
