// @ts-check
const { defineConfig } = require('eslint/config');
const rootConfig = require('../../eslint.config.js');
const ssrSafety = require('../../eslint.ssr-safety.js');

const boundary = 'The public site imports @pantrypress/ui/themes only (PP-D07).';

module.exports = defineConfig([
  ...rootConfig,
  {
    files: ['**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'pps',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'pps',
          style: 'kebab-case',
        },
      ],
    },
  },
  // PP-D07, PP-D08: the public site uses @pantrypress/ui/themes only; admin code never reaches the SSR bundle.
  {
    files: ['**/*.ts'],
    rules: {
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
  {
    files: ['**/*.ts'],
    ignores: ['**/*.spec.ts'],
    rules: ssrSafety,
  },
]);
