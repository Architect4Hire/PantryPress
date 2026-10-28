import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpButton } from '../button/button';
import { PpToolbar } from './toolbar';

@Component({
  imports: [PpToolbar, PpButton],
  template: `
    <div [attr.dir]="dir()">
      <pp-toolbar label="Formatting">
        <button pp-button size="sm">Bold</button>
        <button pp-button size="sm">Italic</button>
        <button pp-button size="sm" disabled>Code</button>
        <button pp-button size="sm">Link</button>
      </pp-toolbar>
    </div>
  `,
})
class Host {
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
}

describe('PpToolbar', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const buttons = () => [...root.querySelectorAll<HTMLButtonElement>('button:not([disabled])')];
    const press = async (key: string) => {
      (document.activeElement as HTMLElement).dispatchEvent(
        new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
      );
      await fixture.whenStable();
    };
    return { fixture, root, buttons, press, host: fixture.componentInstance };
  }

  it('is a named toolbar with one tab stop', async () => {
    const { root, buttons } = await render();
    const toolbar = root.querySelector('pp-toolbar')!;
    expect(toolbar.getAttribute('role')).toBe('toolbar');
    expect(toolbar.getAttribute('aria-label')).toBe('Formatting');
    expect(buttons().map((button) => button.tabIndex)).toEqual([0, -1, -1]);
  });

  it('moves with the arrow keys past a disabled control, wrapping, and with Home and End', async () => {
    const { buttons, press } = await render();
    const [bold, italic, link] = buttons();
    bold.focus();
    await press('ArrowRight');
    expect(document.activeElement).toBe(italic);
    await press('ArrowRight');
    expect(document.activeElement).toBe(link);
    await press('ArrowRight');
    expect(document.activeElement).toBe(bold);
    await press('End');
    expect(document.activeElement).toBe(link);
    await press('Home');
    expect(document.activeElement).toBe(bold);
    expect(buttons().map((button) => button.tabIndex)).toEqual([0, -1, -1]);
  });

  it('makes the control last focused the tab stop', async () => {
    const { buttons } = await render();
    buttons()[2].focus();
    expect(buttons().map((button) => button.tabIndex)).toEqual([-1, -1, 0]);
  });

  it('turns the arrow keys around in a right-to-left page', async () => {
    const { fixture, buttons, press, host } = await render();
    host.dir.set('rtl');
    await fixture.whenStable();
    buttons()[0].focus();
    await press('ArrowLeft');
    expect(document.activeElement).toBe(buttons()[1]);
  });

  it('has no accessibility violations', async () => {
    const { root } = await render();
    expect(await axeViolations(root)).toEqual([]);
  });
});
