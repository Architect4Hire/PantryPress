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
import { PpDropdown, PpDropdownItem } from './dropdown/dropdown';
import { PpEmptyState } from './empty-state/empty-state';
import { PpNotice } from './notice/notice';
import { PpPageHeader } from './page-header/page-header';
import { PpProgress } from './progress/progress';
import { PpStatusPill } from './status-pill/status-pill';
import { PpTab, PpTabs } from './tabs/tabs';
import { PpToastRegion } from './toast/toast-region';
import { PpChipGroup, PpChipOption } from './chip-group/chip-group';
import { PpList, PpSortOption } from './list/list';
import { PpSearchField } from './search-field/search-field';
import { PpUploader, PpUploadItem } from './uploader/uploader';

// The platform's public pages prerender with these primitives (PP-D19). renderApplication puts the server's DOM
// classes on the global object, which breaks jsdom specs that run after it, so every server render lives in this
// file alone. That holds only because the library's tests run isolated, one file to a worker: `isolate` in
// angular.json, which the Angular builder leaves off by default (PP3.5 found specs failing after this one without it).

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
    PpDropdown,
    PpDropdownItem,
    PpEmptyState,
    PpNotice,
    PpPageHeader,
    PpProgress,
    PpStatusPill,
    PpTabs,
    PpTab,
    PpToastRegion,
    PpList,
    PpSearchField,
    PpChipGroup,
    PpUploader,
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
    <pp-dropdown prefix="Site" label="Apple Pie Club" caption="Your sites">
      <a pp-dropdown-item href="/dashboard" aria-current="page">Apple Pie Club</a>
      <button pp-dropdown-item>Sign out</button>
    </pp-dropdown>
    <pp-list label="Posts" [sortOptions]="sorts" sort="title" [page]="2" [hasPrevious]="true">
      <pp-search-field ppListSearch label="Search posts" query="pie" />
      <pp-chip-group ppListFilters legend="Status" [options]="statuses" />
      <ul>
        <li>Apple pie</li>
      </ul>
    </pp-list>
    <pp-uploader label="Choose photos" accept="image/png" [items]="uploads" />
    <pp-toast-region />
  `,
})
class TestPage {
  readonly modes: PpRadioOption<string>[] = [
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
  ];
  readonly sorts: PpSortOption<string>[] = [
    { value: 'newest', label: 'Newest first' },
    { value: 'title', label: 'Title' },
  ];
  readonly statuses: PpChipOption<string>[] = [
    { value: 'draft', label: 'Drafts' },
    { value: 'published', label: 'Published' },
  ];
  readonly uploads: PpUploadItem[] = [
    { id: 'a', name: 'pie.jpg', status: 'uploading', progress: 60 },
    { id: 'b', name: 'tart.jpg', status: 'failed', error: 'The connection dropped.' },
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

  it('render a dropdown closed, its items out of reach until it opens', async () => {
    const html = await render();
    const dropdown = /<pp-dropdown[\s\S]*?<\/pp-dropdown>/.exec(html)![0];
    expect(dropdown).toMatch(/<button[^>]*aria-expanded="false"/);
    expect(dropdown).toMatch(/<div[^>]*class="popup"[^>]*hidden/);
    expect(dropdown).toContain('Your sites');
  });

  it('render every data primitive in the state it was given', async () => {
    const html = await render();
    const list = /<pp-list[\s\S]*?<\/pp-list>/.exec(html)![0];
    expect(list).toMatch(/<pp-list[^>]*role="region"[^>]*aria-label="Posts"/);
    expect(list).toMatch(/<option[^>]*value="1"[^>]*selected/);
    expect(list).toContain('Page 2');
    expect(list).toMatch(
      /<input[^>]*type="search"[^>]*value="pie"|<input[^>]*value="pie"[^>]*type="search"/,
    );
    expect(list).toMatch(/<input[^>]*type="checkbox"/);
    expect(list).toContain('Apple pie');
    expect(html).toMatch(/<input[^>]*type="file"[^>]*hidden|<input[^>]*hidden[^>]*type="file"/);
    expect(html).toMatch(/<progress[^>]*value="60"/);
    expect(html).toContain('The connection dropped.');
  });
});
