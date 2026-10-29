// @ts-check
const { defineConfig } = require('eslint/config');
const rootConfig = require('../../eslint.config.js');
const ssrSafety = require('../../eslint.ssr-safety.js');

const boundary = 'The admin app imports @pantrypress/ui only (PP-D07).';
const httpOnlyInApi =
  'Only core/api/ talks HTTP (PP3.7): use ApiClient through a typed feature client, never HttpClient.';

// PP-D07: the admin app uses @pantrypress/ui only; reader components stay out of the admin bundle.
const themesPaths = [{ name: '@pantrypress/ui/themes', message: boundary }];
const themesPatterns = [
  { group: ['**/pantrypress-site/**', '**/pantrypress-ui/themes/**'], message: boundary },
];

// frontend.md: the app talks only to the BFF, through ApiClient. A raw fetch would skip its antiforgery token and its
// problem reading alike. A lint rule cannot see every alias, so this is a net, not a wall.
const httpGlobals = ['fetch', 'XMLHttpRequest', 'EventSource', 'WebSocket'].map((name) => ({
  name,
  message: httpOnlyInApi,
}));
const httpProperties = [
  ...['window', 'globalThis', 'self'].flatMap((object) =>
    ['fetch', 'XMLHttpRequest', 'EventSource', 'WebSocket'].map((property) => ({
      object,
      property,
      message: httpOnlyInApi,
    })),
  ),
  { object: 'navigator', property: 'sendBeacon', message: httpOnlyInApi },
];

// PPL.2: what the build prerenders runs under Node - the public pages, the app's root and configuration, and the
// core services every page starts. Browser-only hooks opt out per line with an eslint-disable comment that says why.
const prerendered = [
  '**/src/main.server.ts',
  '**/src/app/*.ts',
  '**/src/app/core/**/*.ts',
  '**/src/app/features/landing/**/*.ts',
  '**/src/app/features/legal/**/*.ts',
  '**/src/app/features/not-found/**/*.ts',
];

module.exports = defineConfig([
  ...rootConfig,
  {
    files: ['**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'ppa',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'ppa',
          style: 'kebab-case',
        },
      ],
      'no-restricted-globals': ['error', ...httpGlobals],
      'no-restricted-properties': ['error', ...httpProperties],
    },
  },
  // PP3.7: components never inject HttpClient. Outside core/api/ nothing may import Angular's HTTP package at all,
  // so neither a component nor a service beside one can reach the BFF except through ApiClient.
  {
    files: ['**/*.ts'],
    ignores: ['**/src/app/core/api/**'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [...themesPaths, { name: '@angular/common/http', message: httpOnlyInApi }],
          patterns: [
            ...themesPatterns,
            { group: ['@angular/common/http/*'], message: httpOnlyInApi },
          ],
        },
      ],
    },
  },
  {
    files: ['**/src/app/core/api/**/*.ts'],
    rules: {
      'no-restricted-imports': ['error', { paths: themesPaths, patterns: themesPatterns }],
    },
  },
  // A later block replaces these rules for the files it names rather than merging them, so the HTTP net comes too.
  {
    files: prerendered,
    ignores: ['**/*.spec.ts'],
    rules: {
      'no-restricted-globals': [...ssrSafety['no-restricted-globals'], ...httpGlobals],
      'no-restricted-properties': [...ssrSafety['no-restricted-properties'], ...httpProperties],
      'no-restricted-syntax': ssrSafety['no-restricted-syntax'],
    },
  },
]);
