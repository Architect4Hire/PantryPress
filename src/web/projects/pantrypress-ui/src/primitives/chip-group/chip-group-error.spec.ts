import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { axeViolations } from '../../testing/axe';
import { PpChipGroup } from './chip-group';

@Component({
  imports: [PpChipGroup, ReactiveFormsModule],
  template: `
    <pp-chip-group legend="Course" [options]="options" [formControl]="chosen" [error]="error()" />
  `,
})
class Host {
  readonly options = [
    { value: 'dessert', label: 'Dessert' },
    { value: 'bread', label: 'Bread' },
  ];
  readonly chosen = new FormControl<readonly string[]>([], { nonNullable: true });
  readonly error = signal<string | undefined>(undefined);
}

describe('PpChipGroup with an error (PP4.13)', () => {
  it('describes and marks the group with its error, said inside it, and nothing without one', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const fieldset = root.querySelector('fieldset')!;
    expect(fieldset.getAttribute('aria-invalid')).toBeNull();
    expect(fieldset.getAttribute('aria-describedby')).toBeNull();

    fixture.componentInstance.error.set('Choose up to 5 courses.');
    await fixture.whenStable();
    const error = fieldset.querySelector('pp-field-error')!;
    expect(error.textContent).toContain('Error: Choose up to 5 courses.');
    expect(fieldset.getAttribute('aria-invalid')).toBe('true');
    expect(fieldset.getAttribute('aria-describedby')).toBe(error.id);
    expect(await axeViolations(root)).toEqual([]);
  });
});
