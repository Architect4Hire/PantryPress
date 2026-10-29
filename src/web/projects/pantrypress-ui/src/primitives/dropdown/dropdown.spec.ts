import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpDropdown, PpDropdownItem } from './dropdown';

@Component({
  imports: [PpDropdown, PpDropdownItem],
  template: `
    <button type="button" id="before">Before</button>
    <pp-dropdown prefix="Site" label="Apple Pie Club" caption="Your sites" [(open)]="open">
      <a pp-dropdown-item href="#apple" aria-current="page">Apple Pie Club</a>
      <a pp-dropdown-item href="#plum">Plum Tart Society</a>
      <button pp-dropdown-item (click)="pressed.set(pressed() + 1)">Sign out</button>
    </pp-dropdown>
    <button type="button" id="after">After</button>
  `,
})
class Host {
  readonly open = signal(false);
  readonly pressed = signal(0);
}

describe('PpDropdown', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    document.body.appendChild(root);
    const trigger = root.querySelector<HTMLButtonElement>('pp-dropdown > button')!;
    const panel = root.querySelector<HTMLElement>('.popup')!;
    const items = () => [...panel.querySelectorAll<HTMLElement>('[pp-dropdown-item]')];
    const stable = () => fixture.whenStable();
    const key = async (target: Element, name: string) => {
      const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
      target.dispatchEvent(event);
      await stable();
      return event;
    };
    return { fixture, root, trigger, panel, items, stable, key, host: fixture.componentInstance };
  }

  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('is a button that names and controls a hidden panel', async () => {
    const { trigger, panel } = await render();
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(trigger.getAttribute('aria-controls')).toBe(panel.id);
    expect(trigger.textContent?.trim()).toBe('Site Apple Pie Club');
    expect(trigger.hasAttribute('aria-label')).toBe(false);
    expect(trigger.getAttribute('type')).toBe('button');
    expect(panel.hidden).toBe(true);
  });

  it('opens and closes on a press, saying so', async () => {
    const { trigger, panel, stable, host } = await render();
    trigger.click();
    await stable();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(panel.hidden).toBe(false);
    expect(host.open()).toBe(true);
    trigger.click();
    await stable();
    expect(panel.hidden).toBe(true);
  });

  it('shows the caption as plain text and marks the current item', async () => {
    const { panel, items } = await render();
    const caption = panel.querySelector('.caption')!;
    expect(caption.textContent).toBe('Your sites');
    // Tab passes the caption by, so the button is described by it.
    expect(panel.parentElement!.querySelector('button')!.getAttribute('aria-describedby')).toBe(
      caption.id,
    );
    expect(items()[0].getAttribute('aria-current')).toBe('page');
    expect(items()[2].getAttribute('type')).toBe('button');
    expect(items()[0].hasAttribute('type')).toBe(false);
  });

  it('closes on Escape, puts focus back on the button, and keeps the Escape from going further', async () => {
    const { trigger, panel, items, key, stable } = await render();
    trigger.click();
    await stable();
    items()[1].focus();
    const escape = await key(items()[1], 'Escape');
    expect(panel.hidden).toBe(true);
    expect(document.activeElement).toBe(trigger);
    expect(escape.defaultPrevented).toBe(true);
  });

  it('closes on Escape while open even when focus never reached it, as after a press in Safari', async () => {
    const { trigger, panel, stable } = await render();
    trigger.click();
    await stable();
    (document.activeElement as HTMLElement | null)?.blur();
    document.body.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await stable();
    expect(panel.hidden).toBe(true);
  });

  it('lets an Escape through while closed', async () => {
    const { trigger, key } = await render();
    const escape = await key(trigger, 'Escape');
    expect(escape.defaultPrevented).toBe(false);
  });

  it('closes when an item is chosen, after the item has done its work', async () => {
    const { trigger, panel, items, stable, host } = await render();
    trigger.click();
    await stable();
    items()[2].focus();
    items()[2].click();
    await stable();
    expect(host.pressed()).toBe(1);
    expect(panel.hidden).toBe(true);
    expect(document.activeElement).toBe(trigger);
  });

  it('closes on a press outside, and not on one inside', async () => {
    const { root, trigger, panel, stable } = await render();
    trigger.click();
    await stable();
    panel.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    await stable();
    expect(panel.hidden).toBe(false);
    root.querySelector('#after')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    await stable();
    expect(panel.hidden).toBe(true);
  });

  it('closes when Tab takes focus out, and not when focus goes nowhere', async () => {
    const { root, trigger, panel, items, stable } = await render();
    trigger.click();
    await stable();
    items()[0].dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: null }));
    await stable();
    expect(panel.hidden).toBe(false);
    items()[0].dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, relatedTarget: items()[1] }),
    );
    await stable();
    expect(panel.hidden).toBe(false);
    items()[2].dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, relatedTarget: root.querySelector('#after') }),
    );
    await stable();
    expect(panel.hidden).toBe(true);
  });

  it('has no accessibility violations, closed or open', async () => {
    const { root, trigger, stable } = await render();
    expect(await axeViolations(root)).toEqual([]);
    trigger.click();
    await stable();
    expect(await axeViolations(root)).toEqual([]);
  });
});
