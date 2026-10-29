/**
 * A path under the admin surface, relative to `API_BASE` (PP3.7): `sites/{slug}/members`, never `/api/v1/...`, a
 * query, or a fragment. A value spliced in by hand could carry a `/`, `?`, or `..` and reach another route, so a
 * path with a value in it is written with `apiPath`, which encodes each value as one segment.
 */
export type ApiPath = string & { readonly __apiPath: unique symbol };

// Unreserved characters and percent escapes only, in non-empty segments: no leading or doubled slash, no `?` or `#`.
const SHAPE = /^[A-Za-z0-9\-._~%]+(?:\/[A-Za-z0-9\-._~%]+)*$/;
// A dot segment, spelled out or escaped: a browser reads `%2e%2e` as `..` and climbs out of the surface.
const DOT_SEGMENT = /^(?:\.|%2e){1,2}$/i;

/** Accepts a written path, and throws for a malformed one: a bad path is a bug in the app, not a failure to show. */
export function toApiPath(path: string): ApiPath {
  if (!SHAPE.test(path) || path.split('/').some((segment) => DOT_SEGMENT.test(segment))) {
    throw new Error(`Not a path under the admin API: '${path}'.`);
  }
  return path as ApiPath;
}

/** `apiPath\`sites/${slug}/members/${accountId}\``: each value becomes exactly one encoded segment. */
export function apiPath(
  strings: TemplateStringsArray,
  ...values: readonly (string | number)[]
): ApiPath {
  const joined = strings.reduce((path, text, index) => {
    if (index === 0) {
      return text;
    }
    const value = String(values[index - 1]);
    // An empty value or a dot segment would leave or climb out of its place, whatever the encoding.
    if (value === '' || value === '.' || value === '..') {
      throw new Error(`Not a path segment: '${value}'.`);
    }
    return path + encodeSegment(value) + text;
  }, '');
  return toApiPath(joined);
}

// `encodeURIComponent` leaves `!'()*` as they are; they are not unreserved, so they are escaped too.
function encodeSegment(value: string): string {
  return encodeURIComponent(value).replace(
    /[!'()*]/g,
    (c) => `%${c.charCodeAt(0).toString(16).toUpperCase()}`,
  );
}
