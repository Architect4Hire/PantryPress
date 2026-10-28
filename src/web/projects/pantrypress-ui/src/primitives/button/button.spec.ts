import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpButtonVariant } from './button-base';
import { PpButton } from './button';

@Component({
  imports: [PpButton],
  template: `
    <form (submit)="submitted = submitted + 1; $event.preventDefault()">
      <button pp-button id="plain" (click)="clicks = clicks + 1">Cancel</button>
      <button
        pp-button
        id="chosen"
        [variant]="variant()"
        [busy]="busy()"
        size="sm"
        type="submit"
        (click)="clicks = clicks + 1"
      >
        Save
      </button>
      <button pp-button id="off" variant="primary" disabled>Publish</button>
      <a pp-button id="link" variant="primary" href="/sign-up">Sign up</a>
    </form>
  `,
})
class Host {
  readonly variant = signal<PpButtonVariant>('primary');
  readonly busy = signal(false);
  clicks = 0;
  submitted = 0;
}

describe('PpButton', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const get = (id: string) => root.querySelector<HTMLElement>(`#${id}`)!;
    return { fixture, root, get, host: fixture.componentInstance };
  }

  it('is a secondary, medium button that never submits by accident', async () => {
    const { get } = await render();
    const plain = get('plain');
    expect(plain.dataset['variant']).toBe('secondary');
    expect(plain.dataset['size']).toBe('md');
    expect(plain.getAttribute('type')).toBe('button');
  });

  it('takes the variant, size, and type it is given', async () => {
    const { get } = await render();
    const chosen = get('chosen');
    expect(chosen.dataset['variant']).toBe('primary');
    expect(chosen.dataset['size']).toBe('sm');
    expect(chosen.getAttribute('type')).toBe('submit');
  });

  it('puts no type on a link', async () => {
    const { get } = await render();
    const link = get('link');
    expect(link.hasAttribute('type')).toBe(false);
    expect(link.dataset['variant']).toBe('primary');
    expect(link.getAttribute('href')).toBe('/sign-up');
  });

  it('runs its click and submits its form when it is not busy', async () => {
    const { get, host } = await render();
    get('chosen').click();
    expect(host.clicks).toBe(1);
    expect(host.submitted).toBe(1);
  });

  it('stays focusable while busy, says so, and ignores a press', async () => {
    const { fixture, get, host } = await render();
    host.busy.set(true);
    await fixture.whenStable();
    const chosen = get('chosen') as HTMLButtonElement;
    expect(chosen.getAttribute('aria-busy')).toBe('true');
    expect(chosen.getAttribute('aria-disabled')).toBe('true');
    expect(chosen.disabled).toBe(false);
    expect(chosen.querySelector('.spinner')?.getAttribute('aria-hidden')).toBe('true');
    chosen.click();
    expect(host.clicks).toBe(0);
    expect(host.submitted).toBe(0);
  });

  it('drops the busy state when the work is done', async () => {
    const { fixture, get, host } = await render();
    host.busy.set(true);
    await fixture.whenStable();
    host.busy.set(false);
    await fixture.whenStable();
    const chosen = get('chosen');
    expect(chosen.hasAttribute('aria-busy')).toBe(false);
    expect(chosen.querySelector('.spinner')).toBeNull();
  });

  it.each(['primary', 'secondary', 'quiet', 'danger'] as const)(
    'has no accessibility violations as %s, idle and busy',
    async (variant) => {
      const { fixture, root, host } = await render();
      host.variant.set(variant);
      await fixture.whenStable();
      expect(await axeViolations(root)).toEqual([]);
      host.busy.set(true);
      await fixture.whenStable();
      expect(await axeViolations(root)).toEqual([]);
    },
  );
});
