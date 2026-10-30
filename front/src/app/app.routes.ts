import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./ecrans/accueil/accueil').then((m) => m.Accueil),
  },
  {
    path: 'applications',
    loadComponent: () => import('./ecrans/applications/applications').then((m) => m.Applications),
  },
  {
    path: 'applications/:nom',
    loadComponent: () => import('./ecrans/applications/application').then((m) => m.Application),
  },
  {
    path: 'projets/:nom',
    loadComponent: () => import('./ecrans/conversation/conversation').then((m) => m.Conversation),
  },
  {
    path: 'demandes/:id',
    loadComponent: () => import('./ecrans/conversation/conversation').then((m) => m.Conversation),
  },
  {
    path: 'parametres',
    loadComponent: () => import('./ecrans/parametres/parametres').then((m) => m.Parametres),
  },
  {
    path: 'parametres/avances',
    loadComponent: () =>
      import('./ecrans/parametres/parametres-avances').then((m) => m.ParametresAvances),
  },
  { path: '**', redirectTo: '' },
];
