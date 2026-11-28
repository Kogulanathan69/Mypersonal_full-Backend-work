// @ts-check
// Owner: F — Foundation. Other modules request changes through the Foundation owner.
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

/**
 * Module boundaries (see docs/architecture.md):
 *   F  core/, shared/, layouts/, dev/, features/{auth,system}/, customer.routes.ts, customer/shared-state/
 *   A  features/public/, features/customer/{dashboard,projects,land,requirements,account}/
 *   B  editor-engine/, features/customer/{generation,planning,completion}/
 *   C  features/admin/
 * A, B and C may import only from F folders (and B from editor-engine). Never from each other.
 */
const A_PATHS = [
  '**/features/public/**',
  '**/features/customer/dashboard/**',
  '**/features/customer/projects/**',
  '**/features/customer/land/**',
  '**/features/customer/requirements/**',
  '**/features/customer/account/**',
];
const B_PATHS = [
  '**/editor-engine/**',
  '**/features/customer/generation/**',
  '**/features/customer/planning/**',
  '**/features/customer/completion/**',
];
const C_PATHS = ['**/features/admin/**'];

// Maps an import glob such as features/public/** to the lint file glob for that folder.
const toFiles = (globs) => globs.map((g) => `src/app/${g.replace(/^\*\*\//, '')}/*.ts`);

module.exports = defineConfig([
  {
    ignores: ['dist/**', 'coverage/**', '.angular/**', 'node_modules/**', 'playwright-report/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'bm', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'bm', style: 'kebab-case' },
      ],
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@typescript-eslint/no-explicit-any': 'error',
      'no-console': ['error', { allow: ['warn', 'error'] }],
      // Separate rule name so it does not clash with the module-boundary rules below.
      '@typescript-eslint/no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: 'zod',
              message:
                "Use `import * as z from 'zod/mini'`: the full zod API adds ~440 kB to the bundle.",
            },
          ],
        },
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: "ImportDeclaration[source.value='zod/mini'] > ImportSpecifier",
          message:
            "Use `import * as z from 'zod/mini'`: named imports defeat tree-shaking (+400 kB).",
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {},
  },
  // Foundation code must never depend on feature modules.
  {
    files: ['src/app/core/**/*.ts', 'src/app/shared/**/*.ts', 'src/app/layouts/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/features/**', '**/editor-engine/**'],
              message: 'Foundation (core/shared/layouts) must not import feature modules.',
            },
          ],
        },
      ],
    },
  },
  {
    files: toFiles(A_PATHS),
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: [...B_PATHS, ...C_PATHS],
              message: 'Module A (Customer) may not import from 2D Planning (B) or Admin (C).',
            },
          ],
        },
      ],
    },
  },
  {
    files: toFiles(B_PATHS),
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: [...A_PATHS, ...C_PATHS],
              message: 'Module B (2D Planning) may not import from Customer (A) or Admin (C).',
            },
          ],
        },
      ],
    },
  },
  {
    files: toFiles(C_PATHS),
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: [...A_PATHS, ...B_PATHS],
              message: 'Module C (Admin) may not import from Customer (A) or 2D Planning (B).',
            },
          ],
        },
      ],
    },
  },
]);
