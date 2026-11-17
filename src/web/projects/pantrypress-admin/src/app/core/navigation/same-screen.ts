/**
 * Navigation state for a move that keeps the reader on the screen they are on: only its address changes, as when a new
 * recipe gets its id (PP4.13). The dashboard's shell leaves focus where it is for such a move, as it does for one that
 * changes only the query (PP4.12b), so the person typing in a field keeps typing there.
 */
export const SAME_SCREEN_STATE: Readonly<Record<string, boolean>> = { ppSameScreen: true };

/** Whether a navigation's state says it stays on the same screen. */
export function isSameScreen(state: Readonly<Record<string, unknown>> | undefined): boolean {
  return state?.['ppSameScreen'] === true;
}
