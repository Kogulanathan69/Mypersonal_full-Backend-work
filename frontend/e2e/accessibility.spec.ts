import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

for (const route of ['/', '/about', '/contact', '/auth/sign-in', '/auth/register']) {
  test(`has no automatically detectable WCAG A/AA violations: ${route}`, async ({ page }) => {
    await page.goto(route);
    await expect(page.locator('main')).toBeVisible();
    const result = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(result.violations).toEqual([]);
  });
}
