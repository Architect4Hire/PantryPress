import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { installDialogStandIn, pressEscape } from '../../testing/dialog';
import { PpButton } from '../button/button';
import { PpCheckbox } from '../checkbox/checkbox';
import { PpDrawer } from '../drawer/drawer';
import { PpTab, PpTabs } from '../tabs/tabs';
import { PpToastRegion } from '../toast/toast-region';
import { PpToaster } from '../toast/toaster';
import { PpDialog } from './dialog';

@Component({
  imports: [PpDialog, PpDrawer, PpButton, PpCheckbox, PpTabs, PpTab, PpToastRegion],
  template: `
    <button pp-button id="opener" (click)="asking.set(true)">Delete post</button>
    <pp-dialog
      [(open)]="asking"
      heading="Delete this post?"
      kind="alert"
      size="sm"
      [dismissible]="true"
    >
      <p>Readers will no longer find it.</p>
      <div ppDialogActions>
        <button pp-button id="keep" ppInitialFocus (click)="asking.set(false)">Keep it</button>
        <button pp-button id="delete" variant="danger">Delete</button>
      </div>
    </pp-dialog>

    <button pp-button id="filters" (click)="filtering.set(true)">Filters</button>
    <pp-drawer [(open)]="filtering" heading="Filters" [dismissible]="dismissible()">
      <input type="hidden" name="page" value="1" />
      <label for="tag">Tag</label>
      <input id="tag" />
    </pp-drawer>

    <pp-dialog [(open)]="reading" heading="What's new">
      <p>Nothing to press in here.</p>
    </pp-dialog>

    <pp-dialog [(open)]="choosing" heading="Preferences">
      <pp-tabs>
        <pp-tab key="one" label="One"><p>First</p></pp-tab>
        <pp-tab key="two" label="Two"
          ><button id="hidden-choice" type="button">Hidden</button></pp-tab
        >
      </pp-tabs>
      <pp-checkbox id="remember" label="Remember me" ppInitialFocus />
    </pp-dialog>

    @if (kept()) {
      <pp-dialog [(open)]="passing" heading="Passing">
        <button id="inside" type="button">Inside</button>
      </pp-dialog>
    }
    <pp-toast-region />
  `,
})
class Host {
  readonly asking = signal(false);
  readonly filtering = signal(false);
  readonly reading = signal(false);
  readonly choosing = signal(false);
  readonly passing = signal(false);
  readonly kept = signal(true);
  readonly dismissible = signal(false);
}

describe('PpDialog and PpDrawer', () => {
  let restore: () => void;
  beforeEach(() => (restore = installDialogStandIn()));
  afterEach(() => restore());

  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const dialogs = () => [...root.querySelectorAll<HTMLDialogElement>('dialog')];
    const [alert, drawer, plain, prefs] = dialogs();
    const settle = () => fixture.whenStable();
    const click = (target: Element) => {
      target.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      target.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    };
    return {
      fixture,
      root,
      host: fixture.componentInstance,
      alert,
      drawer,
      plain,
      prefs,
      dialogs,
      settle,
      click,
    };
  }

  it('is named by its heading, and an alert dialog is also described by its body', async () => {
    const { alert, drawer, plain } = await render();
    for (const dialog of [alert, drawer, plain]) {
      expect(dialog.getAttribute('aria-labelledby')).toBe(dialog.querySelector('h2')!.id);
    }
    expect(alert.getAttribute('role')).toBe('alertdialog');
    expect(alert.getAttribute('aria-describedby')).toBe(alert.querySelector('.body')!.id);
    expect(alert.querySelector('.body')!.textContent).toContain('Readers will no longer find it.');
    expect(plain.hasAttribute('role')).toBe(false);
    expect(plain.hasAttribute('aria-describedby')).toBe(false);
    expect(alert.closest('pp-dialog')!.getAttribute('data-size')).toBe('sm');
  });

  it('opens as a modal and puts focus on the least destructive action, which it marks', async () => {
    const showModal = vi.spyOn(HTMLDialogElement.prototype, 'showModal');
    const { root, alert, settle } = await render();
    root.querySelector<HTMLButtonElement>('#opener')!.focus();
    root.querySelector<HTMLButtonElement>('#opener')!.click();
    await settle();
    expect(showModal).toHaveBeenCalledTimes(1);
    expect(alert.hasAttribute('open')).toBe(true);
    expect(document.activeElement?.id).toBe('keep');
  });

  it('puts focus on the first control in the body, skipping one that cannot take it, else on the heading', async () => {
    const { host, plain, settle } = await render();
    host.filtering.set(true);
    await settle();
    expect(document.activeElement?.id).toBe('tag');
    host.filtering.set(false);
    host.reading.set(true);
    await settle();
    expect(document.activeElement).toBe(plain.querySelector('h2'));
  });

  it('focuses inside a marked component, and never a control in a hidden panel', async () => {
    const { host, settle } = await render();
    host.choosing.set(true);
    await settle();
    expect(document.activeElement?.closest('pp-checkbox')?.id).toBe('remember');
    expect(document.activeElement?.id).not.toBe('hidden-choice');
  });

  it('closes from its own buttons, from code, and from close(), and sends focus back to the opener', async () => {
    const { root, host, alert, plain, fixture, settle } = await render();
    const opener = root.querySelector<HTMLButtonElement>('#opener')!;
    opener.focus();
    opener.click();
    await settle();
    root.querySelector<HTMLButtonElement>('#keep')!.click();
    await settle();
    expect(alert.hasAttribute('open')).toBe(false);
    expect(host.asking()).toBe(false);
    expect(document.activeElement).toBe(opener);

    opener.click();
    await settle();
    alert.querySelector<HTMLButtonElement>('button[aria-label="Close"]')!.click();
    await settle();
    expect(alert.hasAttribute('open')).toBe(false);
    expect(document.activeElement).toBe(opener);

    host.reading.set(true);
    await settle();
    fixture.debugElement.queryAll((node) => node.name === 'pp-dialog')[1].componentInstance.close();
    await settle();
    expect(plain.hasAttribute('open')).toBe(false);
    expect(host.reading()).toBe(false);
  });

  it('closes on Escape, an alert dialog included, and says so through [(open)]', async () => {
    const { root, host, alert, drawer, settle } = await render();
    for (const [dialog, opener, state] of [
      [drawer, '#filters', host.filtering],
      [alert, '#opener', host.asking],
    ] as const) {
      const button = root.querySelector<HTMLButtonElement>(opener)!;
      button.focus();
      button.click();
      await settle();
      pressEscape(dialog);
      await settle();
      expect(state()).toBe(false);
      expect(dialog.hasAttribute('open')).toBe(false);
      expect(document.activeElement).toBe(button);
    }
  });

  it('closes on a backdrop click only when dismissible, never for an alert, and not after a drag', async () => {
    const { host, alert, drawer, settle, click } = await render();
    host.filtering.set(true);
    await settle();
    click(drawer);
    await settle();
    expect(host.filtering()).toBe(true);
    host.dismissible.set(true);
    await settle();
    click(drawer.querySelector('input#tag')!);
    await settle();
    expect(host.filtering()).toBe(true);
    // Pressed inside (selecting text), let go on the backdrop.
    drawer
      .querySelector('input#tag')!
      .dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    drawer.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await settle();
    expect(host.filtering()).toBe(true);
    click(drawer);
    await settle();
    expect(host.filtering()).toBe(false);

    host.asking.set(true);
    await settle();
    click(alert);
    await settle();
    expect(host.asking()).toBe(true);
  });

  it('ignores a close the browser queued before it was opened again', async () => {
    const { host, plain, settle } = await render();
    host.reading.set(true);
    await settle();
    // The page's own close and reopen ran before the queued close event arrived.
    plain.dispatchEvent(new Event('close'));
    await settle();
    expect(host.reading()).toBe(true);
    expect(plain.hasAttribute('open')).toBe(true);
  });

  it('sends focus back when it is removed while open', async () => {
    const { root, host, settle } = await render();
    const opener = root.querySelector<HTMLButtonElement>('#filters')!;
    opener.focus();
    host.passing.set(true);
    await settle();
    expect(document.activeElement?.id).toBe('inside');
    host.kept.set(false);
    await settle();
    expect(document.activeElement).toBe(opener);
  });

  it('speaks for the page while open, since everything outside it is inert', async () => {
    const { root, host, alert, settle } = await render();
    const toaster = TestBed.inject(PpToaster);
    const shellVoice = () => root.querySelector('pp-toast-region pp-announcer p')?.textContent;
    const alertVoice = () => alert.querySelector('pp-announcer p')?.textContent;
    toaster.announce('Saving.');
    await settle();
    expect(shellVoice()).toBe('Saving.');
    host.asking.set(true);
    await settle();
    toaster.announce('Deleting.');
    await settle();
    expect(alertVoice()).toBe('Deleting.');
    expect(shellVoice()).toBeUndefined();
    pressEscape(alert);
    await settle();
    toaster.announce('Deleted.');
    await settle();
    expect(shellVoice()).toBe('Deleted.');
    expect(alertVoice()).toBeUndefined();
  });

  it('has no accessibility violations, closed and open', async () => {
    const { host, root, settle } = await render();
    expect(await axeViolations(root)).toEqual([]);
    host.asking.set(true);
    await settle();
    expect(await axeViolations(root)).toEqual([]);
    host.asking.set(false);
    host.filtering.set(true);
    await settle();
    expect(await axeViolations(root)).toEqual([]);
  });
});
