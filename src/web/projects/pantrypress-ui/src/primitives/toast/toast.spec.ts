import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PP_TOAST_STAY_MS, PpToastRegion } from './toast-region';
import { PP_TOAST_LIMIT, PpToaster } from './toaster';

@Component({
  imports: [PpToastRegion],
  template: `
    <main>
      <h1>Posts</h1>
      <button id="save" type="button">Save</button>
    </main>
    @if (shown()) {
      <pp-toast-region />
    }
  `,
})
class Host {
  readonly shown = signal(true);
}

describe('PpToaster and PpToastRegion', () => {
  afterEach(() => vi.useRealTimers());

  async function render() {
    const fixture = TestBed.createComponent(Host);
    // whenStable waits on timers, which fake timers hold still, so under them a refresh is driven by hand.
    const settle = async () => {
      if (vi.isFakeTimers()) {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
      } else {
        await fixture.whenStable();
      }
    };
    await settle();
    const root = fixture.nativeElement as HTMLElement;
    const toaster = TestBed.inject(PpToaster);
    const toasts = () => [...root.querySelectorAll<HTMLElement>('li[pp-toast]')];
    const spoken = () =>
      [...root.querySelectorAll('pp-announcer [aria-live="polite"] p')].map((p) => p.textContent);
    const button = (toast: HTMLElement, name: string) =>
      [...toast.querySelectorAll<HTMLButtonElement>('button')].find(
        (candidate) =>
          (candidate.getAttribute('aria-label') ?? candidate.textContent!.trim()) === name,
      )!;
    return {
      fixture,
      root,
      toaster,
      toasts,
      spoken,
      button,
      settle,
      host: fixture.componentInstance,
    };
  }

  it('is a Notifications landmark whose list is not live, with a polite announcer from the first render', async () => {
    const { root } = await render();
    expect(root.querySelector('section')!.getAttribute('aria-label')).toBe('Notifications');
    expect(root.querySelector('ol')!.hasAttribute('aria-live')).toBe(false);
    expect(root.querySelector('pp-announcer [aria-live="polite"]')).not.toBeNull();
  });

  it('shows a toast with its tone in words and an icon, and says its words once, without its buttons', async () => {
    const { toaster, toasts, spoken, button, settle } = await render();
    toaster.show({
      message: 'Post deleted.',
      tone: 'success',
      action: { label: 'Undo', run: () => undefined },
    });
    await settle();
    const [toast] = toasts();
    expect(toast.dataset['tone']).toBe('success');
    expect(toast.querySelector('.message')!.textContent!.trim()).toBe('Success: Post deleted.');
    expect(toast.querySelector('pp-tone-icon svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(spoken()).toEqual(['Success: Post deleted.']);
    button(toast, 'Dismiss').click();
    await settle();
    expect(toasts()).toEqual([]);
  });

  it('runs its action, then goes', async () => {
    const { toaster, toasts, button, settle } = await render();
    const run = vi.fn();
    toaster.show({ message: 'Post deleted.', action: { label: 'Undo', run } });
    await settle();
    button(toasts()[0], 'Undo').click();
    await settle();
    expect(run).toHaveBeenCalledTimes(1);
    expect(toasts()).toEqual([]);
  });

  it(`shows at most ${PP_TOAST_LIMIT} that leave by themselves, and never sends away one that waits`, async () => {
    const { toaster, toasts, settle } = await render();
    toaster.show({ message: 'Could not publish.', tone: 'danger' });
    toaster.show({ message: 'Deleted.', action: { label: 'Undo', run: () => undefined } });
    for (const n of [1, 2, 3, 4]) {
      toaster.show({ message: `Saved ${n}.`, tone: 'success' });
    }
    await settle();
    expect(toasts().map((toast) => toast.querySelector('.message')!.textContent!.trim())).toEqual([
      'Error: Could not publish.',
      'Note: Deleted.',
      'Success: Saved 2.',
      'Success: Saved 3.',
      'Success: Saved 4.',
    ]);
  });

  it('moves focus to the next toast, else back where it came from, when a focused toast goes', async () => {
    const { root, toaster, toasts, button, settle } = await render();
    toaster.show({ message: 'First.', tone: 'warning' });
    toaster.show({ message: 'Second.', tone: 'warning' });
    await settle();
    const save = root.querySelector<HTMLButtonElement>('#save')!;
    save.focus();
    button(toasts()[0], 'Dismiss').focus();
    button(toasts()[0], 'Dismiss').click();
    await settle();
    expect(document.activeElement).toBe(button(toasts()[0], 'Dismiss'));
    expect(toasts().length).toBe(1);
    button(toasts()[0], 'Dismiss').click();
    await settle();
    expect(toasts()).toEqual([]);
    expect(document.activeElement).toBe(save);
  });

  it('lets a success or a note go by itself, but keeps one that matters', async () => {
    vi.useFakeTimers();
    const { toaster, toasts, settle } = await render();
    toaster.show({ message: 'Saved.', tone: 'success' });
    toaster.show({ message: 'Imported.' });
    toaster.show({ message: 'Could not publish.', tone: 'danger' });
    toaster.show({ message: 'Almost out of space.', tone: 'warning' });
    toaster.show({
      message: 'Deleted.',
      tone: 'success',
      action: { label: 'Undo', run: () => undefined },
    });
    await settle();
    vi.advanceTimersByTime(PP_TOAST_STAY_MS - 1);
    await settle();
    expect(toasts().length).toBe(5);
    vi.advanceTimersByTime(1);
    await settle();
    expect(toasts().map((toast) => toast.dataset['tone'])).toEqual([
      'danger',
      'warning',
      'success',
    ]);
    vi.advanceTimersByTime(PP_TOAST_STAY_MS * 10);
    await settle();
    expect(toasts().length).toBe(3);
  });

  it('waits while it is hovered or focused, even when one ends before the other', async () => {
    vi.useFakeTimers();
    const { toaster, toasts, button, settle } = await render();
    toaster.show({ message: 'Saved.', tone: 'success' });
    await settle();
    const toast = toasts()[0];
    toast.dispatchEvent(new MouseEvent('mouseenter'));
    toast.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    toast.dispatchEvent(new MouseEvent('mouseleave'));
    vi.advanceTimersByTime(PP_TOAST_STAY_MS * 2);
    await settle();
    expect(toasts().length).toBe(1);
    // Focus moving between its own buttons has not left it.
    toast.dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, relatedTarget: button(toast, 'Dismiss') }),
    );
    vi.advanceTimersByTime(PP_TOAST_STAY_MS * 2);
    await settle();
    expect(toasts().length).toBe(1);
    toast.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
    vi.advanceTimersByTime(PP_TOAST_STAY_MS - 1);
    await settle();
    expect(toasts().length).toBe(1);
    vi.advanceTimersByTime(1);
    await settle();
    expect(toasts()).toEqual([]);
  });

  it('stops its timer when the region goes', async () => {
    vi.useFakeTimers();
    const { toaster, settle, host } = await render();
    toaster.show({ message: 'Saved.', tone: 'success' });
    await settle();
    expect(vi.getTimerCount()).toBeGreaterThan(0);
    host.shown.set(false);
    await settle();
    expect(vi.getTimerCount()).toBe(0);
  });

  it('speaks an announcement without drawing it, and the same words twice as two', async () => {
    const { root, toaster, spoken, settle } = await render();
    toaster.announce('Saving your post.');
    await settle();
    const first = root.querySelector('pp-announcer p')!;
    expect(spoken()).toEqual(['Saving your post.']);
    toaster.announce('Saving your post.');
    await settle();
    expect(spoken()).toEqual(['Saving your post.']);
    expect(root.querySelector('pp-announcer p')).not.toBe(first);
  });

  it('has no accessibility violations, empty and with every tone', async () => {
    const { root, toaster, settle } = await render();
    expect(await axeViolations(root)).toEqual([]);
    toaster.show({ message: 'Note.', tone: 'info' });
    toaster.show({
      message: 'Saved.',
      tone: 'success',
      action: { label: 'View', run: () => undefined },
    });
    toaster.show({ message: 'Careful.', tone: 'warning' });
    toaster.show({ message: 'Failed.', tone: 'danger' });
    await settle();
    expect(await axeViolations(root)).toEqual([]);
  });
});
