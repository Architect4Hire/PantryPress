// @ts-check
// SSR safety and deterministic output for the public site and @pantrypress/ui/themes (rendering.md, ARCH-005), and
// for what pantrypress-admin prerenders at build (frontend.md, PPL.2).
// Browser-only hooks opt out per line with an eslint-disable comment that says why.
const message =
  'This code renders on the server, so it must not need a browser and must be deterministic (rendering.md, frontend.md).';

module.exports = {
  'no-restricted-globals': [
    'error',
    ...[
      'window',
      'document',
      'localStorage',
      'sessionStorage',
      'setTimeout',
      'setInterval',
      'requestAnimationFrame',
    ].map((name) => ({ name, message })),
  ],
  'no-restricted-properties': [
    'error',
    { object: 'Math', property: 'random', message },
    { object: 'Date', property: 'now', message },
  ],
  'no-restricted-syntax': ['error', { selector: "NewExpression[callee.name='Date']", message }],
};
