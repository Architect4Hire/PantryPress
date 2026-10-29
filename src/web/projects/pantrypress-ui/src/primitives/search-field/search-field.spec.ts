import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { axeViolations } from '../../testing/axe';
import { PpSearchField } from './search-field';

@Component({
  imports: [PpSearchField, ReactiveFormsModule],
  template: `
    <pp-search-field
      id="posts"
      label="Search posts"
      hint="Titles and words in a post."
      [(query)]="query"
      (searched)="searches.push($event)"
    />
    <pp-search-field id="recipes" label="Search recipes" [formControl]="recipes" />
  `,
})
class Host {
  readonly query = signal('');
  readonly searches: string[] = [];
  readonly recipes = new FormControl<string | null>('pie');
}

describe('PpSearchField', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const box = (id: string) => root.querySelector<HTMLInputElement>(`#${id} input`)!;
    const type = (id: string, value: string) => {
      box(id).value = value;
      box(id).dispatchEvent(new Event('input', { bubbles: true }));
    };
    const press = (id: string, key: string) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      box(id).dispatchEvent(event);
      return event;
    };
    const button = (id: string, name: string) =>
      [...root.querySelectorAll<HTMLButtonElement>(`#${id} button`)].find(
        (candidate) =>
          (candidate.getAttribute('aria-label') ?? candidate.textContent!.trim()) === name,
      );
    return { fixture, root, box, type, press, button, host: fixture.componentInstance };
  }

  it('is a search landmark with a labelled search input, described by its hint', async () => {
    const { root, box } = await render();
    const landmark = root.querySelector('#posts [role="search"]')!;
    expect(landmark.getAttribute('aria-label')).toBe('Search posts');
    expect(box('posts').type).toBe('search');
    expect(root.querySelector(`label[for="${box('posts').id}"]`)!.textContent!.trim()).toBe(
      'Search posts',
    );
    const hint = root.querySelector(`#${box('posts').getAttribute('aria-describedby')}`)!;
    expect(hint.textContent!.trim()).toBe('Titles and words in a post.');
    expect(box('recipes').hasAttribute('aria-describedby')).toBe(false);
  });

  it('follows every keystroke in its query, and searches only on Enter or Search, trimmed', async () => {
    const { fixture, type, press, button, host } = await render();
    type('posts', '  apple pie ');
    expect(host.query()).toBe('  apple pie ');
    expect(host.searches).toEqual([]);
    expect(press('posts', 'Enter').defaultPrevented).toBe(true);
    await fixture.whenStable();
    button('posts', 'Search')!.click();
    expect(host.searches).toEqual(['apple pie', 'apple pie']);
  });

  it('searches for everything once the field is emptied, by Clear, by Escape, or by deleting', async () => {
    const { fixture, box, type, press, button, host } = await render();
    expect(button('posts', 'Clear search')).toBeUndefined();
    type('posts', 'pie');
    await fixture.whenStable();
    button('posts', 'Clear search')!.click();
    await fixture.whenStable();
    expect(host.query()).toBe('');
    expect(box('posts').value).toBe('');
    expect(document.activeElement).toBe(box('posts'));

    type('posts', 'tart');
    expect(press('posts', 'Escape').defaultPrevented).toBe(true);
    await fixture.whenStable();
    expect(box('posts').value).toBe('');

    type('posts', 'cake');
    type('posts', '');
    expect(host.searches).toEqual(['', '', '']);
  });

  it('leaves Escape alone when there is nothing to clear, so a dialog around it still closes', async () => {
    const { press, host } = await render();
    expect(press('posts', 'Escape').defaultPrevented).toBe(false);
    expect(host.searches).toEqual([]);
  });

  it('reads and writes a reactive form, is touched on leaving, and is disabled by it', async () => {
    const { fixture, box, type, button, host } = await render();
    expect(box('recipes').value).toBe('pie');
    type('recipes', 'pies');
    expect(host.recipes.value).toBe('pies');
    box('recipes').dispatchEvent(new FocusEvent('blur'));
    expect(host.recipes.touched).toBe(true);
    host.recipes.setValue('cake');
    await fixture.whenStable();
    expect(box('recipes').value).toBe('cake');
    host.recipes.disable();
    await fixture.whenStable();
    expect(box('recipes').disabled).toBe(true);
    expect(button('recipes', 'Search')!.disabled).toBe(true);
    expect(button('recipes', 'Clear search')).toBeUndefined();
  });

  it('has no accessibility violations, empty, with words, and disabled', async () => {
    const { fixture, root, type, host } = await render();
    expect(await axeViolations(root)).toEqual([]);
    type('posts', 'pie');
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
    host.recipes.disable();
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
  });
});
