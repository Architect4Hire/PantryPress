import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, RouterLink } from '@angular/router';
import { axeViolations } from '../../testing/axe';
import { PpIconButton } from '../icon-button/icon-button';
import { PpButton } from './button';

@Component({ template: '' })
class Blank {}

@Component({
  imports: [PpButton, PpIconButton, RouterLink],
  template: `
    <a pp-button id="link" variant="primary" routerLink="/next" [busy]="busy()">Continue</a>
    <a pp-icon-button id="icon-link" label="Settings" routerLink="/settings">
      <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true">
        <circle cx="10" cy="10" r="3" />
      </svg>
    </a>
    <button pp-icon-button id="icon" label="Refresh" [busy]="busy()">
      <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true">
        <path d="M4 10a6 6 0 1 0 2-4.5" />
      </svg>
    </button>
  `,
})
class Host {
  readonly busy = signal(false);
}

describe('PpButton on a link, and PpIconButton', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'next', component: Blank },
          { path: 'settings', component: Blank },
        ]),
      ],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, host: fixture.componentInstance, router: TestBed.inject(Router) };
  }

  it('follows its routerLink when it is not busy', async () => {
    const { fixture, root, router } = await render();
    root.querySelector<HTMLAnchorElement>('#link')!.click();
    await fixture.whenStable();
    expect(router.url).toBe('/next');
  });

  it('does not navigate while busy', async () => {
    const { fixture, root, host, router } = await render();
    host.busy.set(true);
    await fixture.whenStable();
    root.querySelector<HTMLAnchorElement>('#link')!.click();
    await fixture.whenStable();
    expect(router.url).toBe('/');
  });

  it('gives an icon button its icon back when the work is done', async () => {
    const { fixture, root, host } = await render();
    host.busy.set(true);
    await fixture.whenStable();
    expect(root.querySelector('#icon svg')).toBeNull();
    host.busy.set(false);
    await fixture.whenStable();
    expect(root.querySelector('#icon svg')).not.toBeNull();
    expect(root.querySelector('#icon .spinner')).toBeNull();
  });

  it('has no accessibility violations as links and icon buttons, idle and busy', async () => {
    const { fixture, root, host } = await render();
    expect(root.querySelector('#icon-link')!.getAttribute('aria-label')).toBe('Settings');
    expect(await axeViolations(root)).toEqual([]);
    host.busy.set(true);
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
  });
});
