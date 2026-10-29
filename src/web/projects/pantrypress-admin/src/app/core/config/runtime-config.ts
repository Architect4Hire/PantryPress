import { isRecord } from '../api/api-problem';

/**
 * What the server publishes for the platform home to read at run time (PP3.7): the switches a deployment sets, which
 * the app cannot know when it is built. It holds no session, account, site, or price (frontend.md). Each field joins
 * with the prompt whose screen reads it, and its default is the closed or hidden state, so a page that could not read
 * the document - or a prerendered one, which never reads it - shows nothing it should not.
 *
 * The server is the gate for every switch here; this is courtesy, so a screen does not invite what the API refuses.
 */
export interface RuntimeConfig {
  /**
   * Whether anyone may create an account (BIZ-009). PP3.8's sign-up screen reads it; PPL.13 publishes it and refuses
   * registration while it is off. Until then the server says nothing, so sign-up reads as closed everywhere.
   */
  readonly publicSignUp: boolean;
}

export const RUNTIME_CONFIG_DEFAULTS: RuntimeConfig = { publicSignUp: false };

/**
 * Reads the server's document: only the fields the app knows, each checked for its type and defaulted otherwise, so a
 * server that adds a field never breaks an app built before it. A body that is not a JSON object reads as nothing.
 */
export function readRuntimeConfig(body: unknown): RuntimeConfig | null {
  if (!isRecord(body)) {
    return null;
  }
  const publicSignUp = body['publicSignUp'];
  return {
    ...RUNTIME_CONFIG_DEFAULTS,
    ...(typeof publicSignUp === 'boolean' ? { publicSignUp } : {}),
  };
}
