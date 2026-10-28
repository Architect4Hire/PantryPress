import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpTab, PpTabs } from './tabs';

@Component({
  imports: [PpTabs, PpTab],
  template: `
    <div [attr.dir]="dir()">
      <pp-tabs [(selected)]="selected" label="Settings">
        <pp-tab key="general" label="General"><p>Name and tagline</p></pp-tab>
        <pp-tab key="design" label="Design"><p>Colors and fonts</p></pp-tab>
        <pp-tab key="ads" label="Ads"><p>Ad slots</p></pp-tab>
      </pp-tabs>
    </div>
  `,
})
class Host {
  readonly selected = signal<string | undefined>(undefined);
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
}

describe('PpTabs', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const tabs = () => [...root.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
    const panels = () => [...root.querySelectorAll<HTMLElement>('[role="tabpanel"]')];
    const press = async (key: string) => {
      (document.activeElement as HTMLElement).dispatchEvent(
        new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
      );
      await fixture.whenStable();
    };
    return { fixture, root, tabs, panels, press, host: fixture.componentInstance };
  }

  it('ties each tab to its panel, and selects the first when nothing is chosen', async () => {
    const { root, tabs, panels } = await render();
    expect(root.querySelector('[role="tablist"]')!.getAttribute('aria-label')).toBe('Settings');
    tabs().forEach((tab, index) => {
      expect(tab.getAttribute('aria-controls')).toBe(panels()[index].id);
      expect(panels()[index].getAttribute('aria-labelledby')).toBe(tab.id);
    });
    expect(tabs().map((tab) => tab.getAttribute('aria-selected'))).toEqual([
      'true',
      'false',
      'false',
    ]);
    expect(panels().map((panel) => panel.hidden)).toEqual([false, true, true]);
  });

  it('is one tab stop', async () => {
    const { tabs } = await render();
    expect(tabs().map((tab) => tab.tabIndex)).toEqual([0, -1, -1]);
  });

  it('moves and selects with the arrow keys, wrapping, and with Home and End', async () => {
    const { tabs, panels, press, host } = await render();
    tabs()[0].focus();
    await press('ArrowRight');
    expect(document.activeElement).toBe(tabs()[1]);
    expect(host.selected()).toBe('design');
    expect(panels().map((panel) => panel.hidden)).toEqual([true, false, true]);
    expect(tabs().map((tab) => tab.tabIndex)).toEqual([-1, 0, -1]);
    await press('End');
    expect(host.selected()).toBe('ads');
    await press('ArrowRight');
    expect(host.selected()).toBe('general');
    await press('ArrowLeft');
    expect(host.selected()).toBe('ads');
    await press('Home');
    expect(document.activeElement).toBe(tabs()[0]);
    expect(host.selected()).toBe('general');
  });

  it('turns the arrow keys around in a right-to-left page', async () => {
    const { fixture, tabs, press, host } = await render();
    host.dir.set('rtl');
    await fixture.whenStable();
    tabs()[0].focus();
    await press('ArrowLeft');
    expect(host.selected()).toBe('design');
    await press('ArrowRight');
    expect(host.selected()).toBe('general');
  });

  it('follows [(selected)] and a click', async () => {
    const { fixture, tabs, panels, host } = await render();
    host.selected.set('ads');
    await fixture.whenStable();
    expect(panels()[2].hidden).toBe(false);
    tabs()[1].click();
    await fixture.whenStable();
    expect(host.selected()).toBe('design');
    expect(panels()[1].hidden).toBe(false);
  });

  it('ignores keys it does not use', async () => {
    const { tabs, press, host } = await render();
    tabs()[0].focus();
    await press('a');
    expect(host.selected()).toBeUndefined();
    expect(document.activeElement).toBe(tabs()[0]);
  });

  it('has no accessibility violations on any tab', async () => {
    const { fixture, root, host } = await render();
    for (const key of ['general', 'design', 'ads']) {
      host.selected.set(key);
      await fixture.whenStable();
      expect(await axeViolations(root), key).toEqual([]);
    }
  });
});
