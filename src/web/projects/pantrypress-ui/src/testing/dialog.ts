/**
 * jsdom has no `showModal()` or `close()` on `<dialog>`. This stand-in opens and closes one as a browser does,
 * firing `close`, so the specs can prove what the primitives do. What only a browser does - the inert page, the
 * trapped focus, Escape's `cancel`, the backdrop - is proven in the showcase's browser run (PP3.6). Specs only.
 */
export function installDialogStandIn(): () => void {
  const proto = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
  const had = { showModal: proto['showModal'], close: proto['close'] };
  proto['showModal'] = function (this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  proto['close'] = function (this: HTMLDialogElement) {
    if (this.hasAttribute('open')) {
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    }
  };
  return () => {
    proto['showModal'] = had.showModal;
    proto['close'] = had.close;
  };
}

/** What a browser does on Escape in a modal dialog: `cancel`, then, unless that was prevented, closing it. */
export function pressEscape(dialog: HTMLDialogElement): void {
  const cancel = new Event('cancel', { cancelable: true });
  if (dialog.dispatchEvent(cancel)) {
    dialog.close();
  }
}
