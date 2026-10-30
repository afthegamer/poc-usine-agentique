import { InjectionToken } from '@angular/core';

export const HORLOGE = new InjectionToken<() => Date>('HORLOGE', {
  providedIn: 'root',
  factory: () => () => new Date(),
});
