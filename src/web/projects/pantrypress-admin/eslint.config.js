// @ts-check
const { defineConfig } = require('eslint/config');
const rootConfig = require('../../eslint.config.js');

const boundary = 'The admin app imports @pantrypress/ui only (PP-D07).';

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
    },
  },
  // PP-D07: the admin app uses @pantrypress/ui only; reader components stay out of the admin bundle.
  {
    files: ['**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [{ name: '@pantrypress/ui/themes', message: boundary }],
          patterns: [
            { group: ['**/pantrypress-site/**', '**/pantrypress-ui/themes/**'], message: boundary },
          ],
        },
      ],
    },
  },
]);
