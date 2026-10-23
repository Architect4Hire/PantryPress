import { PANTRYPRESS_UI_ENTRY_POINT } from '@pantrypress/ui';
import { PANTRYPRESS_THEMES_ENTRY_POINT } from '@pantrypress/ui/themes';

describe('@pantrypress/ui entry points', () => {
  it('resolves the admin and themes entry points by package name', () => {
    expect(PANTRYPRESS_UI_ENTRY_POINT).toBe('@pantrypress/ui');
    expect(PANTRYPRESS_THEMES_ENTRY_POINT).toBe('@pantrypress/ui/themes');
  });
});
