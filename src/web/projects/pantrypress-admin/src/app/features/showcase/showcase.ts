import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  Injector,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  PpButton,
  PpCard,
  PpCheckbox,
  PpChipGroup,
  PpDialog,
  PpDrawer,
  PpEmptyState,
  PpField,
  PpIconButton,
  PpList,
  PpNotice,
  PpPageHeader,
  PpProgress,
  PpRadioGroup,
  PpRadioOption,
  PpSearchField,
  PpSelect,
  PpStatusPill,
  PpSwitch,
  PpTab,
  PpTabs,
  PpTextarea,
  PpTextField,
  PpToaster,
  PpToastOptions,
  PpToolbar,
  PpUploader,
} from '@pantrypress/ui';
import { ShowcasePanel } from './showcase-panel';
import { PpaPanel, PpaSpecimen } from './specimen';

type PageMode = 'auto' | 'light' | 'dark';

/**
 * The `@pantrypress/ui` showcase (PP3.6, ADR 0007): every primitive in every state, each drawn in a light panel and a
 * dark one, for people to look at and for the Playwright run (`e2e/showcase.e2e.ts`) to check as drawn. It exists in
 * development builds only (`dev-routes.ts`). The page's own mode is a choice too, because the toasts and the page
 * around the panels follow `data-mode` on `<html>`, as the dashboard's will.
 */
@Component({
  selector: 'ppa-showcase',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    PpaPanel,
    PpaSpecimen,
    PpButton,
    PpCard,
    PpCheckbox,
    PpChipGroup,
    PpDialog,
    PpDrawer,
    PpEmptyState,
    PpField,
    PpIconButton,
    PpList,
    PpNotice,
    PpPageHeader,
    PpProgress,
    PpRadioGroup,
    PpSearchField,
    PpSelect,
    PpStatusPill,
    PpSwitch,
    PpTab,
    PpTabs,
    PpTextarea,
    PpTextField,
    PpToolbar,
    PpUploader,
  ],
  templateUrl: './showcase.html',
  styleUrl: './showcase.css',
})
export class Showcase {
  protected readonly panels = [new ShowcasePanel('light'), new ShowcasePanel('dark')] as const;

  protected readonly pageModes: readonly PpRadioOption<PageMode>[] = [
    { value: 'auto', label: 'Follow this device' },
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
  ];
  protected readonly pageMode = new FormControl<PageMode>('auto', { nonNullable: true });

  private readonly toaster = inject(PpToaster);
  private readonly injector = inject(Injector);

  constructor() {
    const root = inject(DOCUMENT).documentElement;
    const mode = toSignal(this.pageMode.valueChanges, { initialValue: this.pageMode.value });
    // No attribute is `auto` (tokens.css), which is how the dashboard will start; leaving puts the page back so.
    effect(() => {
      const chosen = mode();
      if (chosen === 'auto') {
        root.removeAttribute('data-mode');
      } else {
        root.setAttribute('data-mode', chosen);
      }
    });
    inject(DestroyRef).onDestroy(() => root.removeAttribute('data-mode'));
  }

  /** Dismissing the tip removes the button that held focus, so focus goes to the button that brings it back. */
  protected dismissTip(panel: ShowcasePanel, tip: HTMLElement): void {
    panel.noticeShown.set(false);
    this.focusAfterRender(tip, 'button');
  }

  /** The tip that returns is announced, and focus goes to its Dismiss button, in place of the one that went. */
  protected returnTip(panel: ShowcasePanel, tip: HTMLElement): void {
    panel.tipReturned.set(true);
    panel.noticeShown.set(true);
    this.focusAfterRender(tip, 'pp-notice button');
  }

  protected toast(options: PpToastOptions): void {
    this.toaster.show(options);
  }

  protected toastWithUndo(): void {
    this.toaster.show({
      message: 'Apple pie moved to the bin.',
      tone: 'info',
      action: {
        label: 'Undo',
        run: () => this.toaster.show({ message: 'Apple pie is back.', tone: 'success' }),
      },
    });
  }

  private focusAfterRender(within: HTMLElement, selector: string): void {
    afterNextRender(() => within.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
