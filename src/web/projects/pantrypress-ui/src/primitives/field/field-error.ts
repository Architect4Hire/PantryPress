import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The error line a field or radio group shows: an icon and the words, so an error is never told by color alone. The
 * words come from the caller in plain language; the primitive never invents them. Internal to the library.
 */
@Component({
  selector: 'pp-field-error',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      class="icon"
      viewBox="0 0 20 20"
      width="16"
      height="16"
      aria-hidden="true"
      focusable="false"
    >
      <path
        fill="currentColor"
        d="M10 1.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Zm-.9 4.25h1.8v5.5H9.1v-5.5Zm.9 9.25a1.15 1.15 0 1 1 0-2.3 1.15 1.15 0 0 1 0 2.3Z"
      />
    </svg>
    <span class="visually-hidden">Error: </span>{{ message() }}
  `,
  styleUrl: './field-error.css',
  host: { '[id]': 'errorId()' },
})
export class PpFieldError {
  readonly errorId = input.required<string>();
  readonly message = input.required<string>();
}
