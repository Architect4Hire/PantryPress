import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { axeViolations } from '../../testing/axe';
import { PpSwitch } from '../switch/switch';
import { PpCheckbox } from './checkbox';

@Component({
  imports: [PpCheckbox, PpSwitch, ReactiveFormsModule],
  template: `
    <pp-checkbox
      id="terms"
      label="I agree to the terms"
      hint="You can read them before you agree."
      [formControl]="terms"
    />
    <pp-checkbox id="plain" label="Show drafts" [(checked)]="showDrafts" [disabled]="locked()" />
    <pp-switch
      id="comments"
      label="Allow comments"
      hint="Readers can reply to posts."
      [formControl]="comments"
    />
    <pp-switch id="ratings" label="Allow ratings" [(checked)]="ratings" />
  `,
})
class Host {
  readonly terms = new FormControl(false, { nonNullable: true });
  readonly comments = new FormControl(true, { nonNullable: true });
  readonly showDrafts = signal(false);
  readonly ratings = signal(true);
  readonly locked = signal(false);
}

describe('PpCheckbox and PpSwitch', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const input = (id: string) => root.querySelector<HTMLInputElement>(`#${id} input`)!;
    return { fixture, root, input, host: fixture.componentInstance };
  }

  it('wraps the box in its label, so the words name it and can be pressed', async () => {
    const { input } = await render();
    const terms = input('terms');
    expect(terms.type).toBe('checkbox');
    expect(terms.closest('label')!.textContent!.trim()).toBe('I agree to the terms');
  });

  it('describes the box with its hint', async () => {
    const { root, input } = await render();
    const hint = root.querySelector('#terms .hint')!;
    expect(input('terms').getAttribute('aria-describedby')).toBe(hint.id);
    expect(input('plain').hasAttribute('aria-describedby')).toBe(false);
  });

  it('reads and writes a reactive form control', async () => {
    const { fixture, input, host } = await render();
    expect(input('terms').checked).toBe(false);
    input('terms').click();
    expect(host.terms.value).toBe(true);
    host.terms.setValue(false);
    await fixture.whenStable();
    expect(input('terms').checked).toBe(false);
    expect(host.terms.touched).toBe(false);
    input('terms').dispatchEvent(new Event('blur'));
    expect(host.terms.touched).toBe(true);
  });

  it('is disabled by its form control or by its input', async () => {
    const { fixture, input, host } = await render();
    host.terms.disable();
    host.locked.set(true);
    await fixture.whenStable();
    expect(input('terms').disabled).toBe(true);
    expect(input('plain').disabled).toBe(true);
    host.terms.enable();
    await fixture.whenStable();
    expect(input('terms').disabled).toBe(false);
  });

  it('works without a form through [(checked)]', async () => {
    const { fixture, input, host } = await render();
    input('plain').click();
    expect(host.showDrafts()).toBe(true);
    // The app refreshes after every press; the parent's own [(checked)] binding only learns the new value then.
    await fixture.whenStable();
    host.showDrafts.set(false);
    await fixture.whenStable();
    expect(input('plain').checked).toBe(false);
  });

  it('is a switch to a screen reader when it is a switch', async () => {
    const { fixture, input, host } = await render();
    const comments = input('comments');
    expect(comments.getAttribute('role')).toBe('switch');
    expect(comments.type).toBe('checkbox');
    expect(comments.checked).toBe(true);
    comments.click();
    expect(host.comments.value).toBe(false);
    input('ratings').click();
    expect(host.ratings()).toBe(false);
    await fixture.whenStable();
    host.ratings.set(true);
    await fixture.whenStable();
    expect(input('ratings').checked).toBe(true);
  });

  it('has no accessibility violations, on, off, and disabled', async () => {
    const { fixture, root, input, host } = await render();
    expect(await axeViolations(root)).toEqual([]);
    input('terms').click();
    input('comments').click();
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
    host.terms.disable();
    host.comments.disable();
    host.locked.set(true);
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
  });
});
