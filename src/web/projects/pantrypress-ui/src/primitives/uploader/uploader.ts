import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { PpButton } from '../button/button';
import { nextId } from '../field/field-context';
import { PpFieldError } from '../field/field-error';
import { PpIconButton } from '../icon-button/icon-button';
import { PpNotice } from '../notice/notice';
import { PpProgress } from '../progress/progress';
import { PpStatusPill } from '../status-pill/status-pill';
import { PpCloseIcon } from '../status/tone';
import { PpToaster } from '../toast/toaster';

export type PpUploadStatus = 'waiting' | 'uploading' | 'done' | 'failed';

/** One file the app is sending, as the uploader draws it. `progress` is 0 to 100; `error` is the app's plain words. */
export interface PpUploadItem {
  readonly id: string;
  readonly name: string;
  readonly status: PpUploadStatus;
  readonly progress?: number;
  readonly error?: string;
}

interface Refusal {
  readonly name: string;
  readonly reason: string;
}

/**
 * Choosing files and following them up (PP3.5). The app does the sending; the uploader only chooses and shows.
 * A visible button opens the browser's own file picker, and files dropped on the area are an extra, never the only
 * way (2.5.7). Each chosen file is checked against `accept` and `maxBytes` as a courtesy, so a wrong one is named at
 * once in plain words; the server still checks and scans every file. `filesChosen` gives the files that passed.
 * The app's `items` are drawn as a list: progress while sending, then added or failed, with Try again and Remove,
 * which asks the app to drop the file, stopping it if it is being sent. What changed is said once through the
 * announcer: how many files were chosen or refused, and each file that is added or fails - never each step of
 * progress. Focus never falls off the page: a button that goes leaves it on the nearest one that stays.
 */
@Component({
  selector: 'pp-uploader',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpIconButton, PpCloseIcon, PpFieldError, PpNotice, PpProgress, PpStatusPill],
  template: `
    <div class="zone" [attr.data-dragging]="dragging() ? '' : null">
      <button
        #choose
        pp-button
        [variant]="variant()"
        [disabled]="disabled()"
        [attr.aria-describedby]="hint() ? hintId : null"
        (click)="picker.click()"
      >
        {{ label() }}
      </button>
      <p class="drop" aria-hidden="true">or drop {{ multiple() ? 'files' : 'a file' }} here</p>
      @if (hint(); as hint) {
        <p class="hint" [id]="hintId">{{ hint }}</p>
      }
      <input
        #picker
        type="file"
        hidden
        [attr.accept]="accept() || null"
        [multiple]="multiple()"
        [disabled]="disabled()"
        (change)="picked($event)"
      />
    </div>
    @if (refused().length > 0) {
      <pp-notice
        class="refused"
        tone="warning"
        [heading]="refusedHeading()"
        dismissible
        (dismissed)="dismissRefusals()"
      >
        <ul class="reasons">
          @for (refusal of refused(); track $index) {
            <li>{{ refusal.name }}: {{ refusal.reason }}</li>
          }
        </ul>
      </pp-notice>
    }
    @if (items().length > 0) {
      <ul class="items" [attr.aria-label]="itemsLabel()">
        @for (item of items(); track item.id) {
          <li class="item" [attr.data-status]="item.status">
            @if (item.status === 'uploading') {
              <pp-progress class="progress" [label]="item.name" [value]="item.progress" showValue />
            } @else {
              <span class="name" [id]="nameId(item)">{{ item.name }}</span>
              @switch (item.status) {
                @case ('waiting') {
                  <pp-status-pill>Waiting</pp-status-pill>
                }
                @case ('done') {
                  <pp-status-pill tone="success">Added</pp-status-pill>
                }
                @case ('failed') {
                  <pp-status-pill tone="danger">Not added</pp-status-pill>
                }
              }
            }
            <div class="actions">
              @if (item.status === 'failed') {
                <button
                  pp-button
                  size="sm"
                  [attr.aria-describedby]="retryDescribedBy(item)"
                  (click)="retryItem(item, $event)"
                >
                  Try again
                </button>
              }
              <button
                pp-icon-button
                class="remove"
                variant="quiet"
                size="sm"
                [label]="'Remove ' + item.name"
                (click)="removeItem(item, $event)"
              >
                <pp-close-icon />
              </button>
            </div>
            @if (item.status === 'failed' && item.error) {
              <pp-field-error class="error" [errorId]="errorId(item)" [message]="item.error" />
            }
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './uploader.css',
  host: {
    tabindex: '-1',
    '(dragover)': 'over($event)',
    '(dragleave)': 'leave($event)',
    '(drop)': 'dropped($event)',
  },
})
export class PpUploader {
  /** The button's words, saying what is added: "Choose photos". */
  readonly label = input.required<string>();
  /** What may be added, in plain words; also read with the button: "JPEG, PNG, WebP, or HEIC, up to 20 MB." */
  readonly hint = input<string>();
  /** The browser's own `accept` list: `image/jpeg,image/png,.heic`. Empty takes any file. */
  readonly accept = input('');
  /** What `accept` means in a sentence, for a refused file: "a JPEG, PNG, WebP, or HEIC image". */
  readonly acceptText = input('a kind of file this takes');
  readonly maxBytes = input<number>();
  readonly multiple = input(true, { transform: booleanAttribute });
  /** Primary when choosing files is the page's one obvious next step. */
  readonly variant = input<'primary' | 'secondary'>('secondary');
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly items = input<readonly PpUploadItem[]>([]);
  /** The list's name for a screen reader. */
  readonly itemsLabel = input('Files');

  readonly filesChosen = output<File[]>();
  readonly retry = output<string>();
  readonly remove = output<string>();

  private readonly base = nextId('uploader');
  protected readonly hintId = `${this.base}-hint`;
  protected readonly dragging = signal(false);
  protected readonly refused = signal<readonly Refusal[]>([]);
  protected readonly refusedHeading = computed(() =>
    this.refused().length === 1
      ? "1 file can't be added"
      : `${this.refused().length} files can't be added`,
  );

  private readonly toaster = inject(PpToaster);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly chooseButton = viewChild.required('choose', {
    read: ElementRef<HTMLButtonElement>,
  });
  /** Each item's status when last drawn, so a change to done or failed is said once, and a first sight never. */
  private seen = new Map<string, PpUploadStatus>();

  constructor() {
    effect(() => {
      const items = this.items();
      untracked(() => this.sayFinished(items));
    });
  }

  protected nameId(item: PpUploadItem): string {
    return `${this.base}-item-${this.items().indexOf(item)}`;
  }

  protected errorId(item: PpUploadItem): string {
    return `${this.nameId(item)}-error`;
  }

  /** Try again is one of many, so it is described by its file's name, and by why that file failed. */
  protected retryDescribedBy(item: PpUploadItem): string {
    return item.error ? `${this.nameId(item)} ${this.errorId(item)}` : this.nameId(item);
  }

  protected picked(event: Event): void {
    const picker = event.target as HTMLInputElement;
    this.take([...(picker.files ?? [])]);
    // The same file chosen again must still say it changed.
    picker.value = '';
  }

  protected over(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) {
      return;
    }
    // Held even while disabled: a file let fall through is opened by the browser, and the page with it is gone.
    event.preventDefault();
    event.dataTransfer.dropEffect = this.disabled() ? 'none' : 'copy';
    this.dragging.set(!this.disabled());
  }

  protected leave(event: DragEvent): void {
    const host = event.currentTarget as HTMLElement;
    if (!host.contains(event.relatedTarget as Node | null)) {
      this.dragging.set(false);
    }
  }

  protected dropped(event: DragEvent): void {
    this.dragging.set(false);
    if (!event.dataTransfer) {
      return;
    }
    event.preventDefault();
    if (!this.disabled()) {
      this.take([...event.dataTransfer.files]);
    }
  }

  protected dismissRefusals(): void {
    this.focusChoose();
    this.refused.set([]);
  }

  /** Try again goes once the file is sent again, so focus waits on the same file's Remove, which every file has. */
  protected retryItem(item: PpUploadItem, event: Event): void {
    removeButton(itemOf(event))?.focus();
    this.retry.emit(item.id);
  }

  /** The file is about to go, and its button with it: focus moves to the next file's Remove, else the one before. */
  protected removeItem(item: PpUploadItem, event: Event): void {
    const row = itemOf(event);
    const neighbour =
      removeButton(row?.nextElementSibling ?? null) ??
      removeButton(row?.previousElementSibling ?? null);
    if (neighbour) {
      neighbour.focus();
    } else {
      this.focusChoose();
    }
    this.remove.emit(item.id);
  }

  /** The choose button, or the uploader itself while disabled, so focus always has somewhere to stay. */
  private focusChoose(): void {
    const choose = this.chooseButton().nativeElement;
    if (choose.disabled) {
      this.host.focus();
    } else {
      choose.focus();
    }
  }

  private take(files: File[]): void {
    if (files.length === 0) {
      return;
    }
    const accepted: File[] = [];
    const refused: Refusal[] = [];
    for (const file of files) {
      const reason =
        !this.multiple() && accepted.length > 0
          ? 'only one file can be added at a time.'
          : this.refusal(file);
      if (reason) {
        refused.push({ name: file.name, reason });
      } else {
        accepted.push(file);
      }
    }
    this.refused.set(refused);
    const words = [
      accepted.length > 0 ? `${count(accepted.length)} chosen.` : '',
      refused.length > 0
        ? `${count(refused.length)} can't be added. ${refused.length === 1 ? 'The reason is' : 'The reasons are'} listed below.`
        : '',
    ];
    this.toaster.announce(words.filter((part) => part !== '').join(' '));
    if (accepted.length > 0) {
      this.filesChosen.emit(accepted);
    }
  }

  private refusal(file: File): string | null {
    if (!acceptsFile(this.accept(), file)) {
      return `it isn't ${this.acceptText()}.`;
    }
    const max = this.maxBytes();
    if (max !== undefined && file.size > max) {
      return `it's larger than ${sizeInWords(max)}.`;
    }
    return null;
  }

  private sayFinished(items: readonly PpUploadItem[]): void {
    const said: string[] = [];
    for (const item of items) {
      const was = this.seen.get(item.id);
      if (was !== undefined && was !== item.status) {
        if (item.status === 'done') {
          said.push(`${item.name} added.`);
        } else if (item.status === 'failed') {
          said.push(`${item.name} not added${item.error ? `: ${item.error}` : '.'}`);
        }
      }
    }
    this.seen = new Map(items.map((item) => [item.id, item.status]));
    if (said.length > 0) {
      this.toaster.announce(said.join(' '));
    }
  }
}

function itemOf(event: Event): Element | null {
  return (event.currentTarget as HTMLElement).closest('li');
}

function removeButton(item: Element | null): HTMLElement | null {
  return item?.querySelector<HTMLElement>('button.remove') ?? null;
}

function count(files: number): string {
  return files === 1 ? '1 file' : `${files} files`;
}

/**
 * Whether a file matches the browser's own `accept` list: `.ext` by the name's ending, `type/*` by the type's start,
 * and `type/sub` exactly, each ignoring case. The browser filters its picker by the same list, but a drop is never
 * filtered, and a phone's picker may offer anything.
 */
export function acceptsFile(accept: string, file: { name: string; type: string }): boolean {
  const tokens = accept
    .split(',')
    .map((token) => token.trim().toLowerCase())
    .filter((token) => token !== '');
  if (tokens.length === 0) {
    return true;
  }
  const name = file.name.toLowerCase();
  const type = file.type.toLowerCase();
  return tokens.some((token) => {
    if (token.startsWith('.')) {
      return name.endsWith(token);
    }
    if (token.endsWith('/*')) {
      return type.startsWith(token.slice(0, -1));
    }
    return type === token;
  });
}

/** A byte count in words, the same everywhere it renders: "20 MB", "1.5 MB", "300 KB". */
export function sizeInWords(bytes: number): string {
  const mb = 1024 * 1024;
  if (bytes >= mb) {
    return `${Math.round((bytes / mb) * 10) / 10} MB`;
  }
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
