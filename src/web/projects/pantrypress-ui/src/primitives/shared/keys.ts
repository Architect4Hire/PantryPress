/** Whether the element sits in a right-to-left part of the page, by the nearest `dir` (any case). */
export function isRightToLeft(element: Element): boolean {
  return element.closest('[dir]')?.getAttribute('dir')?.toLowerCase() === 'rtl';
}

/**
 * Where a key moves focus in a row of `count` items from `from` (PP3.4): the arrow keys step and wrap, turned around
 * right to left; Home and End go to the ends. Null for any other key, and for one pressed with Alt, Control, or Meta,
 * which belong to the browser (Alt+Left is Back).
 */
export function arrowStep(
  event: KeyboardEvent,
  element: Element,
  from: number,
  count: number,
): number | null {
  if (event.altKey || event.ctrlKey || event.metaKey || count === 0) {
    return null;
  }
  const forward = isRightToLeft(element) ? -1 : 1;
  switch (event.key) {
    case 'ArrowRight':
      return (from + forward + count) % count;
    case 'ArrowLeft':
      return (from - forward + count) % count;
    case 'Home':
      return 0;
    case 'End':
      return count - 1;
    default:
      return null;
  }
}
