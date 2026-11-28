// Captures full-page screenshots of given routes at the four approved breakpoints and reports
// console errors and horizontal overflow. Owner: F — Foundation.
// Usage: node tools/capture-screens.mjs <baseUrl> <outDir> <route> [route...]
// Uses the system Chrome when Playwright's bundled browser is not installed (CHROME_PATH).
import { chromium } from '@playwright/test';
import { mkdirSync } from 'node:fs';

const [baseUrl, outDir, ...routes] = process.argv.slice(2);
const WIDTHS = [360, 768, 1024, 1280];
mkdirSync(outDir, { recursive: true });

const browser = await chromium.launch({
  executablePath: process.env.CHROME_PATH || undefined,
  args: ['--no-sandbox'],
});
let problems = 0;
for (const route of routes) {
  for (const width of WIDTHS) {
    const page = await browser.newPage({ viewport: { width, height: 900 } });
    const errors = [];
    page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto(baseUrl + route, { waitUntil: 'networkidle' });
    await page.waitForTimeout(300);
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - window.innerWidth,
    );
    const name = `${route.replace(/\W+/g, '_').replace(/^_|_$/g, '') || 'root'}-${width}.png`;
    await page.screenshot({ path: `${outDir}/${name}`, fullPage: true });
    const flag = overflow > 0 || errors.length ? 'CHECK' : 'OK   ';
    if (flag !== 'OK   ') problems++;
    console.log(
      `${flag} ${route} @${width}px  overflowX=${overflow}px  consoleErrors=${errors.length}${errors.length ? ' :: ' + errors.slice(0, 2).join(' | ') : ''}`,
    );
    await page.close();
  }
}
await browser.close();
console.log(
  problems
    ? `${problems} capture(s) need attention.`
    : 'All captures OK (no horizontal overflow, no console errors).',
);
