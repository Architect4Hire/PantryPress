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

/**
 * The page every account screen sits in (PP3.8): the product name linking home, then one panel with the screen's one
 * `h1`. The heading takes focus when the screen opens and again when it moves to another step, so a screen reader
 * hears where it is. The home link is a document load, never a router link: the gateway decides '/' for a signed-in
 * visitor (frontend.md).
 */
@Component({
  selector: 'ppa-auth-frame',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="frame">
      <p class="brand"><a href="/">PantryPress</a></p>
      <section class="panel" aria-labelledby="auth-heading">
        <h1 id="auth-heading" class="heading" tabindex="-1" #title>{{ heading() }}</h1>
        <ng-content />
      </section>
    </main>
  `,
  styleUrl: './auth-frame.css',
})
export class AuthFrame {
  readonly heading = input.required<string>();
  private readonly element = viewChild.required<ElementRef<HTMLElement>>('title');
  private readonly injector = inject(Injector);

  constructor() {
    this.focusHeading();
  }

  /** Moves focus to the heading once the step it names has been drawn. */
  focusHeading(): void {
    afterNextRender(() => this.element().nativeElement.focus(), { injector: this.injector });
  }
}
