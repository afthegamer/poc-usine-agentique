import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  DestroyRef,
  ElementRef,
  Injector,
  Signal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ClientUsine } from '../../api/client-usine';
import { demandeCreeeSansWorker, demandeDisparue, texteErreur } from '../../api/erreurs';
import {
  DemandeDetailDto,
  DemandeDto,
  EtapeAffichee,
  LancementDiscussionDto,
  ValidationDto,
  lireEtapeAffichee,
  lireStatut,
} from '../../api/types';
import { NomCommande, commandesPossibles } from '../../commandes/commandes';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { BoutonPlein } from '../../commun/bouton-plein';
import { DialogueConfirmation } from '../../commun/dialogue-confirmation';
import { CibleFocusEcran, FocusApresAppel, VERS_LA_SAISIE } from '../../commun/focus-ecran';
import { Message } from '../../commun/message';
import { ZoneSaisie } from '../../commun/zone-saisie';
import { Revue, supprimerBrouillon } from '../../etat/brouillons';
import { DetailsDemandes, Lecture, suivreDetails } from '../../etat/details-demandes';
import { EtatUsine } from '../../etat/etat-usine';
import { HORLOGE } from '../../etat/horloge';
import { MessageDePassage, NaturePassage } from '../../etat/message-de-passage';
import { Situation, attendLUtilisateur, estEnCours, situation } from '../../etat/situation';
import { jour, jourEnMinuscules } from '../../textes/dates';
import { libelles } from '../../textes/libelles';
import { Discussion } from '../../discussion/discussion';
import { Disposition } from './disposition';
import { ElementFil, Fil, elementsDuFil, momentDeLAnnulation, momentDuDepart } from './fil';
import { FilDiscussion } from './fil-discussion';
import { BlocSite, LigneHistorique, PanneauProjet } from './panneau-projet';
import { Revoir } from './revoir';
import { Arret } from './vues/arret';
import { AttenteExterne } from './vues/attente-externe';
import { Compris } from './vues/compris';
import { Fabrication } from './vues/fabrication';
import { Plan } from './vues/plan';
import { Preparation } from './vues/preparation';
import { Pret } from './vues/pret';
import { Questions } from './vues/questions';

interface Ecran {
  readonly nom: string | undefined;
  readonly id: string | undefined;
}

type OrigineAnnulation = 'panneau' | 'arret';

interface SituationVue {
  readonly ecran: Ecran;
  readonly id: string;
  readonly situation: Situation;
}

const RANG_DES_ETAPES: Readonly<Record<EtapeAffichee, number>> = {
  comprendre: 0,
  plan: 1,
  fabrication: 2,
  pret: 3,
};

const REVUES: Partial<Readonly<Record<Situation, Revue>>> = {
  questions: 'reponses',
  compris: 'besoin',
  plan: 'backlog',
};

const enCoursHorsAnnulation = (etat: Situation) => estEnCours(etat) && etat !== 'annulation';

@Component({
  selector: 'usine-conversation',
  imports: [
    ActionSoulignee,
    Arret,
    AttenteExterne,
    BoutonPlein,
    CibleFocusEcran,
    Compris,
    DialogueConfirmation,
    Disposition,
    Fabrication,
    Fil,
    FilDiscussion,
    FocusApresAppel,
    Message,
    PanneauProjet,
    Plan,
    Preparation,
    Pret,
    Questions,
    Revoir,
    RouterLink,
    ZoneSaisie,
  ],
  providers: [Discussion],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <usine-disposition [courant]="courant()">
      <h1 #titrePrincipal class="masque" usineCibleFocus="titre">{{ titre() }}</h1>
      @if (detail(); as demande) {
        @if (precedentes().length > 0) {
          <usine-revoir [demandes]="precedentes()" [courante]="demande.id" />
        }
        <usine-fil [elements]="elements()" />
        @if (!echec()) {
          @switch (situation()) {
            @case ('questions') {
              <usine-questions
                [detail]="demande"
                [ajout]="ajout()"
                [occupee]="envoi()"
                [(texte)]="champ"
                (envoi)="repondre(demande.id, $event)"
                (passage)="oublierLesErreurs()"
              />
            }
            @case ('compris') {
              <usine-compris
                [detail]="demande"
                [ajout]="ajout()"
                [occupee]="envoi()"
                (envoi)="valider(demande.id, $event)"
                (versLaSaisie)="focaliserSaisie()"
              />
            }
            @case ('plan') {
              <usine-plan
                [detail]="demande"
                [ajout]="ajout()"
                [occupee]="envoi()"
                [(texte)]="champ"
                (envoi)="valider(demande.id, $event)"
                (versLaSaisie)="focaliserSaisie()"
                (passage)="oublierLesErreurs()"
              />
            }
            @case ('preparation') {
              <usine-preparation [detail]="demande" />
            }
            @case ('fabrication') {
              <usine-fabrication [detail]="demande" [ajout]="ajout()" [moment]="momentDeLaVue()" />
            }
            @case ('pret')
            @case ('inabouti') {
              <usine-pret
                [detail]="demande"
                [ajout]="ajout()"
                [moment]="momentDeLaVue()"
                [lien]="lienSite()"
                [occupee]="relance()"
                [peutRecommencer]="!recommencee()"
                (recommencer)="recommencer(demande.texte)"
              />
            }
            @case ('arret') {
              <usine-arret
                [detail]="demande"
                [ajout]="ajout()"
                [occupee]="relance()"
                [annulation]="annulation()"
                [erreur]="erreurAnnulationDeLArret()"
                (reprendre)="reprendre(demande.id)"
                (annuler)="ouvrirConfirmation('arret')"
              />
            }
            @case ('attente-externe') {
              <usine-attente-externe [detail]="demande" />
            }
            @case ('annulation') {
              <usine-message auteur="usine" [moment]="momentDeLaVue()">
                <p class="neutre">{{ libelles.conversation.arretEnCours }}</p>
              </usine-message>
            }
            @case ('inconnue') {
              <usine-message auteur="usine" [moment]="momentDeLaVue()">
                <p class="neutre">{{ libelles.conversation.travailEnCours }}</p>
              </usine-message>
            }
            @case ('annulee') {
              <usine-message auteur="usine" [moment]="momentDeLaVue()">
                @if (ajout()) {
                  <p>{{ libelles.conversation.ajoutAnnule }}</p>
                  <div class="actions">
                    <a usine-bouton-plein [routerLink]="lienSite()">{{
                      libelles.panneau.ouvrirSite
                    }}</a>
                  </div>
                } @else {
                  <p>{{ libelles.conversation.projetAnnule }}</p>
                  <div class="actions">
                    <a usine-action-soulignee routerLink="/">{{
                      libelles.conversation.retourAccueil
                    }}</a>
                  </div>
                }
              </usine-message>
            }
          }
        }
      } @else if (projetSansDemande() !== null) {
        <usine-fil [elements]="elements()" />
        <usine-pret [moment]="momentDeLaVue()" [lien]="lienSite()" />
      }
      @if (echec()) {
        <div class="echec">
          <p class="alerte" role="alert">{{ echec() }}</p>
          <button
            type="button"
            usine-bouton-plein
            [disabled]="relecture()"
            [attr.aria-busy]="relecture() || null"
            [usineFocusApresAppel]="relecture()"
            (click)="reessayer()"
          >
            {{ libelles.conversation.reessayer }}
          </button>
        </div>
      }
      @if (alerte()) {
        <p class="alerte" role="alert">{{ alerte() }}</p>
      }
      @if (discussion.elements().length > 0 || discussion.indicateur()) {
        <usine-fil-discussion [depuis]="momentDeLaVue()" (boutonsRetires)="replacerLeFocus()" />
      }
      @if (saisie(); as reglage) {
        <usine-zone-saisie
          [etiquette]="reglage.etiquette"
          [indication]="reglage.indication"
          [occupee]="envoi() || (reglage.discussion && discussion.occupee())"
          [erreur]="discussion.erreur()"
          [commandes]="commandes()"
          [(texte)]="champ"
          (envoi)="envoyer($event)"
          (commande)="executer($event)"
        />
      }
      <usine-panneau-projet
        [titre]="titrePanneau()"
        [etape]="etape()"
        [pret]="pret()"
        [sansEtapeCourante]="situation() === 'inabouti' || situation() === 'annulee'"
        [titreAjout]="titreAjout()"
        [progression]="progression()"
        [phrase]="phrase()"
        [attend]="attend()"
        [site]="site()"
        [historique]="historique()"
        [annulable]="annulable()"
        [annulation]="annulation()"
        [erreurAnnulation]="erreurAnnulationDuPanneau()"
        (annuler)="ouvrirConfirmation('panneau')"
        (modification)="focaliserSaisie()"
      />
      <usine-dialogue-confirmation
        [titre]="libelles.annulation.titre(ajout())"
        [phrase]="libelles.annulation.phrase(ajout())"
        [oui]="libelles.annulation.oui"
        [non]="libelles.annulation.non"
        [(ouvert)]="confirmation"
        (reponse)="annuler($event)"
      />
    </usine-disposition>
  `,
  styles: `
    .neutre {
      color: var(--texte-neutre);
      font-style: italic;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 22px;
    }

    .echec {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 16px;
    }

    .alerte {
      color: var(--erreur);
      font-size: 15px;
      line-height: 1.55;
    }
  `,
})
export class Conversation {
  readonly nom = input<string>();
  readonly id = input<string>();

  private readonly etat = inject(EtatUsine);
  private readonly details = inject(DetailsDemandes);
  private readonly client = inject(ClientUsine);
  private readonly passage = inject(MessageDePassage);
  private readonly router = inject(Router);
  private readonly horloge = inject(HORLOGE);
  protected readonly discussion = inject(Discussion);
  private readonly zone = viewChild(ZoneSaisie);
  private readonly questions = viewChild(Questions);
  private readonly compris = viewChild(Compris);
  private readonly plan = viewChild(Plan);
  private readonly titrePrincipal = viewChild.required<ElementRef<HTMLElement>>('titrePrincipal');
  private readonly injecteur = inject(Injector);
  private readonly page = inject(DOCUMENT);

  protected readonly libelles = libelles;
  private readonly ecran = computed<Ecran>(() => ({ nom: this.nom(), id: this.id() }));
  protected readonly champ = linkedSignal({ source: this.ecran, computation: () => '' });
  protected readonly envoi = linkedSignal({ source: this.ecran, computation: () => false });
  protected readonly relance = linkedSignal({ source: this.ecran, computation: () => false });
  protected readonly alerte = linkedSignal({ source: this.ecran, computation: () => '' });
  protected readonly confirmation = linkedSignal({ source: this.ecran, computation: () => false });
  private readonly origineAnnulation = linkedSignal<Ecran, OrigineAnnulation>({
    source: this.ecran,
    computation: () => 'panneau',
  });
  protected readonly annulation = linkedSignal({ source: this.ecran, computation: () => false });
  private readonly erreurAnnulation = linkedSignal({
    source: this.ecran,
    computation: () => '',
  });
  protected readonly erreurAnnulationDuPanneau = computed(() =>
    this.origineAnnulation() === 'panneau' ? this.erreurAnnulation() : '',
  );
  protected readonly erreurAnnulationDeLArret = computed(() =>
    this.origineAnnulation() === 'arret' ? this.erreurAnnulation() : '',
  );
  protected readonly recommencee = linkedSignal({ source: this.ecran, computation: () => false });
  private readonly arretsDuSite = computed(() => {
    const projet = this.projetPublie();
    return projet === null ? 0 : (this.etat.essaisArretes().get(projet) ?? 0);
  });
  private readonly siteOuvert = linkedSignal({
    source: this.arretsDuSite,
    computation: () => false,
  });
  private precedente: SituationVue | null = null;
  private detruit = false;
  private parti = false;

  protected readonly courant = computed(() => {
    const nom = this.nom();
    if (nom !== undefined) {
      return `projet:${nom}`;
    }
    const id = this.id();
    const element = this.etat
      .projetsAffiches()
      .find(({ demandes }) => demandes.some((demande) => demande.id === id));
    return element?.cle ?? `demande:${id}`;
  });
  protected readonly titre = computed(() => {
    const element = this.etat.projetsAffiches().find(({ cle }) => cle === this.courant());
    const id = this.id();
    return element?.titre ?? (id === undefined ? '' : (this.etat.demandes().get(id)?.titre ?? ''));
  });

  private readonly idCourant: Signal<string | null> = computed(
    () => this.id() ?? (this.etat.charge() ? (this.demandesDuProjet()[0]?.id ?? null) : null),
  );
  private readonly lecture: Signal<Lecture | undefined> = computed(() => {
    const id = this.idCourant();
    return id === null ? undefined : this.details.lecture(id);
  });
  private readonly projetVise: Signal<string | null> = computed(
    () => this.nom() ?? this.lecture()?.detail?.projet ?? null,
  );
  private readonly demandesDuProjet: Signal<readonly DemandeDto[]> = computed(() => {
    const projet = this.projetVise();
    const demandes = this.etat.demandes();
    return this.etat.ordreDemandes().flatMap((id) => {
      const demande = demandes.get(id);
      return demande !== undefined && projet !== null && demande.projet === projet ? [demande] : [];
    });
  });
  protected readonly detail = computed(() =>
    this.etat.charge() ? (this.lecture()?.detail ?? null) : null,
  );
  protected readonly ajout = computed(() => {
    const detail = this.detail();
    const siennes = this.demandesDuProjet();
    return detail !== null && siennes.length > 0 && siennes.at(-1)?.id !== detail.id;
  });
  private readonly projetPublie = computed(() => {
    const projet = this.etat.charge() ? this.projetVise() : null;
    return this.etat.projets().some(({ nom }) => nom === projet) ? projet : null;
  });
  protected readonly projetSansDemande = computed(() => {
    const projet = this.projetPublie();
    return projet !== null && this.idCourant() === null
      ? (this.etat.projets().find(({ nom }) => nom === projet) ?? null)
      : null;
  });
  protected readonly lienSite = computed(() => {
    const projet = this.projetPublie();
    return projet === null ? null : `/applications/${projet}`;
  });
  protected readonly situation = computed(() => {
    const detail = this.detail();
    return detail === null
      ? null
      : situation(detail, {
          annulationDemandee: this.etat.annulationsDemandees().has(detail.id),
          projetPublie: this.projetPublie() !== null,
          estCreation: !this.ajout(),
          livraison: detail.livraison,
        });
  });
  private readonly texteEchec = computed(() => {
    const echec = this.lecture()?.echec;
    return echec ? texteErreur('detail', echec) : '';
  });
  private readonly disparue = computed(() => {
    const echec = this.lecture()?.echec ?? null;
    return echec !== null && demandeDisparue('detail', echec);
  });
  protected readonly echec = computed(() => {
    const texte = this.texteEchec();
    return this.etat.charge() && !this.disparue() ? texte : '';
  });
  protected readonly relecture = linkedSignal({
    source: () => this.lecture(),
    computation: () => false,
  });
  protected readonly momentDeLaVue = computed(() => {
    const detail = this.detail();
    if (detail === null) {
      const projet = this.projetSansDemande();
      return projet === null ? null : new Date(projet.derniereMiseAJour);
    }
    switch (this.situation()) {
      case 'fabrication':
        return momentDuDepart(detail);
      case 'annulation':
        return momentDeLAnnulation(detail);
      default:
        return new Date(detail.misAJourLe);
    }
  });
  protected readonly elements = computed<readonly ElementFil[]>(() => {
    const detail = this.detail();
    const vue = this.echec() ? null : this.momentDeLaVue();
    if (detail !== null) {
      return elementsDuFil(detail, {
        ajout: this.ajout(),
        fige: false,
        vue,
        maintenant: this.horloge(),
      });
    }
    return vue === null
      ? []
      : [{ genre: 'jour', jour: jour(vue, this.horloge()), recapitulatif: '' }];
  });
  protected readonly precedentes = computed(() => {
    if (!this.ajout()) {
      return [];
    }
    const siennes = this.demandesDuProjet();
    const rang = siennes.findIndex(({ id }) => id === this.idCourant());
    return siennes.slice(rang + 1).reverse();
  });
  protected readonly saisie = computed(() => {
    const message = libelles.saisie.message;
    const question = {
      etiquette: message,
      indication: libelles.conversation.indicationQuestion,
      discussion: true,
    };
    const modification = {
      etiquette: message,
      indication: libelles.conversation.indicationModification,
      discussion: true,
    };
    if (this.echec()) {
      const creationAnnulee = this.situation() === 'annulee' && !this.ajout();
      return this.corpsDiscussion() === null || creationAnnulee ? null : question;
    }
    switch (this.situation()) {
      case 'questions':
        return {
          etiquette: libelles.questions.etiquette,
          indication: libelles.questions.indication,
          discussion: false,
        };
      case 'compris':
        return this.compris()?.saisieDeRemarque()
          ? { etiquette: message, indication: libelles.compris.indication, discussion: false }
          : {
              etiquette: message,
              indication: libelles.compris.indicationDiscussion,
              discussion: true,
            };
      case 'plan':
        return this.plan()?.saisieDeRemarque()
          ? { etiquette: message, indication: libelles.plan.indication, discussion: false }
          : {
              etiquette: message,
              indication: libelles.plan.indicationDiscussion,
              discussion: true,
            };
      case 'preparation':
      case 'fabrication':
      case 'arret':
      case 'annulation':
      case 'attente-externe':
      case 'inconnue':
        return question;
      case 'pret':
      case 'inabouti':
        return modification;
      case 'annulee':
        return this.ajout() ? modification : null;
      default:
        return this.projetSansDemande() === null
          ? { etiquette: message, indication: '', discussion: false }
          : modification;
    }
  });
  protected readonly commandes = computed(() =>
    commandesPossibles({
      ecran: 'conversation',
      projetPublie: this.projetPublie() !== null,
      situation: this.situation(),
      ajout: this.ajout(),
    }).filter(
      ({ nom }) =>
        !(nom === '/annuler' && this.annulation()) &&
        !(nom === '/reprendre' && (this.relance() || this.echec() !== '')),
    ),
  );
  private readonly corpsDiscussion = computed<LancementDiscussionDto | null>(() => {
    const projet = this.projetPublie();
    const detail = this.detail();
    const etat = this.situation();
    if (projet === null) {
      return detail === null ? null : { demande: detail.id };
    }
    return detail !== null && this.ajout() && etat !== null && estEnCours(etat)
      ? { projet, demande: detail.id }
      : { projet };
  });

  protected readonly titrePanneau = computed(() => (this.etat.charge() ? this.titre() : ''));
  protected readonly titreAjout = computed(() =>
    this.ajout() ? (this.detail()?.titre ?? '') : null,
  );
  protected readonly pret = computed(
    () => this.projetSansDemande() !== null || this.situation() === 'pret',
  );
  protected readonly etape = computed(() => {
    if (this.pret() || this.situation() === 'inabouti') {
      return RANG_DES_ETAPES.pret;
    }
    const detail = this.detail();
    const etape = detail === null ? null : lireEtapeAffichee(detail.etapeAffichee);
    return etape === null ? null : RANG_DES_ETAPES[etape];
  });
  private readonly position = computed(() =>
    this.situation() === 'plan' ? (this.plan()?.position() ?? null) : null,
  );
  protected readonly progression = computed(() => {
    const position = this.position();
    return position === null ? '' : libelles.panneau.progression(position.rang, position.total);
  });
  protected readonly phrase = computed(() => {
    const attentes = libelles.attentes;
    switch (this.situation()) {
      case 'questions':
      case 'compris':
        return attentes.reponse;
      case 'plan': {
        const position = this.position();
        return attentes.avis(position !== null && position.rang === position.total, this.ajout());
      }
      case 'preparation':
        return attentes.preparation;
      case 'fabrication':
        return attentes.fabrication;
      case 'arret':
        return attentes.arret;
      case 'annulation':
        return attentes.annulation;
      case 'attente-externe':
        return attentes.externe;
      default:
        return '';
    }
  });
  protected readonly attend = computed(() => {
    const etat = this.situation();
    return etat !== null && attendLUtilisateur(etat);
  });
  protected readonly site = computed<BlocSite | null>(() => {
    const lien = this.lienSite();
    const etat = this.situation();
    if (lien !== null) {
      const pendantAjout = this.ajout() && etat !== null && estEnCours(etat);
      const forme = !pendantAjout
        ? 'publie'
        : etat === 'fabrication'
          ? 'ajout-en-fabrication'
          : 'ajout';
      return { forme, lien, ouvert: this.siteOuvert() };
    }
    return etat !== null && enCoursHorsAnnulation(etat) ? { forme: 'a-venir' } : null;
  });
  protected readonly historique = computed<readonly LigneHistorique[]>(() => {
    if (!this.ajout()) {
      return [];
    }
    const siennes = this.demandesDuProjet();
    const maintenant = this.horloge();
    return siennes.map((demande, rang) => {
      const etat = this.situationDansLeProjet(demande, rang === siennes.length - 1);
      return {
        id: demande.id,
        texte: libelles.panneau.ligneHistorique(
          demande.titre,
          libelles.situations[etat].court,
          jourEnMinuscules(new Date(demande.misAJourLe), maintenant),
        ),
        carre: attendLUtilisateur(etat)
          ? 'attente'
          : etat === 'pret' || etat === 'terminee'
            ? 'accent'
            : 'texte',
      };
    });
  });
  protected readonly annulable = computed(() => {
    const etat = this.situation();
    return etat !== null && enCoursHorsAnnulation(etat);
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.detruit = true));
    suivreDetails(() => {
      const id = this.idCourant();
      return id === null ? [] : [id];
    });
    suivreDetails(() =>
      this.ajout()
        ? this.demandesDuProjet()
            .slice(0, -1)
            .filter(
              ({ id, statut }) => id !== this.idCourant() && lireStatut(statut) === 'Terminee',
            )
            .map(({ id }) => id)
        : [],
    );
    effect(() => {
      const nom = this.nom();
      const projets = this.etat.projets();
      if (
        nom !== undefined &&
        this.etat.charge() &&
        !projets.some((projet) => projet.nom === nom)
      ) {
        untracked(() => this.quitter(libelles.erreurs.projetDisparu));
      }
    });
    effect(() => {
      if (this.disparue()) {
        untracked(() => this.quitter(libelles.erreurs.projetDisparu));
      }
    });
    effect(() => {
      const ecran = this.ecran();
      const detail = this.detail();
      const etat = this.situation();
      untracked(() => this.suivreLaSituation(ecran, detail, etat));
    });
    effect(() => {
      const ecran = this.ecran();
      const projet = this.projetPublie();
      untracked(() => this.lireEssai(ecran, projet));
    });
    effect(() => {
      this.ecran();
      untracked(() => this.discussion.repartir());
    });
    effect(() => {
      if (!this.saisie()?.discussion && this.discussion.erreur() !== '') {
        untracked(() => this.discussion.oublierLErreur());
      }
    });
  }

  protected envoyer(texte: string): void {
    this.oublierLesErreurs();
    const corps = this.corpsDiscussion();
    if (this.saisie()?.discussion && corps !== null) {
      this.discussion.envoyer(texte, corps, {
        envoye: () => {
          if (this.champ() === texte) {
            this.champ.set('');
          }
        },
        disparu: () => this.quitter(libelles.erreurs.projetDisparu),
      });
    } else {
      (this.questions() ?? this.compris() ?? this.plan())?.saisir(texte);
    }
  }

  protected executer(commande: NomCommande): void {
    const projet = this.projetPublie();
    const detail = this.detail();
    switch (commande) {
      case '/nouveau':
        void this.router.navigate(['/'], { state: VERS_LA_SAISIE });
        break;
      case '/ouvrir':
        if (projet !== null) {
          void this.router.navigate(['/applications', projet]);
        }
        break;
      case '/annuler':
        this.ouvrirConfirmation('panneau');
        break;
      case '/reprendre':
        if (detail !== null) {
          this.reprendre(detail.id);
        }
        break;
    }
  }

  protected repondre(id: string, reponses: string[]): void {
    this.agir(id, 'reponses', this.client.repondre(id, { reponses }));
  }

  protected valider(id: string, corps: ValidationDto): void {
    this.agir(id, corps.etape === 'besoin' ? 'besoin' : 'backlog', this.client.valider(id, corps));
  }

  protected annuler(accord: boolean): void {
    const detail = this.detail();
    if (!accord || detail === null) {
      return;
    }
    const ecran = this.ecran();
    const creation = !this.ajout();
    const titre = this.titre();
    const etat = this.situation();
    const revue = etat === null ? undefined : REVUES[etat];
    this.annulation.set(true);
    this.oublierLesErreurs();
    this.client.annuler(detail.id).subscribe({
      next: (reponse) => {
        if (revue !== undefined) {
          supprimerBrouillon(reponse.id, revue);
        }
        this.details.remplacer(reponse);
        const statut = lireStatut(reponse.statut);
        if (statut === 'EnCours') {
          this.etat.noterAnnulationDemandee(reponse.id);
        }
        if (ecran !== this.ecran()) {
          return;
        }
        this.annulation.set(false);
        if (statut === 'Annulee' && creation) {
          this.quitter(libelles.annulation.projetAnnule(titre), 'information');
          return;
        }
        if (statut === 'Terminee') {
          this.alerte.set(libelles.annulation.tropTard);
        }
        this.replacerLeFocusPerdu();
      },
      error: (erreur: unknown) =>
        this.surEchec(detail.id, erreur, ecran, (texte) => {
          this.annulation.set(false);
          this.erreurAnnulation.set(texte);
        }),
    });
  }

  protected recommencer(texte: string): void {
    const ecran = this.ecran();
    this.relance.set(true);
    this.oublierLesErreurs();
    this.client.lancer({ texte }).subscribe({
      next: (detail) => {
        this.details.recevoirCreation(detail);
        if (ecran === this.ecran() && !this.detruit && !this.parti) {
          this.relance.set(false);
          void this.router.navigate(['/demandes', detail.id]);
        }
      },
      error: (erreur: unknown) => {
        const reponse = erreur instanceof HttpErrorResponse ? erreur : null;
        const message =
          reponse === null ? libelles.erreurs.probleme : texteErreur('demande', reponse);
        const creee = reponse !== null && demandeCreeeSansWorker(reponse);
        if (creee) {
          this.etat.relireDemandes();
        }
        if (ecran === this.ecran()) {
          this.relance.set(false);
          this.alerte.set(message);
          this.recommencee.set(creee);
        }
      },
    });
  }

  protected reprendre(id: string): void {
    const ecran = this.ecran();
    this.relance.set(true);
    this.oublierLesErreurs();
    this.client.reprendre(id).subscribe({
      next: (detail) => {
        this.details.remplacer(detail);
        if (ecran === this.ecran()) {
          this.relance.set(false);
          this.replacerLeFocusPerdu();
        }
      },
      error: (erreur: unknown) =>
        this.surEchec(id, erreur, ecran, (texte) => {
          this.relance.set(false);
          this.alerte.set(texte);
        }),
    });
  }

  protected reessayer(): void {
    const id = this.idCourant();
    if (id !== null && !this.relecture()) {
      this.relecture.set(true);
      this.details.relire(id, (relu) => {
        if (relu) {
          this.replacerLeFocusPerdu();
        }
      });
    }
  }

  protected focaliserSaisie(): void {
    this.zone()?.focaliser();
  }

  protected ouvrirConfirmation(origine: OrigineAnnulation): void {
    this.origineAnnulation.set(origine);
    this.erreurAnnulation.set('');
    this.confirmation.set(true);
  }

  protected replacerLeFocus(): void {
    afterNextRender(() => this.focaliserLaSaisieOuLeTitre(), { injector: this.injecteur });
  }

  protected oublierLesErreurs(): void {
    this.alerte.set('');
    this.erreurAnnulation.set('');
  }

  private agir(id: string, revue: Revue, appel: Observable<DemandeDetailDto>): void {
    const ecran = this.ecran();
    this.envoi.set(true);
    this.oublierLesErreurs();
    appel.subscribe({
      next: (detail) => {
        supprimerBrouillon(id, revue);
        this.details.remplacer(detail);
        if (ecran === this.ecran()) {
          this.envoi.set(false);
          this.champ.set('');
          this.replacerLeFocusPerdu();
        }
      },
      error: (erreur: unknown) =>
        this.surEchec(
          id,
          erreur,
          ecran,
          (texte, relecture) => {
            this.envoi.set(false);
            this.alerte.set(texte);
            if (relecture) {
              this.champ.set('');
            }
          },
          () => supprimerBrouillon(id, revue),
        ),
    });
  }

  private replacerLeFocusPerdu(): void {
    if (this.detruit) {
      return;
    }
    afterNextRender(
      () => {
        const actif = this.page.activeElement;
        if (actif === null || actif === this.page.body) {
          this.focaliserLaSaisieOuLeTitre();
        }
      },
      { injector: this.injecteur },
    );
  }

  private focaliserLaSaisieOuLeTitre(): void {
    const zone = this.zone();
    if (zone === undefined) {
      this.titrePrincipal().nativeElement.focus();
    } else {
      zone.focaliser();
    }
  }

  private surEchec(
    id: string,
    erreur: unknown,
    ecran: Ecran,
    afficher: (texte: string, relecture: boolean) => void,
    invalider: () => void = () => undefined,
  ): void {
    const reponse = erreur instanceof HttpErrorResponse ? erreur : null;
    const texte = reponse === null ? libelles.erreurs.probleme : texteErreur('demande', reponse);
    const disparue = reponse !== null && demandeDisparue('demande', reponse);
    const statut = reponse?.status;
    const relecture = statut === HttpStatusCode.BadRequest || statut === HttpStatusCode.Conflict;
    const signaler = (relu: boolean) => {
      if (ecran !== this.ecran()) {
        return;
      }
      if (disparue) {
        this.quitter(texte);
      } else {
        afficher(relu ? texte : '', relecture);
      }
    };
    if (relecture) {
      invalider();
      this.details.relire(id, signaler);
    } else {
      signaler(true);
    }
  }

  private suivreLaSituation(
    ecran: Ecran,
    detail: DemandeDetailDto | null,
    etat: Situation | null,
  ): void {
    if (detail === null || etat === null) {
      return;
    }
    const precedente = this.precedente;
    const avant =
      precedente !== null && precedente.ecran === ecran && precedente.id === detail.id
        ? precedente.situation
        : null;
    this.precedente = { ecran, id: detail.id, situation: etat };
    if (avant === null || avant === etat) {
      return;
    }
    const revue = REVUES[avant];
    if (revue !== undefined) {
      supprimerBrouillon(detail.id, revue);
    }
    if (avant === 'annulation' && etat === 'annulee' && !this.ajout()) {
      this.quitter(libelles.annulation.projetAnnule(this.titre()), 'information');
    }
  }

  private lireEssai(ecran: Ecran, projet: string | null): void {
    this.siteOuvert.set(false);
    if (projet === null) {
      return;
    }
    this.client.essai(projet).subscribe({
      next: ({ enCours }) => {
        if (ecran === this.ecran()) {
          this.siteOuvert.set(enCours);
        }
      },
      error: (erreur: unknown) => console.warn("Lecture de l'essai", projet, erreur),
    });
  }

  private situationDansLeProjet(demande: DemandeDto, estCreation: boolean): Situation {
    return situation(demande, {
      annulationDemandee: this.etat.annulationsDemandees().has(demande.id),
      projetPublie: this.projetPublie() !== null,
      estCreation,
      livraison: this.details.lecture(demande.id)?.detail?.livraison,
    });
  }

  private quitter(texte: string, nature: NaturePassage = 'erreur'): void {
    if (this.detruit || this.parti) {
      return;
    }
    this.parti = true;
    this.passage.deposer(texte, nature);
    void this.router.navigate(['/']);
  }
}
