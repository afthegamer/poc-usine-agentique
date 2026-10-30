import { Ia, IaDto, ParametresDto, lireIa, lireService } from '../../api/types';
import { libelles } from '../../textes/libelles';

export interface IaAffichee {
  readonly id: Ia;
  readonly dto: IaDto;
}

export function iasAffichees(etat: ParametresDto): IaAffichee[] {
  return etat.ias.flatMap((dto) => {
    const id = lireIa(dto.id);
    return id === null ? [] : [{ id, dto }];
  });
}

export function nomIa(ia: IaAffichee): string {
  const service = ia.id === 'autre' && ia.dto.service !== null ? lireService(ia.dto.service) : null;
  return service === null
    ? libelles.parametres.ias[ia.id].nom
    : libelles.parametres.services[service];
}

export function nomDuModele(ia: IaAffichee, modele: string): string {
  return (
    ia.dto.modeles.find(({ id }) => id === modele)?.nom ??
    (ia.id === 'ollama' ? modele.replace(/:latest$/, '') : modele)
  );
}

export function nomModele(ia: IaAffichee): string | null {
  return ia.dto.modele === null ? null : nomDuModele(ia, ia.dto.modele);
}

export function modeleApresRetrait(etat: ParametresDto, retiree: Ia): string | null {
  const ias = iasAffichees(etat).filter(({ id }) => id !== retiree);
  const generale = ias.find(({ id }) => id === etat.ia);
  if (generale) {
    return nomModele(generale);
  }
  for (const ia of ias) {
    const modele = ia.dto.connectee
      ? (ia.dto.modele ?? ia.dto.modeles.find(({ parDefaut }) => parDefaut)?.id ?? null)
      : null;
    if (modele !== null) {
      return nomDuModele(ia, modele);
    }
  }
  return null;
}
