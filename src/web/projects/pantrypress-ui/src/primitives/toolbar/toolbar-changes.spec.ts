import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PpButton } from '../button/button';
import { PpTab, PpTabs } from '../tabs/tabs';
import { PpToolbar } from './toolbar';

@Component({
  imports: [PpToolbar, PpButton, PpTabs, PpTab],
  template: `
    <pp-toolbar label="Formatting">
      @if (bold()) {
        <button pp-button id="bold" size="sm">Bold</button>
      }
      <button pp-button id="italic" size="sm" [disabled]="italicOff()">Italic</button>
      <input id="size" aria-label="Size" value="16" />
      <button pp-button id="link" size="sm">Link</button>
      @if (extra()) {
        <button pp-button id="extra" size="sm">Extra</button>
      }
    </pp-toolbar>
    <pp-tabs [(selected)]="selected">
      <pp-tab key="one" label="One"><p>One</p></pp-tab>
      <pp-tab key="two" label="Two"><p>Two</p></pp-tab>
    </pp-tabs>
  `,
})
class Host {
  readonly bold = signal(true);
  readonly italicOff = signal(true);
  readonly extra = signal(false);
  readonly selected = signal<string | undefined>('no-such-tab');
}

describe('PpToolbar and PpTabs as things change', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const stops = () =>
      [...root.querySelectorAll<HTMLElement>('pp-toolbar button')]
        .filter((button) => button.getAttribute('tabindex') === '0')
        .map((button) => button.id);
    const key = (target: Element, init: KeyboardEventInit) => {
      const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
      target.dispatchEvent(event);
      return event;
    };
    return {
      fixture,
      root,
      stops,
      key,
      host: fixture.componentInstance,
      settle: () => fixture.whenStable(),
    };
  }

  it('keeps exactly one tab stop when its stop goes, a control is enabled, or one is added', async () => {
    const { stops, host, settle } = await render();
    expect(stops()).toEqual(['bold']);
    host.bold.set(false);
    await settle();
    expect(stops()).toEqual(['link']);
    host.italicOff.set(false);
    host.extra.set(true);
    await settle();
    // The stop stays where it is; the newly enabled and the newly added controls are reached by the arrow keys.
    expect(stops()).toEqual(['link']);
  });

  it('leaves a text field its own arrow keys', async () => {
    const { root, key } = await render();
    const size = root.querySelector<HTMLInputElement>('#size')!;
    size.focus();
    expect(key(size, { key: 'ArrowLeft' }).defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(size);
  });

  it('leaves Alt, Control, and Meta with the arrow keys to the browser', async () => {
    const { root, key } = await render();
    const bold = root.querySelector<HTMLButtonElement>('#bold')!;
    bold.focus();
    for (const modifier of ['altKey', 'ctrlKey', 'metaKey'] as const) {
      expect(key(bold, { key: 'ArrowRight', [modifier]: true }).defaultPrevented, modifier).toBe(
        false,
      );
      expect(document.activeElement).toBe(bold);
    }
    const tab = root.querySelector<HTMLButtonElement>('[role="tab"]')!;
    tab.focus();
    expect(key(tab, { key: 'ArrowRight', altKey: true }).defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(tab);
  });

  it('selects the first tab when the key it is given names no tab', async () => {
    const { root } = await render();
    const tabs = [...root.querySelectorAll('[role="tab"]')];
    expect(tabs.map((tab) => tab.getAttribute('aria-selected'))).toEqual(['true', 'false']);
  });
});
