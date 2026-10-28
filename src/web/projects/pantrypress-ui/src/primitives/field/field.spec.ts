import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { axeViolations } from '../../testing/axe';
import { PpSelect } from '../select/select';
import { PpTextField } from '../text-field/text-field';
import { PpTextarea } from '../textarea/textarea';
import { PpField } from './field';

@Component({
  imports: [PpField, PpTextField, PpTextarea, PpSelect, ReactiveFormsModule],
  template: `
    <pp-field id="name-field" label="Blog name" [hint]="hint()" [error]="error()">
      <input pp-text-field id="ignored" [formControl]="name" autocomplete="off" required />
    </pp-field>
    <pp-field id="about-field" label="About you" optional>
      <textarea pp-textarea [formControl]="about"></textarea>
    </pp-field>
    <pp-field id="mode-field" label="Color mode">
      <select pp-select [formControl]="mode">
        <option value="auto">Match my device</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
    </pp-field>
    <input pp-text-field id="search" aria-label="Search posts" />
  `,
})
class Host {
  readonly hint = signal<string | undefined>('Readers see this at the top of every page.');
  readonly error = signal<string | undefined>(undefined);
  readonly name = new FormControl('Pie Day', { nonNullable: true });
  readonly about = new FormControl('', { nonNullable: true });
  readonly mode = new FormControl('light', { nonNullable: true });
}

describe('PpField', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const field = (id: string) => root.querySelector<HTMLElement>(`#${id}`)!;
    const control = (id: string) =>
      field(id).querySelector<HTMLInputElement>('input, textarea, select')!;
    return { fixture, root, field, control, host: fixture.componentInstance };
  }

  it('labels its control, whatever id the control was given', async () => {
    const { field, control } = await render();
    for (const id of ['name-field', 'about-field', 'mode-field']) {
      const label = field(id).querySelector('label')!;
      expect(label.getAttribute('for'), id).toBe(control(id).id);
      expect(control(id).id, id).toMatch(/^pp-field-\d+$/);
    }
    expect(field('name-field').querySelector('label')!.textContent!.trim()).toBe('Blog name');
  });

  it('gives each field its own id', async () => {
    const { control } = await render();
    const ids = ['name-field', 'about-field', 'mode-field'].map((id) => control(id).id);
    expect(new Set(ids).size).toBe(3);
  });

  it('describes the control with its hint, and its error when there is one', async () => {
    const { fixture, field, control, host } = await render();
    const input = control('name-field');
    const hint = field('name-field').querySelector('.hint')!;
    expect(input.getAttribute('aria-describedby')).toBe(hint.id);
    expect(input.hasAttribute('aria-invalid')).toBe(false);

    host.error.set('Give your blog a name.');
    await fixture.whenStable();
    const error = field('name-field').querySelector('pp-field-error')!;
    expect(input.getAttribute('aria-describedby')).toBe(`${hint.id} ${error.id}`);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(error.textContent!.replace(/\s+/g, ' ').trim()).toBe('Error: Give your blog a name.');
    expect(error.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');

    host.hint.set(undefined);
    host.error.set(undefined);
    await fixture.whenStable();
    expect(input.hasAttribute('aria-describedby')).toBe(false);
    expect(input.hasAttribute('aria-invalid')).toBe(false);
    expect(field('name-field').querySelector('.hint, pp-field-error')).toBeNull();
  });

  it('says in words that an optional field is optional', async () => {
    const { field } = await render();
    expect(field('about-field').querySelector('label')!.textContent).toContain('(optional)');
    expect(field('name-field').querySelector('label')!.textContent).not.toContain('(optional)');
  });

  it('leaves native attributes and reactive forms working', async () => {
    const { fixture, field, control, host } = await render();
    const input = control('name-field');
    expect(input.value).toBe('Pie Day');
    expect(input.required).toBe(true);
    expect(input.getAttribute('autocomplete')).toBe('off');
    input.value = 'Pie Night';
    input.dispatchEvent(new Event('input'));
    expect(host.name.value).toBe('Pie Night');

    const select = control('mode-field') as unknown as HTMLSelectElement;
    expect(select.value).toBe('light');
    select.value = 'dark';
    select.dispatchEvent(new Event('change'));
    expect(host.mode.value).toBe('dark');

    host.about.disable();
    await fixture.whenStable();
    expect((field('about-field').querySelector('textarea') as HTMLTextAreaElement).disabled).toBe(
      true,
    );
  });

  it('keeps the id it was given outside a field', async () => {
    const { root } = await render();
    const search = root.querySelector('input[aria-label="Search posts"]')!;
    expect(search.id).toBe('search');
    expect(search.hasAttribute('aria-describedby')).toBe(false);
  });

  it('has no accessibility violations with a hint, with an error, and disabled', async () => {
    const { fixture, root, host } = await render();
    expect(await axeViolations(root)).toEqual([]);
    host.error.set('Give your blog a name.');
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
    host.name.disable();
    host.mode.disable();
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
  });
});
