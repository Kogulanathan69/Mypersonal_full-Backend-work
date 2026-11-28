// Verifies WCAG 2.2 contrast for every approved token pairing, reading values from tokens.css.
// Owner: F — Foundation. Usage: npm run check:contrast
import { readFileSync } from 'node:fs';

const css = readFileSync(new URL('../src/styles/tokens.css', import.meta.url), 'utf8');
const raw = Object.fromEntries(
  [...css.matchAll(/--color-([\w-]+):\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]),
);
function resolve(name, depth = 0) {
  const v = raw[name];
  if (!v || depth > 5) throw new Error(`Unknown token --color-${name}`);
  const ref = v.match(/^var\(--color-([\w-]+)\)$/);
  return ref ? resolve(ref[1], depth + 1) : v;
}
function luminance(hex) {
  const h = hex.replace('#', '');
  const c = [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16) / 255);
  const [r, g, b] = c.map((x) => (x <= 0.03928 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
const ratio = (a, b) => {
  const [l1, l2] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (l1 + 0.05) / (l2 + 0.05);
};

// [foreground, background, minimum, purpose]
const TEXT = 4.5,
  LARGE = 3,
  UI = 3;
const backgrounds = ['bg-deep', 'bg', 'surface', 'surface-raised'];
const pairs = [];
for (const bg of backgrounds) {
  for (const fg of ['text', 'muted', 'subtle', 'accent', 'success', 'warning', 'danger', 'info']) {
    pairs.push([fg, bg, TEXT, 'body text']);
  }
  pairs.push(['border-strong', bg, UI, 'input / control border']);
  pairs.push(['focus', bg, UI, 'focus indicator']);
}
pairs.push(['on-accent', 'accent', TEXT, 'text on gold button']);
pairs.push(['on-accent', 'accent-hover', TEXT, 'text on gold button (hover)']);
pairs.push(['ink', 'paper', TEXT, 'plan labels on paper']);
// Cinematic marketing theme (public Home Page only)
for (const bg of ['mk-navy', 'mk-surface', 'mk-surface-2']) {
  for (const fg of ['mk-white', 'mk-muted', 'mk-cyan', 'mk-gold']) {
    pairs.push([fg, bg, TEXT, 'marketing text']);
  }
}
pairs.push(['mk-navy', 'mk-cyan', TEXT, 'text on cyan button']);

let failed = 0;
const rows = pairs.map(([fg, bg, min, purpose]) => {
  const r = ratio(resolve(fg), resolve(bg));
  const ok = r >= min;
  if (!ok) failed++;
  return `${ok ? 'PASS' : 'FAIL'}  ${fg.padEnd(14)} on ${bg.padEnd(15)} ${r.toFixed(2).padStart(6)}:1  (min ${min}:1, ${purpose})`;
});
console.log(rows.join('\n'));
if (failed) {
  console.error(`check:contrast FAILED — ${failed} of ${pairs.length} pairings below minimum.`);
  process.exit(1);
}
console.log(
  `check:contrast passed — ${pairs.length} of ${pairs.length} pairings meet WCAG 2.2 AA.`,
);
