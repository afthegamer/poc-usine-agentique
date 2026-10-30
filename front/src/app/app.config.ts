import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { Acces, intercepteurAcces } from './api/acces';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAppInitializer(() => inject(Acces).demarrer()),
    provideHttpClient(withFetch(), withInterceptors([intercepteurAcces])),
    provideRouter(routes, withComponentInputBinding({ queryParams: false })),
  ],
};
