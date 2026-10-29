import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpToastRegion } from '../toast/toast-region';
import { acceptsFile, PpUploader, PpUploadItem, sizeInWords } from './uploader';

const MB = 1024 * 1024;

@Component({
  imports: [PpUploader, PpToastRegion],
  template: `
    <pp-uploader
      id="photos"
      label="Choose photos"
      hint="JPEG, PNG, WebP, or HEIC, up to 2 MB."
      accept="image/jpeg,image/png,image/webp,.heic"
      acceptText="a JPEG, PNG, WebP, or HEIC image"
      [maxBytes]="2 * MB"
      [items]="items()"
      [disabled]="disabled()"
      (filesChosen)="chosen.push($event)"
      (retry)="retried.push($event)"
      (remove)="removed.push($event)"
    />
    <pp-uploader id="logo" label="Choose a logo" variant="primary" multiple="false" />
    <pp-toast-region />
  `,
})
class Host {
  readonly MB = MB;
  readonly items = signal<readonly PpUploadItem[]>([]);
  readonly disabled = signal(false);
  readonly chosen: File[][] = [];
  readonly retried: string[] = [];
  readonly removed: string[] = [];
}

function file(name: string, type: string, bytes = 1024): File {
  return new File([new Uint8Array(bytes)], name, { type });
}

/** jsdom has no DataTransfer, so a drop carries a stand-in with the parts the uploader reads. */
function drag(kind: 'dragover' | 'drop', files: File[]): DragEvent {
  const event = new Event(kind, { bubbles: true, cancelable: true }) as DragEvent;
  Object.defineProperty(event, 'dataTransfer', {
    value: { types: ['Files'], files, dropEffect: 'none' },
  });
  return event;
}

describe('PpUploader', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const uploader = (id: string) => root.querySelector<HTMLElement>(`#${id}`)!;
    const picker = (id: string) =>
      uploader(id).querySelector<HTMLInputElement>('input[type="file"]')!;
    const choose = (id: string, files: File[]) => {
      Object.defineProperty(picker(id), 'files', { value: files, configurable: true });
      picker(id).dispatchEvent(new Event('change', { bubbles: true }));
    };
    const spoken = () => [...root.querySelectorAll('pp-announcer p')].map((p) => p.textContent);
    return { fixture, root, uploader, picker, choose, spoken, host: fixture.componentInstance };
  }

  it('opens the browser’s own picker from a visible button, described by its hint', async () => {
    const { uploader, picker } = await render();
    const button = uploader('photos').querySelector<HTMLButtonElement>('button')!;
    expect(button.textContent!.trim()).toBe('Choose photos');
    const hint = uploader('photos').querySelector(`#${button.getAttribute('aria-describedby')}`)!;
    expect(hint.textContent!.trim()).toBe('JPEG, PNG, WebP, or HEIC, up to 2 MB.');
    expect(picker('photos').hidden).toBe(true);
    expect(picker('photos').multiple).toBe(true);
    expect(picker('photos').getAttribute('accept')).toBe('image/jpeg,image/png,image/webp,.heic');
    expect(picker('logo').multiple).toBe(false);
    expect(picker('logo').hasAttribute('accept')).toBe(false);
    let opened = 0;
    picker('photos').addEventListener('click', () => (opened += 1));
    button.click();
    expect(opened).toBe(1);
  });

  it('gives the files that pass, names each refused one and why, and says both once', async () => {
    const { fixture, uploader, choose, spoken, host } = await render();
    const good = file('pie.jpg', 'image/jpeg');
    const heic = file('IMG_0001.HEIC', '');
    choose('photos', [
      good,
      file('scan.tif', 'image/tiff'),
      heic,
      file('big.png', 'image/png', 3 * MB),
    ]);
    await fixture.whenStable();
    expect(host.chosen).toEqual([[good, heic]]);
    const notice = uploader('photos').querySelector('pp-notice')!;
    expect(notice.textContent).toContain("2 files can't be added");
    expect([...notice.querySelectorAll('li')].map((li) => li.textContent!.trim())).toEqual([
      "scan.tif: it isn't a JPEG, PNG, WebP, or HEIC image.",
      "big.png: it's larger than 2 MB.",
    ]);
    expect(spoken()).toEqual([
      "2 files chosen. 2 files can't be added. The reasons are listed below.",
    ]);
  });

  it('lets the refusals be dismissed, leaving focus on the button, and clears them on the next choice', async () => {
    const { fixture, uploader, choose, spoken } = await render();
    choose('photos', [file('scan.tif', 'image/tiff')]);
    await fixture.whenStable();
    expect(spoken()).toEqual(["1 file can't be added. The reason is listed below."]);
    const dismiss = uploader('photos').querySelector<HTMLButtonElement>('pp-notice button')!;
    dismiss.focus();
    dismiss.click();
    await fixture.whenStable();
    expect(uploader('photos').querySelector('pp-notice')).toBeNull();
    expect(document.activeElement).toBe(uploader('photos').querySelector('button'));
    choose('photos', [file('scan.tif', 'image/tiff')]);
    await fixture.whenStable();
    choose('photos', [file('pie.jpg', 'image/jpeg')]);
    await fixture.whenStable();
    expect(uploader('photos').querySelector('pp-notice')).toBeNull();
  });

  it('empties the picker after a choice, so choosing the same file again still counts', async () => {
    const { picker, choose } = await render();
    // jsdom keeps a file input's value empty, so the browser's chosen path is stood in for.
    let value = 'C:\\fakepath\\pie.jpg';
    Object.defineProperty(picker('photos'), 'value', {
      get: () => value,
      set: (next: string) => (value = next),
      configurable: true,
    });
    choose('photos', [file('pie.jpg', 'image/jpeg')]);
    expect(value).toBe('');
  });

  it('takes one file at a time when multiple is off', async () => {
    const { fixture, uploader, choose } = await render();
    choose('logo', [file('a.png', 'image/png'), file('b.png', 'image/png')]);
    await fixture.whenStable();
    expect(uploader('logo').querySelector('li')!.textContent!.trim()).toBe(
      'b.png: only one file can be added at a time.',
    );
  });

  it('takes dropped files through the same checks, and shows when files are held over it', async () => {
    const { fixture, uploader, host } = await render();
    const over = drag('dragover', []);
    uploader('photos').dispatchEvent(over);
    await fixture.whenStable();
    expect(over.defaultPrevented).toBe(true);
    expect(uploader('photos').querySelector('.zone')!.hasAttribute('data-dragging')).toBe(true);
    const good = file('pie.png', 'image/png');
    uploader('photos').dispatchEvent(drag('drop', [good, file('notes.txt', 'text/plain')]));
    await fixture.whenStable();
    expect(host.chosen).toEqual([[good]]);
    expect(uploader('photos').querySelector('.zone')!.hasAttribute('data-dragging')).toBe(false);
    expect(uploader('photos').querySelector('pp-notice li')!.textContent).toContain('notes.txt');
  });

  it('takes nothing while disabled, but still holds a dropped file so the browser never opens it', async () => {
    const { fixture, uploader, host } = await render();
    host.disabled.set(true);
    await fixture.whenStable();
    const over = drag('dragover', []);
    uploader('photos').dispatchEvent(over);
    await fixture.whenStable();
    expect(over.defaultPrevented).toBe(true);
    expect(over.dataTransfer!.dropEffect).toBe('none');
    expect(uploader('photos').querySelector('.zone')!.hasAttribute('data-dragging')).toBe(false);
    const drop = drag('drop', [file('pie.png', 'image/png')]);
    uploader('photos').dispatchEvent(drop);
    expect(drop.defaultPrevented).toBe(true);
    expect(host.chosen).toEqual([]);
    expect(uploader('photos').querySelector<HTMLButtonElement>('button')!.disabled).toBe(true);
  });

  it('draws each file: progress while sending, then added or not, with Try again and Remove', async () => {
    const { fixture, uploader, host } = await render();
    host.items.set([
      { id: 'a', name: 'pie.jpg', status: 'uploading', progress: 40 },
      { id: 'b', name: 'tart.jpg', status: 'waiting' },
      { id: 'c', name: 'cake.jpg', status: 'done' },
      { id: 'd', name: 'bun.jpg', status: 'failed', error: 'The connection dropped.' },
    ]);
    await fixture.whenStable();
    const list = uploader('photos').querySelector('ul.items')!;
    expect(list.getAttribute('aria-label')).toBe('Files');
    const items = [...list.querySelectorAll<HTMLElement>(':scope > li')];
    expect(items.map((item) => item.dataset['status'])).toEqual([
      'uploading',
      'waiting',
      'done',
      'failed',
    ]);
    expect(items[0].querySelector('progress')!.getAttribute('value')).toBe('40');
    expect(items[0].querySelector('pp-progress')!.textContent).toContain('pie.jpg');
    expect(items[1].querySelector('pp-status-pill')!.textContent!.trim()).toBe('Waiting');
    expect(items[2].querySelector('pp-status-pill')!.textContent!.trim()).toBe('Added');
    expect(items[3].querySelector('pp-field-error')!.textContent!.replace(/\s+/g, ' ').trim()).toBe(
      'Error: The connection dropped.',
    );

    const retry = [...items[3].querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent!.trim() === 'Try again',
    )!;
    const described = retry
      .getAttribute('aria-describedby')!
      .split(' ')
      .map((id) =>
        uploader('photos').querySelector(`#${id}`)!.textContent!.replace(/\s+/g, ' ').trim(),
      );
    expect(described).toEqual(['bun.jpg', 'Error: The connection dropped.']);
    retry.focus();
    retry.click();
    expect(host.retried).toEqual(['d']);
    expect(document.activeElement!.getAttribute('aria-label')).toBe('Remove bun.jpg');
    expect(items.filter((item) => item.textContent!.includes('Try again'))).toHaveLength(1);
  });

  it('chooses with a primary button when asked, so it can be the page’s one next step', async () => {
    const { uploader } = await render();
    expect(uploader('logo').querySelector('button')!.dataset['variant']).toBe('primary');
    expect(uploader('photos').querySelector('button')!.dataset['variant']).toBe('secondary');
  });

  it('leaves focus on the nearest Remove when a file goes, then on the button, then on itself', async () => {
    const { fixture, uploader, host } = await render();
    const remove = (name: string) =>
      uploader('photos').querySelector<HTMLButtonElement>(`button[aria-label="Remove ${name}"]`)!;
    const press = async (name: string, left: readonly PpUploadItem[]) => {
      remove(name).focus();
      remove(name).click();
      host.items.set(left);
      await fixture.whenStable();
    };
    const a: PpUploadItem = { id: 'a', name: 'pie.jpg', status: 'done' };
    const b: PpUploadItem = { id: 'b', name: 'tart.jpg', status: 'waiting' };
    const c: PpUploadItem = { id: 'c', name: 'cake.jpg', status: 'done' };
    host.items.set([a, b, c]);
    await fixture.whenStable();

    await press('tart.jpg', [a, c]);
    expect(document.activeElement).toBe(remove('cake.jpg'));
    await press('cake.jpg', [a]);
    expect(document.activeElement).toBe(remove('pie.jpg'));
    await press('pie.jpg', []);
    expect(document.activeElement).toBe(uploader('photos').querySelector('button'));
    expect(host.removed).toEqual(['b', 'c', 'a']);

    host.items.set([a]);
    host.disabled.set(true);
    await fixture.whenStable();
    await press('pie.jpg', []);
    expect(document.activeElement).toBe(uploader('photos'));
  });

  it('says once when a file is added or fails, never its progress or a first sight', async () => {
    const { fixture, spoken, host } = await render();
    host.items.set([
      { id: 'a', name: 'pie.jpg', status: 'uploading', progress: 10 },
      { id: 'b', name: 'tart.jpg', status: 'uploading', progress: 10 },
      { id: 'c', name: 'old.jpg', status: 'done' },
    ]);
    await fixture.whenStable();
    expect(spoken()).toEqual([]);
    host.items.set([
      { id: 'a', name: 'pie.jpg', status: 'uploading', progress: 80 },
      { id: 'b', name: 'tart.jpg', status: 'uploading', progress: 50 },
      { id: 'c', name: 'old.jpg', status: 'done' },
    ]);
    await fixture.whenStable();
    expect(spoken()).toEqual([]);
    host.items.set([
      { id: 'a', name: 'pie.jpg', status: 'done' },
      { id: 'b', name: 'tart.jpg', status: 'failed', error: 'The connection dropped.' },
      { id: 'c', name: 'old.jpg', status: 'done' },
    ]);
    await fixture.whenStable();
    expect(spoken()).toEqual(['pie.jpg added. tart.jpg not added: The connection dropped.']);
  });

  it('has no accessibility violations, empty, refusing, and with every kind of file', async () => {
    const { fixture, root, choose, host } = await render();
    expect(await axeViolations(root)).toEqual([]);
    choose('photos', [file('scan.tif', 'image/tiff')]);
    host.items.set([
      { id: 'a', name: 'pie.jpg', status: 'uploading', progress: 40 },
      { id: 'b', name: 'tart.jpg', status: 'waiting' },
      { id: 'c', name: 'cake.jpg', status: 'done' },
      { id: 'd', name: 'bun.jpg', status: 'failed', error: 'The connection dropped.' },
      { id: 'e', name: 'roll.jpg', status: 'failed' },
    ]);
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
    host.disabled.set(true);
    await fixture.whenStable();
    expect(await axeViolations(root)).toEqual([]);
  });
});

describe('acceptsFile', () => {
  it('matches extensions by the name, wildcards by the type, and exact types, ignoring case', () => {
    expect(acceptsFile('', { name: 'any.bin', type: '' })).toBe(true);
    expect(acceptsFile('.heic', { name: 'IMG.HEIC', type: '' })).toBe(true);
    expect(acceptsFile('image/*', { name: 'a', type: 'image/avif' })).toBe(true);
    expect(acceptsFile('image/*', { name: 'a', type: 'imagery/x' })).toBe(false);
    expect(acceptsFile('image/png, image/jpeg', { name: 'a', type: 'IMAGE/JPEG' })).toBe(true);
    expect(acceptsFile('image/png', { name: 'a.png', type: 'image/apng' })).toBe(false);
    expect(acceptsFile('.png', { name: 'a.png.exe', type: '' })).toBe(false);
  });
});

describe('sizeInWords', () => {
  it('says a size the same way everywhere', () => {
    expect(sizeInWords(20 * MB)).toBe('20 MB');
    expect(sizeInWords(1.5 * MB)).toBe('1.5 MB');
    expect(sizeInWords(300 * 1024)).toBe('300 KB');
    expect(sizeInWords(10)).toBe('1 KB');
  });
});
