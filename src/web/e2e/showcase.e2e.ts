import AxeBuilder from '@axe-core/playwright';
import { expect, Locator, Page, test } from '@playwright/test';

/*
 * The accessibility smoke test over the @pantrypress/ui showcase (PP3.6), in each browser. axe runs the same WCAG 2.x
 * A and AA rules as the library's specs, 2.2 included, but here with color contrast on, because a browser draws what
 * jsdom cannot. It also proves what the specs had to leave to a browser (PP3.2 to PP3.5): how the modes resolve, the
 * native modal dialog, the radio group's arrow keys, and the focus ring.
 */

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const MODES = ['light', 'dark'] as const;
type Mode = (typeof MODES)[number];

async function violations(page: Page, include?: string): Promise<string[]> {
  const builder = new AxeBuilder({ page }).withTags(WCAG);
  const results = await (include ? builder.include(include) : builder).analyze();
  return results.violations.map(
    (violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
}

/** One specimen's panel in one mode: `#dialog .panel[data-mode="dark"]`. */
const panel = (page: Page, specimen: string, mode: Mode): Locator =>
  page.locator(`#${specimen} .panel[data-mode="${mode}"]`);

/** A token as the browser resolves it inside an element, read through a probe so every value compares as a color. */
async function tokenColor(element: Locator, token: string): Promise<string> {
  return element.evaluate((host, name) => {
    const probe = host.ownerDocument.createElement('span');
    probe.style.color = `var(${name})`;
    host.append(probe);
    const color = getComputedStyle(probe).color;
    probe.remove();
    return color;
  }, token);
}

async function choosePageMode(page: Page, label: 'Follow this device' | 'Light' | 'Dark') {
  await page.locator('.page-mode').getByRole('radio', { name: label, exact: true }).check();
}

test.beforeEach(async ({ page }) => {
  await page.goto('/showcase');
  await expect(page.getByRole('heading', { level: 1, name: 'Component showcase' })).toBeVisible();
});

test.describe('axe, contrast included', () => {
  for (const mode of MODES) {
    test(`passes over the whole page with the page in ${mode} mode`, async ({ page }) => {
      await choosePageMode(page, mode === 'light' ? 'Light' : 'Dark');
      await expect(page.locator('html')).toHaveAttribute('data-mode', mode);
      expect(await violations(page)).toEqual([]);
    });

    test(`passes with the page following a device set to ${mode}`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: mode });
      await expect(page.locator('html')).not.toHaveAttribute('data-mode');
      expect(await violations(page)).toEqual([]);
    });
  }

  for (const mode of MODES) {
    test(`passes over every dialog and the drawer, open in the ${mode} panel`, async ({ page }) => {
      const host = panel(page, 'dialog', mode);
      for (const [opener, heading] of [
        ['Rename post', 'Rename post'],
        ['Share a preview', 'Share a preview'],
        ['Move to the bin', 'Move Apple pie to the bin?'],
        ['Compare revisions', 'Compare revisions'],
        ['Post settings', 'Post settings'],
      ]) {
        await host.getByRole('button', { name: opener, exact: true }).click();
        const dialog = page.getByRole(heading.startsWith('Move') ? 'alertdialog' : 'dialog', {
          name: heading,
        });
        await expect(dialog).toBeVisible();
        expect(await violations(page, `#dialog .panel[data-mode="${mode}"] dialog[open]`)).toEqual(
          [],
        );
        await page.keyboard.press('Escape');
        await expect(dialog).toBeHidden();
      }
    });
  }

  for (const mode of MODES) {
    test(`passes over every toast, with the page in ${mode} mode`, async ({ page }) => {
      await choosePageMode(page, mode === 'light' ? 'Light' : 'Dark');
      const host = panel(page, 'toast', mode);
      for (const name of ['Show a warning', 'Show an error', 'Show one with an action']) {
        await host.getByRole('button', { name }).click();
      }
      // The app has one toast region, whichever panel asked (PP3.9's shell will hold it).
      await expect(page.locator('pp-toast-region')).toHaveCount(1);
      const region = page.getByRole('region', { name: 'Notifications' });
      await expect(region.getByRole('listitem')).toHaveCount(3);
      expect(await violations(page, 'pp-toast-region')).toEqual([]);
    });
  }

  for (const mode of MODES) {
    test(`passes over the list's other states and the uploader's refusals, in the ${mode} panel`, async ({
      page,
    }) => {
      const list = panel(page, 'list', mode);
      const recipes = list.getByRole('region', { name: 'Recipes' });

      await recipes.getByRole('button', { name: 'Next' }).click();
      await expect(recipes.locator('.items')).toHaveAttribute('aria-busy', 'true');
      expect(await violations(page, `#list .panel[data-mode="${mode}"]`)).toEqual([]);
      await expect(recipes.getByText('Page 2')).toBeVisible();
      await expect(recipes.locator('.items')).not.toHaveAttribute('aria-busy');

      // Searching for nothing that exists offers the one way back.
      const search = recipes.getByRole('searchbox', { name: 'Search recipes' });
      await search.fill('zzz');
      await search.press('Enter');
      await expect(recipes.getByRole('heading', { name: 'No recipes match' })).toBeVisible();
      expect(await violations(page, `#list .panel[data-mode="${mode}"]`)).toEqual([]);
      await recipes.getByRole('button', { name: 'Show every recipe' }).click();
      await expect(recipes.getByText('Apple pie')).toBeVisible();
      await expect(search).toHaveValue('');

      // The empty and error states have nothing to page through.
      const pager = recipes.getByRole('navigation', { name: 'Recipes pages' });
      await expect(pager).toHaveCount(1);
      await list.getByRole('radio', { name: 'Empty' }).check();
      await expect(recipes.getByRole('heading', { name: 'No recipes yet' })).toBeVisible();
      await expect(pager).toHaveCount(0);
      expect(await violations(page, `#list .panel[data-mode="${mode}"]`)).toEqual([]);
      await list.getByRole('radio', { name: "Couldn't load" }).check();
      await expect(recipes.getByRole('alert')).toBeVisible();
      await expect(pager).toHaveCount(0);
      expect(await violations(page, `#list .panel[data-mode="${mode}"]`)).toEqual([]);

      const uploader = panel(page, 'uploader', mode);
      await uploader
        .locator('input[type="file"]')
        .first()
        .setInputFiles([
          { name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('not a photo') },
          { name: 'huge.png', mimeType: 'image/png', buffer: Buffer.alloc(1_048_577) },
          { name: 'pie.png', mimeType: 'image/png', buffer: Buffer.from('a photo') },
        ]);
      await expect(uploader.getByText('notes.txt:')).toBeVisible();
      await expect(uploader.getByText('huge.png:')).toBeVisible();
      await expect(uploader.getByText('pie.png')).toBeVisible();
      expect(await violations(page, `#uploader .panel[data-mode="${mode}"]`)).toEqual([]);
    });
  }
});

test.describe('modes as drawn', () => {
  const cases = [
    { page: 'Light', device: 'light', expected: 'light' },
    { page: 'Dark', device: 'light', expected: 'dark' },
    { page: 'Light', device: 'dark', expected: 'light' },
    { page: 'Follow this device', device: 'light', expected: 'light' },
    { page: 'Follow this device', device: 'dark', expected: 'dark' },
  ] as const;

  for (const { page: pageMode, device, expected } of cases) {
    test(`page "${pageMode}" on a ${device} device draws the page ${expected}, and each panel its own`, async ({
      page,
    }) => {
      await page.emulateMedia({ colorScheme: device });
      await choosePageMode(page, pageMode);
      const html = page.locator('html');
      const light = panel(page, 'button', 'light');
      const dark = panel(page, 'button', 'dark');

      const lightBg = await tokenColor(html, '--L-bg');
      const darkBg = await tokenColor(html, '--D-bg');
      expect(lightBg).not.toEqual(darkBg);
      expect(await tokenColor(html, '--bg')).toEqual(expected === 'light' ? lightBg : darkBg);
      expect(await tokenColor(light, '--bg')).toEqual(lightBg);
      expect(await tokenColor(dark, '--bg')).toEqual(darkBg);
      await expect(light).toHaveCSS('background-color', lightBg);
      await expect(dark).toHaveCSS('background-color', darkBg);
      await expect(light).toHaveCSS('color-scheme', 'light');
      await expect(dark).toHaveCSS('color-scheme', 'dark');
      await expect(page.locator('body')).toHaveCSS(
        'background-color',
        expected === 'light' ? lightBg : darkBg,
      );
    });
  }
});

test.describe('the native modal dialog', () => {
  for (const mode of MODES) {
    test(`in the ${mode} panel: holds focus, closes on Escape, gives focus back, and dims with --overlay`, async ({
      page,
    }) => {
      const host = panel(page, 'dialog', mode);
      const opener = host.getByRole('button', { name: 'Rename post' });
      // Opened from the keyboard, the way focus return matters. WebKit never focuses a button on a click, so a dialog
      // opened by mouse there has no opener to go back to (recorded in PP3.6's delivery note).
      await opener.focus();
      await page.keyboard.press('Enter');
      const dialog = host.locator('dialog[open]');
      await expect(dialog).toBeVisible();
      await expect(dialog.getByRole('textbox', { name: 'Post title' })).toBeFocused();

      // The page behind is inert: Tab and Shift+Tab never leave the dialog.
      for (const key of ['Tab', 'Tab', 'Tab', 'Tab', 'Shift+Tab', 'Shift+Tab', 'Shift+Tab']) {
        await page.keyboard.press(key);
        const inside = await dialog.evaluate((element) =>
          element.contains(element.ownerDocument.activeElement),
        );
        const onPage = await page.evaluate(() => document.activeElement === document.body);
        expect(inside || onPage, `focus after ${key}`).toBe(true);
      }
      // Even a script cannot focus a control behind it.
      const behind = page.locator('.page-mode input').first();
      await behind.evaluate((element: HTMLElement) => element.focus());
      await expect(behind).not.toBeFocused();

      const backdrop = await dialog.evaluate(
        (element) => getComputedStyle(element, '::backdrop').backgroundColor,
      );
      expect(backdrop).toEqual(await tokenColor(dialog, '--overlay'));

      await page.keyboard.press('Escape');
      await expect(dialog).toHaveCount(0);
      await expect(opener).toBeFocused();
    });
  }

  test('an alert dialog starts on its least destructive action and ignores the backdrop', async ({
    page,
  }) => {
    const host = panel(page, 'dialog', 'light');
    await host.getByRole('button', { name: 'Move to the bin' }).click();
    const alert = page.getByRole('alertdialog', { name: 'Move Apple pie to the bin?' });
    await expect(alert.getByRole('button', { name: 'Keep it' })).toBeFocused();
    await page.mouse.click(4, 4);
    await expect(alert).toBeVisible();
    await alert.getByRole('button', { name: 'Keep it' }).click();
    await expect(alert).toBeHidden();
  });

  test('a dismissible dialog closes on a press that starts on the backdrop', async ({ page }) => {
    const host = panel(page, 'dialog', 'dark');
    await host.getByRole('button', { name: 'Share a preview' }).click();
    const dialog = page.getByRole('dialog', { name: 'Share a preview' });
    await expect(dialog).toBeVisible();
    await page.mouse.click(4, 4);
    await expect(dialog).toBeHidden();
  });
});

test.describe('the keyboard', () => {
  for (const mode of MODES) {
    test(`dismissing the tip and bringing it back never drops focus, in the ${mode} panel`, async ({
      page,
    }) => {
      const host = panel(page, 'notice', mode);
      await host.getByRole('button', { name: /Dismiss/ }).click();
      const again = host.getByRole('button', { name: 'Show the tip again' });
      await expect(again).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(host.getByRole('button', { name: /Dismiss/ })).toBeFocused();
    });

    test(`arrow keys move between radios in the ${mode} panel`, async ({ page }) => {
      const group = panel(page, 'radio-group', mode).getByRole('radiogroup', {
        name: /Newsletter/,
      });
      await group.getByRole('radio', { name: 'Weekly' }).focus();
      await page.keyboard.press('ArrowDown');
      await expect(group.getByRole('radio', { name: 'Monthly' })).toBeFocused();
      await expect(group.getByRole('radio', { name: 'Monthly' })).toBeChecked();
      await page.keyboard.press('ArrowUp');
      await expect(group.getByRole('radio', { name: 'Weekly' })).toBeChecked();
    });

    test(`the focus ring is an offset outline in --focus, in the ${mode} panel`, async ({
      page,
    }) => {
      const host = panel(page, 'button', mode);
      const publish = host.getByRole('button', { name: 'Publish', exact: true }).first();
      const before = host.getByRole('button', { name: 'Save draft', exact: true }).first();
      // Reached by the keyboard, so :focus-visible applies in every engine.
      await publish.focus();
      await page.keyboard.press('Tab');
      await expect(before).toBeFocused();
      await page.keyboard.press('Shift+Tab');
      await expect(publish).toBeFocused();

      await expect(publish).not.toHaveCSS('outline-style', 'none');
      await expect(publish).toHaveCSS('outline-color', await tokenColor(host, '--focus'));
      const offset = await publish.evaluate((element) =>
        parseFloat(getComputedStyle(element).outlineOffset),
      );
      expect(offset).toBeGreaterThan(0);
    });
  }
});
