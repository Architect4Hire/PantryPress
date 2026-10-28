import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpButton } from '../button/button';
import { PpCard } from '../card/card';
import { PpEmptyState } from '../empty-state/empty-state';
import { PpPageHeader } from '../page-header/page-header';
import { PpProgress } from '../progress/progress';
import { PpTone } from '../status/tone';
import { PpStatusPill } from '../status-pill/status-pill';
import { PpNotice } from './notice';

@Component({
  imports: [PpNotice, PpEmptyState, PpProgress, PpStatusPill, PpCard, PpPageHeader, PpButton],
  template: `
    <pp-page-header heading="Posts" description="Everything you have written.">
      <button pp-button variant="primary" ppPageActions>Write a post</button>
    </pp-page-header>
    <main>
      <pp-notice
        id="notice"
        [tone]="tone()"
        [heading]="heading()"
        [announce]="announce()"
        dismissible
        (dismissed)="dismissals = dismissals + 1"
      >
        Your plan renews tomorrow.
      </pp-notice>
      <pp-card id="card"
        ><h2>Drafts</h2>
        <p>Two drafts.</p></pp-card
      >
      <pp-empty-state
        id="empty"
        heading="No posts yet"
        description="Your posts will show here."
        [level]="level()"
      >
        <button pp-button variant="primary">Write a post</button>
      </pp-empty-state>
      <pp-progress id="upload" label="Uploading photo" [value]="value()" [max]="max()" showValue />
      <pp-progress id="working" label="Preparing your site" />
      <p>
        @for (pill of pills; track pill) {
          <pp-status-pill [tone]="pill">{{ pill }}</pp-status-pill>
        }
      </p>
    </main>
  `,
})
class Host {
  readonly tone = signal<PpTone>('info');
  readonly heading = signal<string | undefined>('Heads up');
  readonly announce = signal(false);
  readonly level = signal<2 | 3 | 4>(2);
  readonly value = signal<number | undefined>(40);
  readonly max = signal(100);
  readonly pills: (PpTone | 'neutral')[] = ['neutral', 'info', 'success', 'warning', 'danger'];
  dismissals = 0;
}

describe('layout and feedback primitives', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, host: fixture.componentInstance, settle: () => fixture.whenStable() };
  }

  describe('PpPageHeader', () => {
    it("is the page's one h1, with its sentence and actions", async () => {
      const { root } = await render();
      expect(root.querySelectorAll('h1').length).toBe(1);
      expect(root.querySelector('pp-page-header h1')!.textContent!.trim()).toBe('Posts');
      expect(root.querySelector('pp-page-header .description')!.textContent).toBe(
        'Everything you have written.',
      );
      expect(root.querySelector('pp-page-header .actions button')!.textContent!.trim()).toBe(
        'Write a post',
      );
    });
  });

  describe('PpNotice', () => {
    it('says its tone in words before its heading', async () => {
      const { root } = await render();
      const notice = root.querySelector('#notice')!;
      expect(notice.getAttribute('data-tone')).toBe('info');
      expect(notice.querySelector('.heading')!.textContent!.trim()).toBe('Note: Heads up');
      expect(notice.querySelector('pp-tone-icon svg')!.getAttribute('aria-hidden')).toBe('true');
    });

    it('says its tone in words when it has no heading', async () => {
      const { root, host, settle } = await render();
      host.heading.set(undefined);
      host.tone.set('danger');
      await settle();
      const notice = root.querySelector('#notice')!;
      expect(notice.querySelector('.heading')).toBeNull();
      expect(notice.querySelector('.content')!.textContent!.replace(/\s+/g, ' ').trim()).toBe(
        'Error: Your plan renews tomorrow.',
      );
    });

    it('is an alert when it announces a warning or an error, and has no live role otherwise', async () => {
      const { root, host, settle } = await render();
      const notice = root.querySelector('#notice')!;
      expect(notice.hasAttribute('role')).toBe(false);
      host.announce.set(true);
      for (const [tone, role] of [
        ['info', null],
        ['success', null],
        ['warning', 'alert'],
        ['danger', 'alert'],
      ] as const) {
        host.tone.set(tone);
        await settle();
        expect(notice.getAttribute('role'), tone).toBe(role);
      }
    });

    it('says when it is dismissed', async () => {
      const { root, host } = await render();
      root.querySelector<HTMLButtonElement>('#notice button[aria-label="Dismiss"]')!.click();
      expect(host.dismissals).toBe(1);
    });
  });

  describe('PpEmptyState', () => {
    it('has one heading at the level it is given, a sentence, and its one action', async () => {
      const { root, host, settle } = await render();
      const empty = () => root.querySelector('#empty')!;
      expect(empty().querySelector('h2')!.textContent).toBe('No posts yet');
      host.level.set(3);
      await settle();
      expect(empty().querySelector('h2')).toBeNull();
      expect(empty().querySelector('h3')!.textContent).toBe('No posts yet');
      expect(empty().querySelector('.description')!.textContent).toBe('Your posts will show here.');
      expect(empty().querySelectorAll('button').length).toBe(1);
    });
  });

  describe('PpProgress', () => {
    it('is named by its label and shows how much is done', async () => {
      const { root } = await render();
      const bar = root.querySelector<HTMLProgressElement>('#upload progress')!;
      const label = root.querySelector('#upload .label')!;
      expect(bar.getAttribute('aria-labelledby')).toBe(label.id);
      expect(bar.getAttribute('value')).toBe('40');
      expect(bar.getAttribute('max')).toBe('100');
      expect(root.querySelector('#upload .value')!.textContent).toBe('40%');
      expect(root.querySelector('#upload .value')!.getAttribute('aria-hidden')).toBe('true');
    });

    it('keeps its value between nothing and all, and says only that work goes on with no value', async () => {
      const { root, host, settle } = await render();
      host.value.set(250);
      await settle();
      expect(root.querySelector('#upload progress')!.getAttribute('value')).toBe('100');
      host.value.set(-5);
      host.max.set(10);
      await settle();
      expect(root.querySelector('#upload progress')!.getAttribute('value')).toBe('0');
      expect(root.querySelector('#upload .value')!.textContent).toBe('0%');
      const working = root.querySelector('#working progress')!;
      expect(working.hasAttribute('value')).toBe(false);
      expect(root.querySelector('#working .value')).toBeNull();
    });
  });

  describe('PpProgress with nothing to measure', () => {
    it('says only that work goes on when its max is zero or less', async () => {
      const { root, host, settle } = await render();
      host.max.set(0);
      await settle();
      expect(root.querySelector('#upload progress')!.hasAttribute('value')).toBe(false);
      expect(root.querySelector('#upload .value')).toBeNull();
    });
  });

  describe('PpStatusPill and PpCard', () => {
    it('carries its tone as an attribute and its meaning in words', async () => {
      const { root } = await render();
      const pills = [...root.querySelectorAll('pp-status-pill')];
      expect(pills.map((pill) => pill.getAttribute('data-tone'))).toEqual([
        'neutral',
        'info',
        'success',
        'warning',
        'danger',
      ]);
      expect(pills.every((pill) => pill.textContent!.trim().length > 0)).toBe(true);
    });

    it("keeps the caller's own heading in a card", async () => {
      const { root } = await render();
      expect(root.querySelector('#card h2')!.textContent).toBe('Drafts');
    });
  });

  it('has no accessibility violations in any tone', async () => {
    const { root, host, settle } = await render();
    for (const tone of ['info', 'success', 'warning', 'danger'] as const) {
      host.tone.set(tone);
      host.announce.set(tone !== 'info');
      await settle();
      expect(await axeViolations(root), tone).toEqual([]);
    }
    host.value.set(undefined);
    await settle();
    expect(await axeViolations(root)).toEqual([]);
  });
});
