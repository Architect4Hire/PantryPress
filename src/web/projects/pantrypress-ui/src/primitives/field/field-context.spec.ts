import { Component } from '@angular/core';
import { bootstrapApplication, BootstrapContext } from '@angular/platform-browser';
import { renderApplication } from '@angular/platform-server';
import { provideServerRendering } from '@angular/ssr';
import { TestBed } from '@angular/core/testing';
import { PpButton } from '../button/button';
import { PpCheckbox } from '../checkbox/checkbox';
import { PpRadioGroup, PpRadioOption } from '../radio-group/radio-group';
import { PpSwitch } from '../switch/switch';
import { PpTextField } from '../text-field/text-field';
import { PpField } from './field';
import { PpIdSequence } from './field-context';

@Component({
  selector: 'pp-test-page',
  imports: [PpField, PpTextField, PpCheckbox, PpSwitch, PpRadioGroup, PpButton],
  template: `
    <pp-field label="Email" hint="We never share it."
      ><input pp-text-field type="email"
    /></pp-field>
    <pp-checkbox label="Keep me signed in" hint="On this device only." [checked]="true" />
    <pp-switch label="Comments" hint="Readers can reply." [checked]="true" />
    <pp-radio-group legend="Mode" [options]="modes" />
    <button pp-button variant="primary" busy>Sign in</button>
  `,
})
class TestPage {
  readonly modes: PpRadioOption<string>[] = [
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
  ];
}

describe('PpIdSequence', () => {
  it('starts again for every application', () => {
    const first = [
      TestBed.inject(PpIdSequence).next('field'),
      TestBed.inject(PpIdSequence).next('field'),
    ];
    TestBed.resetTestingModule();
    const second = TestBed.inject(PpIdSequence).next('field');
    expect(first).toEqual(['pp-field-1', 'pp-field-2']);
    expect(second).toBe('pp-field-1');
  });
});

describe('the primitives on the server', () => {
  const render = () =>
    renderApplication(
      (context: BootstrapContext) =>
        bootstrapApplication(TestPage, { providers: [provideServerRendering()] }, context),
      {
        document:
          '<!doctype html><html><head></head><body><pp-test-page></pp-test-page></body></html>',
      },
    );

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
});
