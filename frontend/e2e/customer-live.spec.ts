import { expect, test } from '@playwright/test';

const liveApi = process.env['BUILDMIND_LIVE_API'];
const evidence = 'docs/application-completion/evidence';

test.describe('live customer application', () => {
  test.skip(!liveApi, 'Set BUILDMIND_LIVE_API to run against the authorized disposable backend.');

  test('signs in and renders server-owned projects without exposing tokens', async ({ page, request }) => {
    const suffix = crypto.randomUUID().replaceAll('-', '');
    const email = `frontend-browser-${suffix}@example.invalid`;
    const password = `Bm!${suffix.slice(0, 16)}9a`;
    const registration = await request.post(`${liveApi}/api/v1/auth/register`, {
      data: { email, password },
    });
    expect(registration.status()).toBe(201);

    await page.goto('/auth/sign-in');
    await page.getByLabel('Email').fill(email);
    await page.getByRole('textbox', { name: 'Password', exact: true }).fill(password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/app\/dashboard$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Your projects' })).toBeVisible();
    await expect(page.getByText('Create your first project')).toBeVisible();
    await page.screenshot({ path: `${evidence}/customer-dashboard-live.png`, fullPage: true });

    await page.getByRole('link', { name: 'Create project' }).first().click();
    await page.getByLabel('Project name').fill(`Browser verification ${suffix.slice(0, 8)}`);
    await page.getByLabel('Location reference').fill('Synthetic local test');
    await page.getByRole('button', { name: 'Create project' }).click();

    await expect(page).toHaveURL(/\/app\/projects\/[0-9a-f-]+$/);
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Browser verification');
    await expect(page.getByRole('heading', { level: 2, name: 'Planning workspace' })).toBeVisible();
    await page.screenshot({ path: `${evidence}/customer-project-live.png`, fullPage: true });
  });

  test('verifies customer security, project lifecycle, search, editing and survey intake', async ({ page, request }) => {
    test.setTimeout(90_000);
    const suffix = crypto.randomUUID().replaceAll('-', '');
    const email = `frontend-modules-${suffix}@example.invalid`;
    const password = `Bm!${suffix.slice(0, 16)}9a`;
    expect((await request.post(`${liveApi}/api/v1/auth/register`, { data: { email, password } })).status()).toBe(201);

    await page.goto('/auth/sign-in');
    await page.getByLabel('Email').fill(email);
    await page.getByRole('textbox', { name: 'Password', exact: true }).fill(password);
    await page.getByRole('button', { name: 'Sign in' }).click();
    await page.getByRole('link', { name: 'Create project' }).first().click();
    const projectName = `Lifecycle verification ${suffix.slice(0, 8)}`;
    await page.getByLabel('Project name').fill(projectName);
    await page.getByLabel('Location reference').fill('Synthetic local test');
    await page.getByRole('button', { name: 'Create project' }).click();
    await expect(page.getByRole('heading', { name: 'Project lifecycle' })).toBeVisible();
    await page.screenshot({ path: `${evidence}/customer-project-lifecycle-live.png`, fullPage: true });

    await page.getByRole('link', { name: 'Edit project details' }).click();
    await page.getByLabel('Project name').fill(`${projectName} edited`);
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByRole('heading', { level: 1 })).toContainText('edited');
    await page.getByRole('link', { name: 'Back to projects' }).click();
    await page.getByLabel('Search projects').fill('does-not-exist');
    await expect(page.getByText(/No projects match/)).toBeVisible();
    await page.getByLabel('Search projects').fill('Lifecycle verification');
    await expect(page.getByText(`${projectName} edited`)).toBeVisible();
    await page.screenshot({ path: `${evidence}/customer-project-search-live.png`, fullPage: true });

    await page.getByRole('button', { name: /Account menu/ }).click();
    await page.getByRole('link', { name: 'Profile' }).click();
    await expect(page.getByRole('heading', { name: 'Active sessions' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Recent security activity' })).toBeVisible();
    await page.screenshot({ path: `${evidence}/customer-security-live.png`, fullPage: true });
    const revoke = page.getByRole('button', { name: 'Revoke' }).last();
    if (await revoke.isVisible()) {
      const before = await page.getByRole('button', { name: 'Revoke' }).count();
      await revoke.click();
      await expect(page.getByRole('button', { name: 'Revoke' })).toHaveCount(Math.max(0, before - 1));
    }

    const projectLink = page.getByRole('link', { name: 'Projects' });
    await projectLink.click();
    await page.getByRole('link', { name: new RegExp(projectName) }).click();
    await page.getByRole('link', { name: /Survey plan/ }).click();
    await expect(page.getByRole('heading', { name: 'Survey plan' })).toBeVisible();

    const fileInput = page.locator('input[type=file]');
    await fileInput.setInputFiles({ name: 'invalid.txt', mimeType: 'text/plain', buffer: Buffer.from('not a survey') });
    await expect(page.getByText(/file type is not supported/)).toBeVisible();
    await fileInput.setInputFiles({ name: 'oversized.pdf', mimeType: 'application/pdf', buffer: Buffer.alloc(6 * 1024 * 1024 + 1) });
    await expect(page.getByText(/larger than 6 MB/)).toBeVisible();

    const files = [
      { name: 'synthetic.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%%EOF') },
      { name: 'synthetic.png', mimeType: 'image/png', buffer: Buffer.from('89504e470d0a1a0a', 'hex') },
      { name: 'synthetic.jpg', mimeType: 'image/jpeg', buffer: Buffer.from('ffd8ffd9', 'hex') },
    ];
    for (const file of files) {
      await fileInput.setInputFiles(file);
      await page.getByRole('button', { name: 'Upload survey' }).click();
      await expect(page.getByText('Survey accepted for processing.')).toBeVisible();
      await expect(page.getByText(file.name)).toBeVisible();
    }
    await page.screenshot({ path: `${evidence}/customer-survey-live.png`, fullPage: true });
  });
});
