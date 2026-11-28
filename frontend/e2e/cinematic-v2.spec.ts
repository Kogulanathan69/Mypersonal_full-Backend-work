import { expect, test, type Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';

const evidence = 'docs/cinematic-v2/evidence';
const widths = [360, 390, 768, 1024, 1280, 1440, 1920] as const;

async function assertHealthy(page: Page): Promise<void> {
  await expect(page.locator('body')).not.toHaveCSS('overflow-x', 'scroll');
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
  expect(overflow).toBeLessThanOrEqual(1);
}

test('static architectural homepage is complete across responsive layouts', async ({ page }) => {
  test.setTimeout(360_000);
  const errors: string[] = [];
  const failed: string[] = [];
  page.on('console', (message) => message.type() === 'error' && errors.push(message.text()));
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('requestfailed', (request) => failed.push(`${request.method()} ${request.url()}`));

  for (const width of widths) {
    await page.setViewportSize({ width, height: width < 600 ? 844 : 900 });
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      /Plan your home\s+with confidence\./,
    );
    await expect(
      page.getByRole('list', { name: 'Static planning journey' }).locator('li'),
    ).toHaveCount(5);
    await expect(page.locator('canvas')).toHaveCount(0);
    await expect(page.getByText('Turn animation off')).toHaveCount(0);
    const loaded = await page
      .locator('.bm-static-hero__image')
      .evaluate((image: HTMLImageElement) => ({
        complete: image.complete,
        width: image.naturalWidth,
      }));
    expect(loaded.complete).toBe(true);
    expect(loaded.width).toBeGreaterThan(1000);
    await assertHealthy(page);
    await page.screenshot({ path: `${evidence}/static-home-${width}.png`, fullPage: true });
  }

  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');
  await page.locator('.bm-static-hero').screenshot({ path: `${evidence}/static-hero-desktop.png` });
  await page
    .locator('.bm-planning-journey')
    .screenshot({ path: `${evidence}/lower-theme-planning-journey.png` });
  await page
    .locator('#how-it-works')
    .screenshot({ path: `${evidence}/lower-theme-how-it-works.png` });
  await page
    .locator('section:has(#faq-heading)')
    .screenshot({ path: `${evidence}/lower-theme-faq.png` });
  await page.locator('footer').screenshot({ path: `${evidence}/lower-theme-footer.png` });
  await page
    .locator('section:has(#survey-heading)')
    .screenshot({ path: `${evidence}/product-survey-extraction.png` });
  await page
    .locator('section:has(#land-heading)')
    .screenshot({ path: `${evidence}/product-land-interpretation.png` });
  await page
    .locator('section:has(#gen-heading)')
    .screenshot({ path: `${evidence}/product-floor-plan-alternatives.png` });
  await page
    .getByRole('button', { name: /View detailed plan/ })
    .first()
    .click();
  await page
    .getByLabel('Expanded floor plan')
    .screenshot({ path: `${evidence}/product-floor-plan-expanded.png` });
  await page.getByRole('button', { name: /Close/ }).click();
  await page
    .locator('section:has(#editor-heading)')
    .screenshot({ path: `${evidence}/product-plan-editor.png` });
  await page.getByRole('button', { name: 'Select Living' }).click();
  await expect(page.getByLabel('Room properties')).toContainText('Living');
  await page
    .locator('section:has(#editor-heading)')
    .screenshot({ path: `${evidence}/product-selected-room.png` });
  await page
    .locator('section:has(#rag-heading)')
    .screenshot({ path: `${evidence}/product-regulation-evidence.png` });
  await page
    .locator('section:has(#ai-heading)')
    .screenshot({ path: `${evidence}/product-ai-refinement.png` });
  await page
    .locator('section:has(#architect-heading)')
    .screenshot({ path: `${evidence}/product-professional-review.png` });

  const contactWidths = [390, 768, 1280, 1440] as const;
  const panels = await Promise.all(
    contactWidths.map(async (width) => {
      const bytes = await readFile(`${evidence}/static-home-${width}.png`);
      return { width, src: `data:image/png;base64,${bytes.toString('base64')}` };
    }),
  );
  await page.setViewportSize({ width: 1600, height: 900 });
  await page.setContent(
    `<style>html,body{margin:0;background:#071216;color:#fff;font-family:system-ui}body{display:grid;grid-template-columns:repeat(4,400px);gap:0}figure{position:relative;width:400px;height:900px;margin:0;overflow:hidden;background:#071216}img{width:100%;height:100%;object-fit:cover;object-position:top}figcaption{position:absolute;left:12px;top:12px;padding:6px 10px;border-radius:999px;background:#071216dd;font-size:13px}</style>${panels.map(({ width, src }) => `<figure><img src="${src}"><figcaption>${width}px viewport</figcaption></figure>`).join('')}`,
  );
  await page.screenshot({ path: `${evidence}/static-home-contact-sheet.png` });
  expect(errors).toEqual([]);
  expect(failed).toEqual([]);
});

test('navigation, public routes and authentication entry remain operable', async ({ page }) => {
  test.setTimeout(120_000);
  for (const route of [
    '/',
    '/about',
    '/contact',
    '/privacy',
    '/terms',
    '/auth/sign-in',
    '/auth/register',
  ]) {
    const response = await page.goto(route);
    expect(response?.ok(), route).toBe(true);
    await expect(page.locator('main')).toBeVisible();
  }

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  await page.getByRole('button', { name: 'Open menu' }).click();
  await expect(page.getByRole('dialog', { name: 'Menu' })).toBeVisible();
  await page.screenshot({ path: `${evidence}/static-mobile-navigation.png` });
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: 'Menu' })).toBeHidden();

  await expect(page.getByRole('link', { name: /Start the Planning Experience/ })).toHaveAttribute(
    'href',
    '/auth/register',
  );
  await expect(page.getByRole('link', { name: /See How It Works/ })).toHaveAttribute(
    'href',
    '/#how-it-works',
  );
});

test('homepage stays static for reduced-motion users and loads no cinematic runtime', async ({
  page,
}) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  await expect(page.locator('canvas')).toHaveCount(0);
  await expect(
    page.getByRole('list', { name: 'Static planning journey' }).locator('li'),
  ).toHaveCount(5);

  const resources = await page.evaluate(() =>
    performance.getEntriesByType('resource').map((entry) => entry.name.toLowerCase()),
  );
  expect(resources.some((url) => /cinematic-engine|three\.module|scrolltrigger/.test(url))).toBe(
    false,
  );
  await page.screenshot({ path: `${evidence}/static-reduced-motion.png`, fullPage: true });
});
