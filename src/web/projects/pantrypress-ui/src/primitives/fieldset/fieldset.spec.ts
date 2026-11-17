import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpField } from '../field/field';
import { PpTextField } from '../text-field/text-field';
import { PpFieldset } from './fieldset';

@Component({
  imports: [PpField, PpFieldset, PpTextField],
  template: `
    <pp-fieldset #set="ppFieldset" legend="Prep time" [error]="error()">
      <pp-field label="Hours">
        <input
          pp-text-field
          [aria-describedby]="set.describedBy()"
          [aria-invalid]="set.invalid()"
        />
      </pp-field>
      <pp-field label="Minutes">
        <input
          pp-text-field
          [aria-describedby]="set.describedBy()"
          [aria-invalid]="set.invalid()"
        />
      </pp-field>
    </pp-fieldset>
  `,
})
class Host {
  readonly error = signal<string | undefined>(undefined);
}

describe('PpFieldset', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, host: fixture.componentInstance };
  }

  it('names its fields with its legend, and shows no error until there is one', async () => {
    const { root } = await render();
    const fieldset = root.querySelector('fieldset')!;
    expect(fieldset.querySelector('legend')?.textContent?.trim()).toBe('Prep time');
    expect(fieldset.querySelectorAll('input')).toHaveLength(2);
    expect(root.querySelector('pp-field-error')).toBeNull();
    for (const input of root.querySelectorAll('input')) {
      expect(input.getAttribute('aria-invalid')).toBeNull();
    }
  });

  it('says one error once, and every field it is about is described by it and marked', async () => {
    const { fixture, root, host } = await render();
    host.error.set('Enter whole hours and minutes, like 1 and 30.');
    await fixture.whenStable();
    const errors = root.querySelectorAll('pp-field-error');
    expect(errors).toHaveLength(1);
    expect(errors[0].textContent).toContain('Error: Enter whole hours and minutes, like 1 and 30.');
    for (const input of root.querySelectorAll('input')) {
      expect(input.getAttribute('aria-invalid')).toBe('true');
      expect(input.getAttribute('aria-describedby')?.split(' ')).toContain(errors[0].id);
    }
    expect(await axeViolations(root)).toEqual([]);
  });
});
