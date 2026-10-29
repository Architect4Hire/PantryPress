import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { axeViolations } from '../../testing/axe';
import { PpChipGroup, PpChipOption } from './chip-group';

type Status = 'draft' | 'scheduled' | 'published';

@Component({
  imports: [PpChipGroup, ReactiveFormsModule],
  template: `
    <pp-chip-group id="status" legend="Status" [options]="statuses" [formControl]="status" />
    <pp-chip-group
      id="when"
      legend="Written"
      multiple="false"
      [options]="periods"
      [formControl]="when"
    />
  `,
})
class Host {
  readonly statuses: PpChipOption<Status>[] = [
    { value: 'draft', label: 'Drafts' },
    { value: 'scheduled', label: 'Scheduled' },
    { value: 'published', label: 'Published' },
  ];
  readonly periods: PpChipOption<number>[] = [
    { value: 0, label: 'Any time' },
    { value: 7, label: 'This week' },
    { value: 30, label: 'This month' },
  ];
  readonly status = new FormControl<readonly Status[] | null>(['published']);
  readonly when = new FormControl<number | null>(0);
}

describe('PpChipGroup', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const inputs = (id: string) => [...root.querySelectorAll<HTMLInputElement>(`#${id} input`)];
    return { fixture, root, inputs, host: fixture.componentInstance };
  }

  it('names its chips with a legend, each chip a native checkbox inside its own label', async () => {
    const { root, inputs } = await render();
    expect(root.querySelector('#status legend')!.textContent!.trim()).toBe('Status');
    expect(root.querySelector('#status fieldset')!.hasAttribute('role')).toBe(false);
    expect(inputs('status').map((input) => input.type)).toEqual([
      'checkbox',
      'checkbox',
      'checkbox',
    ]);
    expect(inputs('status').map((input) => input.closest('label')!.textContent!.trim())).toEqual([
      'Drafts',
      'Scheduled',
      'Published',
    ]);
  });

  it('shows the form value, and writes back every chosen value in the options order', async () => {
    const { fixture, inputs, host } = await render();
    expect(inputs('status').map((input) => input.checked)).toEqual([false, false, true]);
    inputs('status')[0].click();
    expect(host.status.value).toEqual(['draft', 'published']);
    inputs('status')[2].click();
    expect(host.status.value).toEqual(['draft']);
    host.status.setValue(['scheduled']);
    await fixture.whenStable();
    expect(inputs('status').map((input) => input.checked)).toEqual([false, true, false]);
    host.status.setValue(null);
    await fixture.whenStable();
    expect(inputs('status').some((input) => input.checked)).toBe(false);
  });

  it('with multiple off, is a radio group of one typed value, sharing one name', async () => {
    const { fixture, root, inputs, host } = await render();
    expect(root.querySelector('#when fieldset')!.getAttribute('role')).toBe('radiogroup');
    expect(inputs('when').every((input) => input.type === 'radio')).toBe(true);
    expect(new Set(inputs('when').map((input) => input.name)).size).toBe(1);
    expect(inputs('when')[0].name).not.toBe(inputs('status')[0].name);
    expect(inputs('when').map((input) => input.checked)).toEqual([true, false, false]);
    inputs('when')[2].click();
    expect(host.when.value).toBe(30);
    host.when.setValue(null);
    await fixture.whenStable();
    expect(inputs('when').some((input) => input.checked)).toBe(false);
  });

  it('is touched only when focus leaves the whole group', async () => {
    const { inputs, host } = await render();
    inputs('status')[0].dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, relatedTarget: inputs('status')[1] }),
    );
    expect(host.status.touched).toBe(false);
    inputs('status')[1].dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, relatedTarget: null }),
    );
    expect(host.status.touched).toBe(true);
  });

  it('is disabled as a whole by its form control', async () => {
    const { fixture, inputs, host } = await render();
    host.status.disable();
    await fixture.whenStable();
    expect(inputs('status').every((input) => input.disabled)).toBe(true);
    expect(inputs('when').some((input) => input.disabled)).toBe(false);
  });

  it('draws a check mark in every chip, hidden from screen readers, which hear the checked state', async () => {
    const { root } = await render();
    const checks = [...root.querySelectorAll('#status .check')];
    expect(checks).toHaveLength(3);
    expect(checks.every((check) => check.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it('has no accessibility violations, chosen, unchosen, and disabled', async () => {
    const { fixture, root, host } = await render();
    expect(await axeViolations(root)).toEqual([]);
    host.status.setValue([]);
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
    host.status.disable();
    host.when.disable();
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
  });
});
