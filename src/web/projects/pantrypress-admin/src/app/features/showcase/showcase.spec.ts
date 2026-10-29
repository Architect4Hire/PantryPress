import { reflectComponentType, Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import * as ui from '@pantrypress/ui';
import axe from 'axe-core';
import { Showcase } from './showcase';

/** Every component `@pantrypress/ui` exports, by its selector; the toaster is a service and has none. */
const components = (Object.values(ui) as unknown[])
  .filter((value): value is Type<unknown> => typeof value === 'function')
  .map((type) => reflectComponentType(type))
  .filter((mirror) => mirror !== null);

describe('Showcase', () => {
  let fixture: ComponentFixture<Showcase>;
  let root: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    fixture = TestBed.createComponent(Showcase);
    await fixture.whenStable();
    root = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => document.documentElement.removeAttribute('data-mode'));

  const inMode = (mode: 'light' | 'dark', selector: string) =>
    [...root.querySelectorAll(`.panel[data-mode="${mode}"]`)].some((panel) =>
      panel.querySelector(selector),
    );

  it('draws every component the library exports, in a light panel and a dark one', () => {
    expect(components.length).toBeGreaterThanOrEqual(24);
    const missing = components
      .filter((component) => component.selector !== 'pp-toast-region')
      .flatMap((component) =>
        (['light', 'dark'] as const)
          .filter((mode) => !inMode(mode, component.selector))
          .map((mode) => `${component.selector} (${mode})`),
      );
    expect(missing).toEqual([]);
  });

  it('draws no toast region of its own: the app shell holds the one an app has (PP3.8)', () => {
    expect(root.querySelectorAll('pp-toast-region')).toHaveLength(0);
  });

  it('sets the page mode on <html>, and puts it back to none on leaving', async () => {
    const html = document.documentElement;
    const choose = async (label: string) => {
      const radio = [...root.querySelectorAll<HTMLLabelElement>('.page-mode label')].find(
        (option) => option.textContent?.trim() === label,
      );
      radio!.querySelector('input')!.click();
      await fixture.whenStable();
    };

    expect(html.hasAttribute('data-mode')).toBe(false);
    await choose('Dark');
    expect(html.getAttribute('data-mode')).toBe('dark');
    await choose('Follow this device');
    expect(html.hasAttribute('data-mode')).toBe(false);
    await choose('Light');
    fixture.destroy();
    expect(html.hasAttribute('data-mode')).toBe(false);
  });

  it("keeps each panel's state its own", async () => {
    const tab = (mode: string, label: string) =>
      [...root.querySelectorAll<HTMLElement>(`.panel[data-mode="${mode}"] [role="tab"]`)].find(
        (item) => item.textContent?.trim() === label,
      )!;

    tab('light', 'Drafts').click();
    await fixture.whenStable();
    expect(tab('light', 'Drafts').getAttribute('aria-selected')).toBe('true');
    expect(tab('dark', 'Drafts').getAttribute('aria-selected')).toBe('false');
    expect(tab('dark', 'Published').getAttribute('aria-selected')).toBe('true');
  });

  it('passes axe (contrast is the browser run’s, since jsdom draws nothing)', async () => {
    const results = await axe.run(root, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
      rules: { 'color-contrast': { enabled: false } },
    });
    expect(
      results.violations.map(
        (violation) =>
          `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
      ),
    ).toEqual([]);
  }, 30_000);
});
