import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { axeViolations } from '../../testing/axe';
import { PpRadioGroup, PpRadioOption } from './radio-group';

type Layout = 'right' | 'left' | 'single';

@Component({
  imports: [PpRadioGroup, ReactiveFormsModule],
  template: `
    <pp-radio-group
      id="layout"
      legend="Sidebar"
      hint="Where your sidebar sits on a post."
      required
      [options]="layouts"
      [error]="error()"
      [formControl]="layout"
    />
    <pp-radio-group
      id="size"
      legend="Serving size"
      optional
      [options]="sizes"
      [formControl]="size"
    />
  `,
})
class Host {
  readonly layouts: PpRadioOption<Layout>[] = [
    { value: 'right', label: 'On the right' },
    { value: 'left', label: 'On the left' },
    { value: 'single', label: 'No sidebar', hint: 'Posts use the full width.' },
  ];
  readonly sizes: PpRadioOption<number>[] = [
    { value: 2, label: 'Two' },
    { value: 4, label: 'Four' },
  ];
  readonly layout = new FormControl<Layout | null>('left');
  readonly size = new FormControl<number | null>(null);
  readonly error = signal<string | undefined>(undefined);
}

describe('PpRadioGroup', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const radios = (id: string) => [
      ...root.querySelectorAll<HTMLInputElement>(`#${id} input[type="radio"]`),
    ];
    return { fixture, root, radios, host: fixture.componentInstance };
  }

  it('asks its question in a legend, over radios that share one name', async () => {
    const { root, radios } = await render();
    const fieldset = root.querySelector('#layout fieldset')!;
    expect(fieldset.getAttribute('role')).toBe('radiogroup');
    expect(fieldset.querySelector('legend')!.textContent!.trim()).toBe('Sidebar');
    const names = new Set(radios('layout').map((radio) => radio.name));
    expect(names.size).toBe(1);
    expect(radios('size')[0].name).not.toBe(radios('layout')[0].name);
    expect(radios('layout').map((radio) => radio.closest('label')!.textContent!.trim())).toEqual([
      'On the right',
      'On the left',
      'No sidebar',
    ]);
  });

  it('shows the form value and writes the choice back', async () => {
    const { fixture, radios, host } = await render();
    expect(radios('layout').map((radio) => radio.checked)).toEqual([false, true, false]);
    radios('layout')[2].click();
    expect(host.layout.value).toBe('single');
    host.layout.setValue('right');
    await fixture.whenStable();
    expect(radios('layout').map((radio) => radio.checked)).toEqual([true, false, false]);
    radios('layout')[0].dispatchEvent(new Event('blur'));
    expect(host.layout.touched).toBe(true);
  });

  it('keeps typed values, and selects nothing for no value', async () => {
    const { fixture, radios, host } = await render();
    expect(radios('size').some((radio) => radio.checked)).toBe(false);
    radios('size')[1].click();
    expect(host.size.value).toBe(4);
    host.size.setValue(null);
    await fixture.whenStable();
    expect(radios('size').some((radio) => radio.checked)).toBe(false);
  });

  it('describes the group with its hint and error, and each option with its own hint', async () => {
    const { fixture, root, radios, host } = await render();
    const fieldset = root.querySelector('#layout fieldset')!;
    const hint = root.querySelector('#layout .group-hint')!;
    expect(fieldset.getAttribute('aria-describedby')).toBe(hint.id);
    expect(fieldset.getAttribute('aria-required')).toBe('true');
    expect(fieldset.hasAttribute('aria-invalid')).toBe(false);

    const optionHint = root.querySelector(
      `#${radios('layout')[2].getAttribute('aria-describedby')}`,
    )!;
    expect(optionHint.textContent!.trim()).toBe('Posts use the full width.');
    expect(radios('layout')[0].hasAttribute('aria-describedby')).toBe(false);

    host.error.set('Choose where your sidebar goes.');
    await fixture.whenStable();
    const error = root.querySelector('#layout pp-field-error')!;
    expect(fieldset.getAttribute('aria-describedby')).toBe(`${hint.id} ${error.id}`);
    expect(fieldset.getAttribute('aria-invalid')).toBe('true');
    expect(error.textContent!.replace(/\s+/g, ' ').trim()).toBe(
      'Error: Choose where your sidebar goes.',
    );
  });

  it('says in words that an optional group is optional', async () => {
    const { root } = await render();
    expect(root.querySelector('#size legend')!.textContent).toContain('(optional)');
    expect(root.querySelector('#layout legend')!.textContent).not.toContain('(optional)');
  });

  it('is disabled as a whole by its form control', async () => {
    const { fixture, radios, host } = await render();
    host.layout.disable();
    await fixture.whenStable();
    expect(radios('layout').every((radio) => radio.disabled)).toBe(true);
    expect(radios('size').some((radio) => radio.disabled)).toBe(false);
  });

  it('has no accessibility violations, chosen, in error, and disabled', async () => {
    const { fixture, root, host } = await render();
    expect(await axeViolations(root)).toEqual([]);
    host.error.set('Choose where your sidebar goes.');
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
    host.layout.disable();
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
  });
});
