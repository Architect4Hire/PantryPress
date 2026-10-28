import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PpTone } from '../status/tone';
import { PpToastRegion } from '../toast/toast-region';
import { PpNotice } from './notice';

@Component({
  imports: [PpNotice, PpToastRegion],
  template: `
    @if (shown()) {
      <pp-notice [tone]="tone()" heading="Saved" announce>Your changes are live.</pp-notice>
    }
    <pp-toast-region />
  `,
})
class Host {
  readonly shown = signal(false);
  readonly tone = signal<PpTone>('success');
}

describe('PpNotice that announces', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const spoken = () => [...root.querySelectorAll('pp-announcer p')].map((p) => p.textContent);
    return { fixture, root, spoken, host: fixture.componentInstance };
  }

  it('says a note or a success politely through the announcer once it appears', async () => {
    const { fixture, spoken, host } = await render();
    host.shown.set(true);
    await fixture.whenStable();
    expect(spoken()).toEqual(['Success: Saved Your changes are live.']);
  });

  it('leaves a warning or an error to its own alert role', async () => {
    const { fixture, root, spoken, host } = await render();
    host.tone.set('danger');
    host.shown.set(true);
    await fixture.whenStable();
    expect(root.querySelector('pp-notice')!.getAttribute('role')).toBe('alert');
    expect(spoken()).toEqual([]);
  });
});
