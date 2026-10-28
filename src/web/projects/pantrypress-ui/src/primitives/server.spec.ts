import { Component } from '@angular/core';
import { bootstrapApplication, BootstrapContext } from '@angular/platform-browser';
import { renderApplication } from '@angular/platform-server';
import { provideServerRendering } from '@angular/ssr';
import { PpButton } from './button/button';
import { PpCheckbox } from './checkbox/checkbox';
import { PpField } from './field/field';
import { PpRadioGroup, PpRadioOption } from './radio-group/radio-group';
import { PpSwitch } from './switch/switch';
import { PpTextField } from './text-field/text-field';
import { PpToolbar } from './toolbar/toolbar';
import { PpCard } from './card/card';
import { PpDialog } from './dialog/dialog';
import { PpDrawer } from './drawer/drawer';
import { PpEmptyState } from './empty-state/empty-state';
import { PpNotice } from './notice/notice';
import { PpPageHeader } from './page-header/page-header';
import { PpProgress } from './progress/progress';
import { PpStatusPill } from './status-pill/status-pill';
import { PpTab, PpTabs } from './tabs/tabs';
import { PpToastRegion } from './toast/toast-region';

// The platform's public pages prerender with these primitives (PP-D19). renderApplication puts the server's DOM
// classes on the global object, which breaks jsdom specs that run after it, so every server render lives in this
// file alone (Vitest isolates files).

@Component({
  selector: 'pp-test-page',
  imports: [
    PpField,
    PpTextField,
    PpCheckbox,
    PpSwitch,
    PpRadioGroup,
    PpButton,
    PpToolbar,
    PpCard,
    PpDialog,
    PpDrawer,
    PpEmptyState,
    PpNotice,
    PpPageHeader,
    PpProgress,
    PpStatusPill,
    PpTabs,
    PpTab,
    PpToastRegion,
  ],
  template: `
    <pp-field label="Email" hint="We never share it."
      ><input pp-text-field type="email"
    /></pp-field>
    <pp-checkbox label="Keep me signed in" hint="On this device only." [checked]="true" />
    <pp-switch label="Comments" hint="Readers can reply." [checked]="true" />
    <pp-radio-group legend="Mode" [options]="modes" />
    <button pp-button variant="primary" busy>Sign in</button>
    <pp-toolbar label="Formatting">
      <button pp-button size="sm">Bold</button>
      <button pp-button size="sm">Italic</button>
    </pp-toolbar>
    <pp-page-header heading="Pricing" description="Plans for every blog." />
    <pp-card
      ><h2>Plus</h2>
      <pp-status-pill tone="success">Popular</pp-status-pill></pp-card
    >
    <pp-notice tone="info" heading="Coming soon" announce>Sign-up opens soon.</pp-notice>
    <pp-empty-state heading="No posts yet" />
    <pp-progress label="Uploading" [value]="40" showValue />
    <pp-tabs>
      <pp-tab key="one" label="One"><p>One</p></pp-tab>
      <pp-tab key="two" label="Two"><p>Two</p></pp-tab>
    </pp-tabs>
    <pp-dialog heading="Closed dialog"><p>Not shown.</p></pp-dialog>
    <pp-drawer heading="Closed drawer"><p>Not shown.</p></pp-drawer>
    <pp-toast-region />
  `,
})
class TestPage {
  readonly modes: PpRadioOption<string>[] = [
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
  ];
}

const render = () =>
  renderApplication(
    (context: BootstrapContext) =>
      bootstrapApplication(TestPage, { providers: [provideServerRendering()] }, context),
    {
      document:
        '<!doctype html><html><head></head><body><pp-test-page></pp-test-page></body></html>',
    },
  );

describe('the primitives on the server', () => {
  it('render with no browser, in the state they were given', async () => {
    const html = await render();
    expect(html).toContain('for="pp-field-1"');
    expect(html).toMatch(/<input[^>]*id="pp-field-1"/);
    expect(html).toMatch(/<input[^>]*class="box"[^>]*checked/);
    expect(html).toMatch(/<input[^>]*role="switch"[^>]*checked/);
    expect(html).toMatch(/<button[^>]*aria-busy="true"/);
    expect(html).toMatch(/<button[^>]*type="button"/);
  });

  it('render the same ids on every render, as on every build', async () => {
    const ids = (html: string) =>
      [...html.matchAll(/\bid="(pp-[a-z]+-\d+[^"]*)"/g)].map((match) => match[1]);
    const [first, second] = [await render(), await render()];
    expect(ids(first).length).toBeGreaterThan(0);
    expect(ids(second)).toEqual(ids(first));
  });

  it('render every layout and feedback primitive, with dialogs closed and no toast', async () => {
    const html = await render();
    expect(html).toMatch(/<h1[^>]*class="heading"[^>]*>Pricing<\/h1>/);
    expect(html).toContain('role="tablist"');
    expect(html).toMatch(/<pp-tab[^>]*role="tabpanel"[^>]*hidden/);
    expect(html).toMatch(/<progress[^>]*value="40"/);
    expect(html).toContain('aria-label="Notifications"');
    expect(html).not.toContain('li pp-toast');
    expect(html).toMatch(/<dialog[^>]*class="dialog"/);
    expect(html).not.toMatch(/<dialog[^>]*\sopen/);
  });

  it('leave every toolbar control reachable, since tab stops are set only in the browser', async () => {
    const html = await render();
    const toolbar = /<pp-toolbar[\s\S]*?<\/pp-toolbar>/.exec(html)![0];
    expect(toolbar).toContain('role="toolbar"');
    expect(toolbar).not.toContain('tabindex');
  });
});
