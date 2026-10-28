import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpIconButton } from './icon-button';

@Component({
  imports: [PpIconButton],
  template: `
    <button pp-icon-button id="close" label="Close" [busy]="busy()" (click)="clicks = clicks + 1">
      <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true">
        <path d="M4 4l12 12M16 4L4 16" />
      </svg>
    </button>
    <button pp-icon-button id="small" label="More" size="sm" variant="quiet">
      <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true">
        <circle cx="10" cy="10" r="2" />
      </svg>
    </button>
    <button pp-icon-button id="off" label="Delete" variant="danger" disabled>
      <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true">
        <path d="M5 5h10v12H5z" />
      </svg>
    </button>
  `,
})
class Host {
  readonly busy = signal(false);
  clicks = 0;
}

describe('PpIconButton', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, host: fixture.componentInstance };
  }

  it('is named by its label', async () => {
    const { root } = await render();
    const close = root.querySelector('#close')!;
    expect(close.getAttribute('aria-label')).toBe('Close');
    expect(close.getAttribute('type')).toBe('button');
    expect(close.getAttribute('data-variant')).toBe('secondary');
    expect(root.querySelector('#small')!.getAttribute('data-size')).toBe('sm');
  });

  it('cannot render without a label', () => {
    // A template that leaves it out does not compile (strictTemplates); this is the runtime backstop.
    const fixture = TestBed.createComponent(PpIconButton);
    expect(() => fixture.detectChanges()).toThrow(/NG0950/);
  });

  it('shows a spinner in place of its icon and ignores a press while busy', async () => {
    const { fixture, root, host } = await render();
    host.busy.set(true);
    await fixture.whenStable();
    const close = root.querySelector<HTMLButtonElement>('#close')!;
    expect(close.querySelector('svg')).toBeNull();
    expect(close.querySelector('.spinner')).not.toBeNull();
    expect(close.getAttribute('aria-label')).toBe('Close');
    close.click();
    expect(host.clicks).toBe(0);
  });

  it('has no accessibility violations, idle and busy', async () => {
    const { fixture, root, host } = await render();
    expect(await axeViolations(root)).toEqual([]);
    host.busy.set(true);
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
  });
});
