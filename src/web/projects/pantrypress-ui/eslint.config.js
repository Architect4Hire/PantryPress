// @ts-check
const { defineConfig } = require('eslint/config');
const rootConfig = require('../../eslint.config.js');
const ssrSafety = require('../../eslint.ssr-safety.js');

const boundary = '@pantrypress/ui/themes never imports the admin entry point (PP-D07).';

module.exports = defineConfig([
  ...rootConfig,
  {
    files: ['**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'pp',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'pp',
          style: 'kebab-case',
        },
      ],
    },
  },
  // PP-D07: the themes entry point is SSR-safe and never pulls in admin code. Relative imports out of themes/ also fail
  // the ng-packagr build.
  {
    files: ['**/themes/**/*.ts'],
    ignores: ['**/*.spec.ts'],
    rules: {
      ...ssrSafety,
      'no-restricted-imports': [
        'error',
        {
          paths: [{ name: '@pantrypress/ui', message: boundary }],
          patterns: [
            { group: ['**/pantrypress-admin/**', '**/pantrypress-ui/src/**'], message: boundary },
          ],
        },
      ],
    },
  },
]);
