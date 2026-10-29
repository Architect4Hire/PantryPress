/**
 * A refusal or failure, as the admin app reads it (PP3.7). The API and the gateway answer every failure with an
 * RFC 9457 problem document carrying a stable `code` and a plain-language `title` (`api-contract.md`), so a screen
 * decides by `code` and may show `title` as it stands. Two codes are the app's own, for answers that never were a
 * problem document: `unreachable` when no answer came at all, and `unexpected-error` (the servers' own code for a
 * fault) when one came that the app cannot read.
 */
export interface ApiProblem {
  /** The HTTP status; 0 when no answer came. */
  readonly status: number;
  readonly code: string;
  readonly title: string;
  /** Field errors, keyed by field, on a validation failure. Absent on a plain refusal. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
  /** Seconds to wait before trying again, from `Retry-After` (a lockout's or a rate limit's 429, a 409 in use). */
  readonly retryAfter?: number;
  /** The server's trace id, for a report to support. */
  readonly traceId?: string;
}

/** What every call answers: the value, or the problem. Expected failures are values here, never exceptions. */
export type ApiResult<T> =
  | { readonly ok: true; readonly status: number; readonly value: T }
  | { readonly ok: false; readonly problem: ApiProblem };

export const UNREACHABLE_CODE = 'unreachable';
export const UNREACHABLE_TITLE =
  "We couldn't reach PantryPress. Check your connection and try again.";
export const UNEXPECTED_CODE = 'unexpected-error';
// The servers' own title for the same code (GatewayApi.ServerErrorTitle), so the words match whoever failed.
export const UNEXPECTED_TITLE = 'Something went wrong on our side. Please try again in a moment.';
// For one of ours that came without a title of its own, which only a framework default could leave out.
export const REFUSED_TITLE = "That didn't work. Please try again.";

/**
 * Reads a failed answer's body as a problem document. The status is the answer's, never the body's. A body without a
 * string `code` is not one of ours - a proxy's page, a truncated body - and reads as `unexpected-error`; one with a
 * code but no title keeps its code and takes a plain title.
 */
export function readProblem(
  status: number,
  body: unknown,
  retryAfterHeader: string | null,
): ApiProblem {
  const retryAfter = readRetryAfter(retryAfterHeader);

  if (status === 0) {
    return { status, code: UNREACHABLE_CODE, title: UNREACHABLE_TITLE };
  }

  const traceId = isRecord(body) ? body['traceId'] : undefined;
  if (!isRecord(body) || typeof body['code'] !== 'string') {
    return {
      status,
      code: UNEXPECTED_CODE,
      title: UNEXPECTED_TITLE,
      ...(retryAfter === undefined ? {} : { retryAfter }),
      ...(typeof traceId === 'string' ? { traceId } : {}),
    };
  }

  const errors = readErrors(body['errors']);
  const title = body['title'];
  return {
    status,
    code: body['code'],
    title: typeof title === 'string' ? title : status >= 500 ? UNEXPECTED_TITLE : REFUSED_TITLE,
    ...(errors === undefined ? {} : { errors }),
    ...(retryAfter === undefined ? {} : { retryAfter }),
    ...(typeof traceId === 'string' ? { traceId } : {}),
  };
}

// The servers send `Retry-After` in seconds; an HTTP date, which they never send, is left unread rather than guessed.
function readRetryAfter(header: string | null): number | undefined {
  return header !== null && /^\d+$/.test(header.trim()) ? Number(header.trim()) : undefined;
}

function readErrors(value: unknown): Readonly<Record<string, readonly string[]>> | undefined {
  if (!isRecord(value)) {
    return undefined;
  }

  const errors: Record<string, readonly string[]> = {};
  for (const [field, messages] of Object.entries(value)) {
    if (Array.isArray(messages) && messages.every((message) => typeof message === 'string')) {
      errors[field] = messages;
    }
  }
  // An empty `errors` is a plain refusal, which carries none.
  return Object.keys(errors).length === 0 ? undefined : errors;
}

export function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
