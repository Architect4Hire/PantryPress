import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { axeViolations } from '../../testing/axe';
import { PpField } from '../field/field';
import { PpSelect } from '../select/select';
import { PpTextField } from '../text-field/text-field';
import { PpTextarea } from '../textarea/textarea';

interface Plan {
  readonly id: number;
  readonly name: string;
}

@Component({
  imports: [PpField, PpTextField, PpTextarea, PpSelect, ReactiveFormsModule],
  template: `
    <pp-field label="Tagline" hint="A line under your blog's name." [error]="error()">
      <input pp-text-field id="inside" aria-describedby="tagline-count" />
    </pp-field>
    <p id="tagline-count">40 characters left</p>

    <label for="outside">Search posts</label>
    <input
      pp-text-field
      id="outside"
      aria-describedby="search-help"
      [aria-invalid]="searchInvalid()"
    />
    <p id="search-help">Search by title or ingredient.</p>

    <pp-field label="Plan">
      <select pp-select [formControl]="plan">
        @for (option of plans; track option.id) {
          <option [ngValue]="option">{{ option.name }}</option>
        }
      </select>
    </pp-field>
  `,
})
class Host {
  readonly error = signal<string | undefined>(undefined);
  readonly searchInvalid = signal(false);
  readonly plans: Plan[] = [
    { id: 1, name: 'Starter' },
    { id: 2, name: 'Plus' },
  ];
  readonly plan = new FormControl<Plan | null>(this.plans[1]);
}

describe('PpControl', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, host: fixture.componentInstance };
  }

  it("keeps a caller's own description after the field's hint and error", async () => {
    const { fixture, root, host } = await render();
    const inside = root.querySelector<HTMLInputElement>('pp-field input')!;
    const hint = root.querySelector('pp-field .hint')!;
    expect(inside.getAttribute('aria-describedby')).toBe(`${hint.id} tagline-count`);
    host.error.set('Keep it under 120 characters.');
    await fixture.whenStable();
    const error = root.querySelector('pp-field pp-field-error')!;
    expect(inside.getAttribute('aria-describedby')).toBe(`${hint.id} ${error.id} tagline-count`);
  });

  it("keeps a caller's description and invalid state outside a field", async () => {
    const { fixture, root, host } = await render();
    const outside = root.querySelector('#outside')!;
    expect(outside.getAttribute('aria-describedby')).toBe('search-help');
    expect(outside.hasAttribute('aria-invalid')).toBe(false);
    host.searchInvalid.set(true);
    await fixture.whenStable();
    expect(outside.getAttribute('aria-invalid')).toBe('true');
  });

  it('works with options made by @for and [ngValue]', async () => {
    const { fixture, root, host } = await render();
    const select = root.querySelector<HTMLSelectElement>('select')!;
    expect(select.options[select.selectedIndex].textContent!.trim()).toBe('Plus');
    select.selectedIndex = 0;
    select.dispatchEvent(new Event('change'));
    expect(host.plan.value).toBe(host.plans[0]);
    host.plan.setValue(host.plans[1]);
    await fixture.whenStable();
    expect(select.selectedIndex).toBe(1);
  });

  it('has no accessibility violations', async () => {
    const { root } = await render();
    expect(await axeViolations(root)).toEqual([]);
  });
});
