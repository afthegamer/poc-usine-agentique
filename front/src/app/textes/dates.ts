import { libelles } from './libelles';

const FORMAT_HEURE = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit' });
const FORMAT_JOUR = new Intl.DateTimeFormat('fr-FR', { day: 'numeric', month: 'long' });
const FORMAT_JOUR_ANNEE = new Intl.DateTimeFormat('fr-FR', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});

export function memeJour(date: Date, autre: Date): boolean {
  return (
    date.getFullYear() === autre.getFullYear() &&
    date.getMonth() === autre.getMonth() &&
    date.getDate() === autre.getDate()
  );
}

export function heure(date: Date): string {
  return FORMAT_HEURE.format(date);
}

export function dateCourte(date: Date, maintenant: Date): string {
  return (date.getFullYear() === maintenant.getFullYear() ? FORMAT_JOUR : FORMAT_JOUR_ANNEE).format(
    date,
  );
}

export function jour(date: Date, maintenant: Date): string {
  const veille = new Date(
    maintenant.getFullYear(),
    maintenant.getMonth(),
    maintenant.getDate() - 1,
  );
  if (memeJour(date, maintenant)) {
    return libelles.jours.aujourdhui;
  }
  if (memeJour(date, veille)) {
    return libelles.jours.hier;
  }
  return dateCourte(date, maintenant);
}

export function jourEnMinuscules(date: Date, maintenant: Date): string {
  return jour(date, maintenant).toLocaleLowerCase('fr-FR');
}
