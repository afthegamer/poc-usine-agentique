import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ADRESSE_INVALIDE } from '../../api/erreurs';
import { ModeleDto, SERVICES, Service, lireService } from '../../api/types';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { BoutonPlein } from '../../commun/bouton-plein';
import { FocusApresAppel, focaliserSiPerdu } from '../../commun/focus-ecran';
import { EtatParametres } from '../../etat/etat-parametres';
import { libelles } from '../../textes/libelles';
import { ChoixModele } from './choix-modele';
import { IaAffichee } from './ias';

@Component({
  selector: 'usine-panneau-autre',
  imports: [ActionSoulignee, BoutonPlein, FocusApresAppel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './panneau-autre.html',
  styleUrl: './panneau.css',
})
export class PanneauAutre implements OnInit {
  readonly ia = input.required<IaAffichee>();
  readonly retraitEnCours = input(false);
  readonly retirer = output();

  private readonly parametres = inject(EtatParametres);
  private readonly injecteur = inject(Injector);
  private readonly titreCle = viewChild<ElementRef<HTMLElement>>('titreCle');
  private readonly champ = viewChild<ElementRef<HTMLInputElement>>('champ');
  private readonly adresse = viewChild<ElementRef<HTMLInputElement>>('adresse');

  protected readonly libelles = libelles;
  protected readonly services = SERVICES;
  protected readonly enregistre = computed(() => {
    const service = this.ia().dto.service;
    return service === null ? null : lireService(service);
  });
  protected readonly service = linkedSignal<Service>(() => this.enregistre() ?? 'mistral');
  protected readonly nomService = computed(() => libelles.parametres.services[this.service()]);
  protected readonly remplacement = signal(false);
  protected readonly aCle = computed(() => {
    const enregistre = this.enregistre();
    return (
      this.ia().dto.connectee &&
      !this.remplacement() &&
      (enregistre === null || enregistre === this.service())
    );
  });
  protected readonly envoi = signal(false);
  protected readonly erreurCle = signal('');
  protected readonly erreurAdresse = signal('');
  protected readonly modeles = signal<readonly ModeleDto[]>([]);
  protected readonly choix = new ChoixModele(this.ia);
  protected readonly erreurModeles = signal('');

  ngOnInit(): void {
    if (this.ia().dto.connectee) {
      void this.chargerModeles();
    }
  }

  protected async enregistrer(champ: HTMLInputElement): Promise<void> {
    const service = this.service();
    const adresse = this.adresse()?.nativeElement;
    this.erreurCle.set('');
    this.erreurAdresse.set('');
    this.envoi.set(true);
    const resultat = await this.parametres.enregistrerCle('autre', {
      cle: champ.value,
      service,
      adresse: service === 'adresse' ? (adresse?.value ?? '') : null,
    });
    this.envoi.set(false);
    if ('erreur' in resultat) {
      if (adresse && resultat.erreur === ADRESSE_INVALIDE) {
        this.erreurAdresse.set(resultat.erreur);
        focaliserSiPerdu(this.injecteur, () => adresse);
      } else {
        this.erreurCle.set(resultat.erreur);
        focaliserSiPerdu(this.injecteur, () => champ);
      }
      return;
    }
    champ.value = '';
    this.fermerSaisie();
    void this.chargerModeles();
  }

  protected choisirService(service: Service): void {
    this.service.set(service);
    this.erreurCle.set('');
    this.erreurAdresse.set('');
    const champ = this.champ()?.nativeElement;
    if (champ) {
      champ.value = '';
    }
  }

  protected remplacer(): void {
    this.parametres.nouvelleAction();
    this.remplacement.set(true);
    afterNextRender(() => this.champ()?.nativeElement.focus(), { injector: this.injecteur });
  }

  protected fermerSaisie(): void {
    this.remplacement.set(false);
    this.erreurCle.set('');
    this.erreurAdresse.set('');
    focaliserSiPerdu(this.injecteur, () => this.titreCle()?.nativeElement);
  }

  private async chargerModeles(): Promise<void> {
    this.erreurModeles.set('');
    const resultat = await this.parametres.modelesAutre();
    if ('erreur' in resultat) {
      this.erreurModeles.set(resultat.erreur);
      return;
    }
    this.modeles.set(resultat.valeur);
  }
}
