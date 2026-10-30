import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChoixDto, Ia, ModeleDto, RolesDto } from '../../api/types';
import { ActionSoulignee } from '../../commun/action-soulignee';
import { Cadre } from '../../commun/cadre';
import { CibleFocusEcran, FocusApresAppel, focaliserSiPerdu } from '../../commun/focus-ecran';
import { EtatParametres } from '../../etat/etat-parametres';
import { libelles } from '../../textes/libelles';
import { IaAffichee, iasAffichees, nomDuModele, nomModele } from './ias';

type Role = keyof RolesDto;
type Cible = Role | 'tout';

interface OptionRole {
  readonly valeur: string;
  readonly libelle: string;
}

interface GroupeRole {
  readonly ia: Ia;
  readonly libelle: string;
  readonly connectee: boolean;
  readonly options: readonly OptionRole[];
}

const ROLES: readonly Role[] = ['po', 'qaAmont', 'dev', 'qa'];
const ORDRE_GROUPES: readonly Ia[] = ['gemini', 'anthropic', 'ollama', 'autre'];
const AUCUN_ROLE: RolesDto = { po: null, qaAmont: null, dev: null, qa: null };

const valeurChoix = (choix: ChoixDto | null): string =>
  choix === null ? '' : `${choix.ia}:${choix.modele}`;

function lireChoix(valeur: string): ChoixDto | null {
  const separateur = valeur.indexOf(':');
  return separateur < 0
    ? null
    : { ia: valeur.slice(0, separateur), modele: valeur.slice(separateur + 1) };
}

@Component({
  selector: 'usine-parametres-avances',
  imports: [ActionSoulignee, Cadre, CibleFocusEcran, FocusApresAppel, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './parametres-avances.html',
  styleUrl: './parametres-avances.css',
})
export class ParametresAvances {
  private readonly parametres = inject(EtatParametres);
  private readonly injecteur = inject(Injector);
  private readonly titre = viewChild<ElementRef<HTMLElement>>('titre');

  protected readonly libelles = libelles;
  protected readonly textes = libelles.parametresAvances;
  protected readonly roles = ROLES;
  protected readonly charge = signal(false);
  protected readonly chargement = signal(false);
  protected readonly echec = signal(false);
  protected readonly etat = computed(() => (this.charge() ? this.parametres.etat() : null));
  protected readonly general = computed(() => {
    const etat = this.etat();
    const ia = etat?.pret ? iasAffichees(etat).find(({ id }) => id === etat.ia) : undefined;
    return ia ? nomModele(ia) : null;
  });
  protected readonly modelesAutre = signal<readonly ModeleDto[]>([]);
  protected readonly groupes = computed(() => {
    const etat = this.etat();
    if (!etat) {
      return [];
    }
    const ias = iasAffichees(etat);
    return ORDRE_GROUPES.flatMap((id): GroupeRole[] => {
      const ia = ias.find((affichee) => affichee.id === id);
      if (!ia) {
        return [];
      }
      const nom = libelles.parametres.ias[id].nom;
      if (!ia.dto.connectee && id !== 'ollama') {
        return [{ ia: id, libelle: this.textes.cleAAjouter(nom), connectee: false, options: [] }];
      }
      const modeles = this.modeles(ia, etat.roles);
      if (!ia.dto.connectee && modeles.length === 0) {
        return [];
      }
      return [
        {
          ia: id,
          libelle: id === 'ollama' ? this.textes.ollama : nom,
          connectee: true,
          options: modeles.map((modele) => ({
            valeur: `${id}:${modele.id}`,
            libelle:
              id === 'gemini' || id === 'anthropic'
                ? this.textes.option(modele.nom, modele.id)
                : modele.nom,
          })),
        },
      ];
    });
  });
  protected readonly regle = computed(() => {
    const roles = this.etat()?.roles;
    return roles ? ROLES.some((role) => roles[role] !== null) : false;
  });
  protected readonly enCours = signal<{ readonly cible: Cible; readonly valeur: string } | null>(
    null,
  );
  protected readonly erreur = signal<{ readonly cible: Cible; readonly texte: string } | null>(
    null,
  );

  constructor() {
    void this.charger();
  }

  protected async charger(): Promise<void> {
    const reprise = this.echec();
    this.chargement.set(true);
    const etat = await this.parametres.charger();
    this.chargement.set(false);
    this.echec.set(!etat);
    if (!etat) {
      return;
    }
    this.charge.set(true);
    if (reprise) {
      focaliserSiPerdu(this.injecteur, () => this.titre()?.nativeElement);
    }
    if (etat.ias.some(({ id, connectee }) => id === 'autre' && connectee)) {
      const resultat = await this.parametres.modelesAutre();
      if ('valeur' in resultat) {
        this.modelesAutre.set(resultat.valeur);
      }
    }
  }

  protected valeur(role: Role): string {
    const enCours = this.enCours();
    if (enCours?.cible === role || enCours?.cible === 'tout') {
      return enCours.valeur;
    }
    return valeurChoix(this.etat()?.roles[role] ?? null);
  }

  protected personnalise(role: Role): boolean {
    return (this.etat()?.roles[role] ?? null) !== null;
  }

  protected changer(role: Role, valeur: string): void {
    const roles = this.etat()?.roles;
    if (roles) {
      void this.regler({ ...roles, [role]: lireChoix(valeur) }, role, valeur);
    }
  }

  protected async toutRemettre(): Promise<void> {
    if (await this.regler(AUCUN_ROLE, 'tout', '')) {
      focaliserSiPerdu(this.injecteur, () => this.titre()?.nativeElement);
    }
  }

  private async regler(roles: RolesDto, cible: Cible, valeur: string): Promise<boolean> {
    this.erreur.set(null);
    this.enCours.set({ cible, valeur });
    const resultat = await this.parametres.reglerRoles(roles);
    this.enCours.set(null);
    if ('erreur' in resultat) {
      this.erreur.set({ cible, texte: resultat.erreur });
      return false;
    }
    return true;
  }

  private modeles(ia: IaAffichee, roles: RolesDto): readonly Pick<ModeleDto, 'id' | 'nom'>[] {
    const liste = ia.id === 'autre' ? this.modelesAutre() : ia.dto.modeles;
    const regles = ROLES.flatMap((role) => {
      const choix = roles[role];
      return choix?.ia === ia.id && !liste.some(({ id }) => id === choix.modele)
        ? [choix.modele]
        : [];
    });
    return [
      ...liste,
      ...regles
        .filter((id, rang) => regles.indexOf(id) === rang)
        .map((id) => ({ id, nom: nomDuModele(ia, id) })),
    ];
  }
}
