// Fails if any raw colour value appears outside src/styles/tokens.css.
// Owner: F — Foundation. Usage: npm run check:tokens
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

// URL.pathname returns `/C:/...` on Windows; passing that string through path.join produces
// `C:\C:\...`. fileURLToPath performs the platform-specific URL-to-filesystem conversion.
export function sourceRoot(moduleUrl = import.meta.url) {
  return fileURLToPath(new URL('../src/', moduleUrl));
}

const ROOT = sourceRoot();
const ALLOWED = [join('styles', 'tokens.css')];
const EXT = /\.(ts|html|css)$/;
const PATTERNS = [
  { name: 'hex colour', re: /(?<![\w&])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})\b/g },
  { name: 'rgb()/hsl() colour', re: /\b(?:rgba?|hsla?|oklch|oklab)\(\s*\d/g },
];

function walk(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    const p = join(dir, entry);
    if (statSync(p).isDirectory()) walk(p, out);
    else if (EXT.test(p)) out.push(p);
  }
  return out;
}

const problems = [];
for (const file of walk(ROOT)) {
  const rel = relative(ROOT, file);
  if (ALLOWED.includes(rel)) continue;
  const lines = readFileSync(file, 'utf8').split('\n');
  lines.forEach((line, i) => {
    for (const { name, re } of PATTERNS) {
      for (const m of line.matchAll(re)) {
        problems.push(`src${sep}${rel}:${i + 1}  ${name} "${m[0]}"`);
      }
    }
  });
}

if (problems.length) {
  console.error(`check:tokens FAILED — ${problems.length} raw colour value(s) outside tokens.css:`);
  for (const p of problems) console.error('  ' + p);
  process.exit(1);
}
console.log(
  `check:tokens passed — 0 raw colour values outside src/styles/tokens.css (${walk(ROOT).length} files scanned).`,
);
