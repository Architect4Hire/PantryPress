import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { axeViolations } from '../../testing/axe';
import { PpEmptyState } from '../empty-state/empty-state';
import { PpSearchField } from '../search-field/search-field';
import { PpToastRegion } from '../toast/toast-region';
import { PpList, PpListStatus, PpPageTurn, PpSortOption } from './list';

type Sort = 'newest' | 'oldest' | 'title';

@Component({
  imports: [PpList, PpEmptyState, PpSearchField, PpToastRegion],
  template: `
    <pp-list
      label="Posts"
      [status]="status()"
      [empty]="empty()"
      [resultsText]="resultsText()"
      [sortOptions]="sorts"
      [(sort)]="sort"
      [page]="page()"
      [hasPrevious]="page() > 1"
      [hasNext]="hasNext()"
      (pageChange)="turns.push($event)"
      (retry)="retries = retries + 1"
    >
      <pp-search-field ppListSearch label="Search posts" />
      <ul class="rows">
        <li>Apple pie</li>
        <li><button type="button">Lemon tart</button></li>
      </ul>
      <pp-empty-state ppListEmpty heading="No posts yet" />
    </pp-list>
    <pp-list id="bare" label="Members" />
    <pp-toast-region />
  `,
})
class Host {
  readonly sorts: PpSortOption<Sort>[] = [
    { value: 'newest', label: 'Newest first' },
    { value: 'oldest', label: 'Oldest first' },
    { value: 'title', label: 'Title' },
  ];
  readonly status = signal<PpListStatus>('loading');
  readonly empty = signal(false);
  readonly resultsText = signal<string | undefined>(undefined);
  readonly sort = signal<Sort | undefined>(undefined);
  readonly page = signal(1);
  readonly hasNext = signal(true);
  readonly turns: PpPageTurn[] = [];
  retries = 0;
}

describe('PpList', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const list = root.querySelector<HTMLElement>('pp-list')!;
    const shown = (selector: string) => {
      const element = list.querySelector<HTMLElement>(selector);
      return !!element && !element.closest('[hidden]');
    };
    const button = (name: string) =>
      [...list.querySelectorAll<HTMLButtonElement>('button')].find(
        (candidate) => candidate.textContent!.trim() === name,
      );
    const spoken = () => [...root.querySelectorAll('pp-announcer p')].map((p) => p.textContent);
    const set = async (change: () => void) => {
      change();
      await fixture.whenStable();
    };
    return { fixture, root, list, shown, button, spoken, set, host: fixture.componentInstance };
  }

  it('is a region named by its label, holding the search, sort, rows, and pager', async () => {
    const { list, shown, set, host } = await render();
    expect(list.getAttribute('role')).toBe('region');
    expect(list.getAttribute('aria-label')).toBe('Posts');
    expect(list.getAttribute('tabindex')).toBe('-1');
    await set(() => host.status.set('ready'));
    expect(shown('pp-search-field')).toBe(true);
    expect(shown('select')).toBe(true);
    expect(shown('.rows')).toBe(true);
    expect(list.querySelector('nav')!.getAttribute('aria-label')).toBe('Posts pages');
  });

  it('shows progress on the first load, and keeps the rows in view, marked busy, on later ones', async () => {
    const { list, shown, button, set, host } = await render();
    expect(shown('pp-progress')).toBe(true);
    expect(list.querySelector('pp-progress')!.textContent!.trim()).toBe('Loading posts');
    expect(shown('.rows')).toBe(false);
    expect(shown('pp-empty-state')).toBe(false);
    await set(() => host.status.set('ready'));
    expect(shown('pp-progress')).toBe(false);
    expect(list.querySelector('.items')!.hasAttribute('aria-busy')).toBe(false);
    await set(() => host.status.set('loading'));
    expect(shown('pp-progress')).toBe(false);
    expect(shown('.rows')).toBe(true);
    expect(list.querySelector('.items')!.getAttribute('aria-busy')).toBe('true');
    // A load the pager did not start (a search, a sort) shows on neither of its buttons.
    expect(button('Next')!.hasAttribute('aria-busy')).toBe(false);
  });

  it('shows the empty state instead of the rows when the answer has nothing in it', async () => {
    const { shown, set, host } = await render();
    await set(() => {
      host.empty.set(true);
      host.status.set('ready');
    });
    expect(shown('pp-empty-state')).toBe(true);
    expect(shown('.rows')).toBe(false);
  });

  it('shows the error as an alert with Try again, in place of the rows', async () => {
    const { list, shown, button, set, host } = await render();
    await set(() => host.status.set('ready'));
    await set(() => host.status.set('error'));
    const notice = list.querySelector('pp-notice')!;
    expect(notice.getAttribute('role')).toBe('alert');
    expect(notice.textContent).toContain("This list couldn't be loaded.");
    expect(shown('.rows')).toBe(false);
    expect(shown('pp-empty-state')).toBe(false);
    button('Try again')!.focus();
    button('Try again')!.click();
    expect(host.retries).toBe(1);
    expect(document.activeElement).toBe(list);
  });

  it('sorts by typed options, showing the first when none is chosen', async () => {
    const { fixture, list, set, host } = await render();
    const select = list.querySelector<HTMLSelectElement>('select')!;
    const label = list.querySelector(`label[for="${select.id}"]`)!;
    expect(label.textContent!.trim()).toBe('Sort by');
    expect([...select.options].map((option) => option.textContent!.trim())).toEqual([
      'Newest first',
      'Oldest first',
      'Title',
    ]);
    expect(select.selectedIndex).toBe(0);
    select.value = '2';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(host.sort()).toBe('title');
    await set(() => host.sort.set('oldest'));
    expect(select.selectedIndex).toBe(1);
  });

  it('pages back and forth, saying the page, and moves focus to the top of the list', async () => {
    const { list, button, set, host } = await render();
    await set(() => host.status.set('ready'));
    expect(list.querySelector('.page')!.textContent!.trim()).toBe('Page 1');
    expect(button('Previous')!.disabled).toBe(true);
    button('Next')!.focus();
    button('Next')!.click();
    expect(host.turns).toEqual(['next']);
    expect(document.activeElement).toBe(list.querySelector('.items'));
    await set(() => host.status.set('loading'));
    expect(button('Next')!.getAttribute('aria-busy')).toBe('true');
    await set(() => {
      host.status.set('ready');
      host.page.set(2);
      host.hasNext.set(false);
    });
    expect(list.querySelector('.page')!.textContent!.trim()).toBe('Page 2');
    expect(button('Next')!.disabled).toBe(true);
    button('Previous')!.click();
    expect(host.turns).toEqual(['next', 'previous']);
  });

  it('says the results each time new ones arrive, but not on the first load', async () => {
    const { spoken, set, host } = await render();
    await set(() => {
      host.resultsText.set('12 posts');
      host.status.set('ready');
    });
    expect(spoken()).toEqual([]);
    await set(() => host.status.set('loading'));
    await set(() => {
      host.resultsText.set('Page 2 of posts');
      host.status.set('ready');
    });
    expect(spoken()).toEqual(['Page 2 of posts']);
  });

  it('says the results that follow a failed first load once Try again brings them', async () => {
    const { spoken, set, host } = await render();
    await set(() => host.status.set('error'));
    await set(() => host.status.set('loading'));
    await set(() => {
      host.resultsText.set('12 posts');
      host.status.set('ready');
    });
    expect(spoken()).toEqual(['12 posts']);
  });

  it('draws no controls row or pager when the page gives none', async () => {
    const { root } = await render();
    const bare = root.querySelector('#bare')!;
    expect(bare.querySelector('select')).toBeNull();
    expect(bare.querySelector('nav')).toBeNull();
    expect(bare.querySelector('.search')!.childElementCount).toBe(0);
  });

  it('has no accessibility violations, loading, ready, busy, empty, and in error', async () => {
    const { root, set, host } = await render();
    expect(await axeViolations(root)).toEqual([]);
    await set(() => host.status.set('ready'));
    expect(await axeViolations(root)).toEqual([]);
    await set(() => host.status.set('loading'));
    expect(await axeViolations(root)).toEqual([]);
    await set(() => {
      host.empty.set(true);
      host.status.set('ready');
    });
    expect(await axeViolations(root)).toEqual([]);
    await set(() => host.status.set('error'));
    expect(await axeViolations(root)).toEqual([]);
  });

  it('pages to the list itself when there are no rows to start from', async () => {
    const { list, button, set, host } = await render();
    await set(() => {
      host.empty.set(true);
      host.status.set('ready');
    });
    button('Next')!.click();
    expect(document.activeElement).toBe(list);
  });

  it('keeps focus on the list when the rows holding it are hidden', async () => {
    const { list, set, host } = await render();
    await set(() => host.status.set('ready'));
    list.querySelector<HTMLButtonElement>('.rows button')!.focus();
    await set(() => host.status.set('error'));
    expect(document.activeElement).toBe(list);
  });
});
