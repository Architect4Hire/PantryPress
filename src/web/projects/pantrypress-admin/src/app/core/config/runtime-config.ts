import { isRecord } from '../api/api-problem';

/**
 * What the server publishes for the platform home to read at run time (PP3.7): the switches a deployment sets, which
 * the app cannot know when it is built. It holds no session, account, site, or price (frontend.md). Each field joins
 * with the prompt whose server publishes it, and its default is the closed or hidden state, so a page that could not
 * read the document - or a prerendered one, which never reads it - shows nothing it should not. PPL.13 adds the first,
 * `publicSignUp` (default `false`).
 *
 * The server is the gate for every switch here; this is courtesy, so a screen does not invite what the API refuses.
 */
export type RuntimeConfig = Readonly<Record<never, never>>;

export const RUNTIME_CONFIG_DEFAULTS: RuntimeConfig = {};

/**
 * Reads the server's document: only the fields the app knows, each checked for its type and defaulted otherwise, so a
 * server that adds a field never breaks an app built before it. A body that is not a JSON object reads as nothing.
 */
export function readRuntimeConfig(body: unknown): RuntimeConfig | null {
  if (!isRecord(body)) {
    return null;
  }
  return { ...RUNTIME_CONFIG_DEFAULTS };
}
