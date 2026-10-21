import { test, expect } from '@playwright/test';

test('reader can verify an account, subscribe, inspect delivery and remove the account', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const email = `browser-${Date.now()}@example.com`;
  const password = 'Browser-Test-Only9!';
  await page.goto('/news');
  await expect(page.getByRole('heading', { name: 'The world feed.' })).toBeVisible();
  await expect(page.getByText('DEMO EDITION', { exact: false })).toBeVisible();
  await expect(page.locator('.news-card').first()).toBeVisible();
  await page.screenshot({ path: 'test-results/world-feed-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  await page.screenshot({ path: 'test-results/world-feed-mobile.png', fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto('/register');
  await page.getByLabel('Display name').fill('Browser Reader');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Create account →', exact: true }).click();
  const verification = page.getByRole('link', { name: 'Open email verification link ↗' });
  await expect(verification).toBeVisible();
  const url = new URL((await verification.getAttribute('href'))!);
  await page.goto(url.pathname + url.search);
  await page.getByRole('button', { name: 'Verify email →' }).click();
  await expect(page.getByRole('status')).toContainText('Email verified');
  await page.goto('/login');
  await page.getByLabel('Email address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in →', exact: true }).click();
  await expect(page).toHaveURL(/\/news$/);
  await expect(page.locator('.account-actions')).toContainText('Browser Reader');
  await page.goto('/subscriptions');
  await expect(page.getByRole('heading', { name: 'Your subscriptions.' })).toBeVisible();
  await page.getByRole('button', { name: 'Save my preferences →' }).click();
  await expect(page.getByRole('status')).toContainText('Preferences saved');
  await page.goto('/notifications');
  await expect(page.getByText('Your news subscription is active', { exact: true })).toBeVisible();
  await expect(page.getByText('Your initial world news digest', { exact: true })).toBeVisible();
  await page.goto('/admin/users');
  await expect(page).toHaveURL(/\/news$/);
  await page.goto('/settings');
  await page.getByLabel('Confirm your password').fill(password);
  await page.getByLabel('I understand this cannot be undone.').check();
  await page.getByRole('button', { name: 'Delete my account', exact: true }).click();
  await expect(page).toHaveURL(/\/register$/);
  expect(errors).toEqual([]);
});
