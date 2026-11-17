import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpToastRegion } from '../toast/toast-region';
import { PpSaveState, PpSaveStatus } from './save-status';

@Component({
  imports: [PpSaveStatus, PpToastRegion],
  template: `
    <pp-save-status [state]="state()" [detail]="detail()" />
    <pp-toast-region />
  `,
})
class Host {
  readonly state = signal<PpSaveState>('saved');
  readonly detail = signal<string | undefined>(undefined);
}

describe('PpSaveStatus', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const host = fixture.componentInstance;
    const spoken = () => [...root.querySelectorAll('pp-announcer p')].map((p) => p.textContent);
    const status = () => root.querySelector('pp-save-status') as HTMLElement;
    const to = async (state: PpSaveState, detail?: string) => {
      host.detail.set(detail);
      host.state.set(state);
      await fixture.whenStable();
    };
    return { root, spoken, status, to };
  }

  it('says each state in words, with the caller’s detail under them', async () => {
    const { status, to } = await render();
    expect(status().textContent?.trim()).toBe('Saved');
    expect(status().getAttribute('data-state')).toBe('saved');
    await to('pending');
    expect(status().querySelector('.words')?.textContent).toBe('Changes not saved yet');
    await to('saving');
    expect(status().querySelector('.words')?.textContent).toBe('Saving…');
    await to('retrying', 'Your changes are kept on this device.');
    expect(status().querySelector('.words')?.textContent).toBe('Not saved yet. Trying again…');
    expect(status().querySelector('.detail')?.textContent).toBe(
      'Your changes are kept on this device.',
    );
    await to('failed');
    expect(status().querySelector('.words')?.textContent).toBe('Not saved');
  });

  it('is no live region, and says nothing while saving goes well', async () => {
    const { status, spoken, to } = await render();
    expect(status().getAttribute('role')).toBeNull();
    expect(status().getAttribute('aria-live')).toBeNull();
    for (const state of ['pending', 'saving', 'saved', 'pending', 'saving', 'saved'] as const) {
      await to(state);
    }
    expect(spoken()).toEqual([]);
  });

  it('says trouble once as it begins, and the save that ends it once', async () => {
    const { spoken, to } = await render();
    await to('saving');
    await to('retrying', 'Your changes are kept on this device.');
    expect(spoken()).toEqual([
      'Not saved yet. Trying again… Your changes are kept on this device.',
    ]);
    await to('saving');
    await to('retrying', 'Your changes are kept on this device.');
    await to('failed');
    expect(spoken()).toEqual([
      'Not saved yet. Trying again… Your changes are kept on this device.',
    ]);
    await to('pending');
    await to('saving');
    await to('saved', 'Last saved 3:42 PM');
    expect(spoken()).toEqual(['Saved. Last saved 3:42 PM']);
  });

  it('never says the state it starts in', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.state.set('failed');
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('pp-announcer p')).toHaveLength(0);
  });

  it('passes axe in every state', async () => {
    const { root, to } = await render();
    for (const state of ['saved', 'pending', 'saving', 'retrying', 'failed'] as const) {
      await to(state, 'Detail');
      expect(await axeViolations(root)).toEqual([]);
    }
  });
});
