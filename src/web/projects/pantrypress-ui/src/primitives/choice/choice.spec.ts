import { Component, DestroyRef, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { PpCheckbox } from '../checkbox/checkbox';
import { PpRadioGroup, PpRadioOption } from '../radio-group/radio-group';
import { PpSwitch } from '../switch/switch';

type Layout = 'right' | 'left' | 'single';

/** Every control here is refused by its form, which puts the old value straight back, as a confirm-first form does. */
@Component({
  imports: [PpCheckbox, PpSwitch, PpRadioGroup, ReactiveFormsModule],
  template: `
    <pp-checkbox id="agree" label="I agree" [formControl]="agree" />
    <pp-switch
      id="comments"
      label="Allow comments"
      hint="Readers can reply to posts."
      [formControl]="comments"
    />
    <pp-radio-group id="layout" legend="Sidebar" [options]="layouts" [formControl]="layout" />
    <pp-radio-group id="locked" legend="Width" [options]="widths" [disabled]="locked()" />
    <button id="after" type="button">Next</button>
  `,
})
class Host {
  readonly agree = new FormControl(false, { nonNullable: true });
  readonly comments = new FormControl(true, { nonNullable: true });
  readonly layout = new FormControl<Layout>('left', { nonNullable: true });
  readonly layouts: PpRadioOption<Layout>[] = [
    { value: 'right', label: 'On the right' },
    { value: 'left', label: 'On the left' },
    { value: 'single', label: 'No sidebar' },
  ];
  readonly widths: PpRadioOption<number>[] = [
    { value: 600, label: 'Narrow' },
    { value: 860, label: 'Wide' },
  ];
  readonly locked = signal(true);

  constructor() {
    const refuse = <T>(control: FormControl<T>) => {
      const kept = control.value;
      const subscription = control.valueChanges.subscribe(() =>
        control.setValue(kept, { emitEvent: false }),
      );
      inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
    };
    refuse(this.agree);
    refuse(this.comments);
    refuse(this.layout);
  }
}

describe('choice primitives', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const inputs = (id: string) => [...root.querySelectorAll<HTMLInputElement>(`#${id} input`)];
    return { fixture, root, inputs, host: fixture.componentInstance };
  }

  it('shows a value the form put straight back, for a checkbox, a switch, and a radio group', async () => {
    const { fixture, inputs, host } = await render();
    inputs('agree')[0].click();
    inputs('comments')[0].click();
    inputs('layout')[2].click();
    await fixture.whenStable();
    expect(host.agree.value).toBe(false);
    expect(inputs('agree')[0].checked).toBe(false);
    expect(host.comments.value).toBe(true);
    expect(inputs('comments')[0].checked).toBe(true);
    // Neither radio's binding changes here (left was checked and still is), so only the sync puts the page right.
    expect(host.layout.value).toBe('left');
    expect(inputs('layout').map((radio) => radio.checked)).toEqual([false, true, false]);
  });

  it('describes a switch with its hint', async () => {
    const { root, inputs } = await render();
    const hint = root.querySelector('#comments .hint')!;
    expect(inputs('comments')[0].getAttribute('aria-describedby')).toBe(hint.id);
  });

  it('disables a radio group by its own input', async () => {
    const { fixture, inputs, host } = await render();
    expect(inputs('locked').every((radio) => radio.disabled)).toBe(true);
    host.locked.set(false);
    await fixture.whenStable();
    expect(inputs('locked').some((radio) => radio.disabled)).toBe(false);
  });

  it('marks a radio group touched only when focus leaves the whole group', async () => {
    const { root, inputs, host } = await render();
    const [right, left] = inputs('layout');
    right.focus();
    left.focus();
    expect(host.layout.touched).toBe(false);
    root.querySelector<HTMLButtonElement>('#after')!.focus();
    expect(host.layout.touched).toBe(true);
  });
});
