import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  Injector,
  input,
  viewChild,
} from '@angular/core';
import { PpCard } from '@pantrypress/ui';

/**
 * The page every account screen sits in (PP3.8): the product name linking home, then one card with the screen's one
 * `h1`. The heading takes focus when the screen opens and again when it moves to another step, so a screen reader
 * hears where it is; a refusal that marks fields moves focus to the first of them, whose error is read with it. The
 * home link is a document load, never a router link: the gateway decides '/' for a signed-in visitor (frontend.md).
 *
 * No skip link: nothing comes before `main` to skip but the product name, which is inside it.
 */
@Component({
  selector: 'ppa-auth-frame',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpCard],
  template: `
    <main class="frame">
      <p class="brand"><a href="/">PantryPress</a></p>
      <pp-card class="panel" role="region" aria-labelledby="auth-heading">
        <h1 id="auth-heading" class="heading" tabindex="-1" #title>{{ heading() }}</h1>
        <ng-content />
      </pp-card>
    </main>
  `,
  styleUrl: './auth-frame.css',
})
export class AuthFrame {
  readonly heading = input.required<string>();
  private readonly element = viewChild.required<ElementRef<HTMLElement>>('title');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  constructor() {
    this.focusHeading();
  }

  /** Moves focus to the heading once the step it names has been drawn. */
  focusHeading(): void {
    afterNextRender(() => this.element().nativeElement.focus(), { injector: this.injector });
  }

  /**
   * Moves focus to the first field marked invalid once the refusal has been drawn, so its error - which the field
   * describes itself by - is heard. A refusal tied to no field is a notice, and a notice of danger is an alert.
   */
  focusFirstInvalid(): void {
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(),
      { injector: this.injector },
    );
  }
}
