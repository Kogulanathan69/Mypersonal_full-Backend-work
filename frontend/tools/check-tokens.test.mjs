import assert from 'node:assert/strict';
import { test } from 'node:test';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { sourceRoot } from './check-tokens.mjs';

test('sourceRoot resolves the project src directory as a native filesystem path', () => {
  const toolsDirectory = dirname(fileURLToPath(import.meta.url));
  assert.equal(sourceRoot(import.meta.url), join(toolsDirectory, '..', 'src') + '\\');
});

test('sourceRoot never duplicates the Windows drive prefix', () => {
  const root = sourceRoot(import.meta.url);
  assert.doesNotMatch(root, /^[A-Za-z]:[\\/][A-Za-z]:[\\/]/);
});
