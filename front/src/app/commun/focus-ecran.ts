import {
  DOCUMENT,
  DestroyRef,
  Directive,
  ElementRef,
  Injectable,
  Injector,
  afterNextRender,
  effect,
  inject,
  input,
} from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

export type CibleFocus = 'titre' | 'saisie';

export const VERS_LA_SAISIE: { readonly focus: CibleFocus } = { focus: 'saisie' };

export function focaliserSiPerdu(injecteur: Injector, cible: () => HTMLElement | undefined): void {
  if (injecteur.get(DestroyRef).destroyed) {
    return;
  }
  const page = injecteur.get(DOCUMENT);
  afterNextRender(
    () => {
      if (page.activeElement === null || page.activeElement === page.body) {
        cible()?.focus();
      }
    },
    { injector: injecteur },
  );
}

@Injectable({ providedIn: 'root' })
export class FocusEcran {
  private readonly router = inject(Router);
  private readonly injecteur = inject(Injector);
  private readonly cibles = new Map<CibleFocus, HTMLElement>();
  private declencheur: HTMLElement | null = null;

  demarrer(): void {
    this.router.events
      .pipe(filter((evenement) => evenement instanceof NavigationEnd))
      .subscribe(() => this.apresNavigation());
  }

  inscrire(cible: CibleFocus, element: HTMLElement): void {
    this.cibles.set(cible, element);
  }

  retirer(cible: CibleFocus, element: HTMLElement): void {
    if (this.cibles.get(cible) === element) {
      this.cibles.delete(cible);
    }
  }

  noterDeclencheur(element: HTMLElement): void {
    this.declencheur = element;
  }

  estDeclencheur(element: HTMLElement): boolean {
    return this.declencheur === element;
  }

  private apresNavigation(): void {
    const navigation = this.router.lastSuccessfulNavigation();
    if (!navigation?.previousNavigation) {
      return;
    }
    const voulue: unknown = navigation.extras.state?.['focus'];
    const demandee = navigation.trigger === 'imperative' && voulue === 'saisie';
    const cible: CibleFocus = demandee ? 'saisie' : 'titre';
    afterNextRender(
      () => {
        const element = this.cibles.get(cible);
        (element?.matches(':disabled') ? this.cibles.get('titre') : element)?.focus();
      },
      { injector: this.injecteur },
    );
  }
}

@Directive({
  selector: '[usineCibleFocus]',
  host: { '[attr.tabindex]': "usineCibleFocus() === 'titre' ? -1 : null" },
})
export class CibleFocusEcran {
  readonly usineCibleFocus = input.required<CibleFocus>();

  constructor() {
    const focus = inject(FocusEcran);
    const element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    effect((nettoyer) => {
      const cible = this.usineCibleFocus();
      focus.inscrire(cible, element);
      nettoyer(() => focus.retirer(cible, element));
    });
  }
}

@Directive({
  selector: '[usineFocusApresAppel]',
  host: { '(click)': 'noter()', '(change)': 'noter()' },
})
export class FocusApresAppel {
  readonly usineFocusApresAppel = input.required<boolean>();

  private readonly focus = inject(FocusEcran);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  constructor() {
    const page = inject(DOCUMENT);
    const injecteur = inject(Injector);
    let occupe = false;
    effect(() => {
      const avant = occupe;
      occupe = this.usineFocusApresAppel();
      if (avant && !occupe) {
        afterNextRender(
          () => {
            const perdu = page.activeElement === null || page.activeElement === page.body;
            if (perdu && this.focus.estDeclencheur(this.element)) {
              this.element.focus();
            }
          },
          { injector: injecteur },
        );
      }
    });
  }

  protected noter(): void {
    this.focus.noterDeclencheur(this.element);
  }
}
