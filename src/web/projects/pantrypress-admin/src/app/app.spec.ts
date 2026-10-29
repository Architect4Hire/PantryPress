import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  // Each page owns its one <main> and its one h1 (PPL.2), so the shell adds neither.
  it('is the router outlet and nothing else', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('main')).toBeNull();
    expect(compiled.querySelector('h1')).toBeNull();
    expect(compiled.querySelector('router-outlet')).not.toBeNull();
  });
});
