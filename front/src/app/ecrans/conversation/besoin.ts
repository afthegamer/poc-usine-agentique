import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'usine-besoin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `{{ resume() }}`,
  styles: `
    :host {
      display: block;
      padding: 16px 0;
      border-top-width: 1px;
      border-top-style: solid;
      border-top-color: var(--filet);
      border-bottom-width: 1px;
      border-bottom-style: solid;
      border-bottom-color: var(--filet);
      color: var(--texte-neutre);
      font-size: 16px;
      line-height: 1.65;
      white-space: pre-line;
    }
  `,
})
export class Besoin {
  readonly resume = input.required<string>();
}
