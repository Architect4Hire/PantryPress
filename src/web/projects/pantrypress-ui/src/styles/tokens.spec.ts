import { readdirSync, readFileSync } from 'node:fs';
import { contrastRatio } from './contrast';

// Tests run from src/web (the workspace root).
const css = readFileSync('projects/pantrypress-ui/src/styles/tokens.css', 'utf8').replace(
  /\/\*[\s\S]*?\*\//g,
  '',
);

/** The declarations of the first rule whose selector list is exactly `selector`, inside `within` if given. */
function block(selector: string, within = css): Map<string, string> {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/\s+/g, '\\s*');
  const match = new RegExp(`(?:^|[}\\s])${escaped}\\s*\\{([^{}]*)\\}`).exec(within);
  if (!match) {
    throw new Error(`No rule for ${selector}`);
  }
  const declarations = new Map<string, string>();
  for (const part of match[1].split(';')) {
    const colon = part.indexOf(':');
    if (colon > 0) {
      declarations.set(part.slice(0, colon).trim(), part.slice(colon + 1).trim());
    }
  }
  return declarations;
}

function media(query: string): string {
  const start = css.indexOf(`@media (${query})`);
  if (start < 0) {
    throw new Error(`No @media (${query})`);
  }
  const open = css.indexOf('{', start);
  let depth = 0;
  for (let i = open; i < css.length; i++) {
    if (css[i] === '{') depth++;
    if (css[i] === '}' && --depth === 0) return css.slice(open + 1, i);
  }
  throw new Error(`Unclosed @media (${query})`);
}

const root = block(':root');
const light = block(":root, [data-mode='light'], [data-mode='auto']");
const dark = block("[data-mode='dark']");
const auto = block(
  ":root:not([data-mode]), [data-mode='auto']",
  media('prefers-color-scheme: dark'),
);

const roles = [...root.keys()]
  .filter((name) => name.startsWith('--L-'))
  .map((name) => name.slice(4));
const pairValue = (mode: 'L' | 'D', role: string): string => root.get(`--${mode}-${role}`) ?? '';

/** Roles drawn with transparency (scrims, shadows); they sit under or around content, never behind text. */
const translucent = ['overlay', 'shadow'];
/** Roles no contrast pair measures, and why: decoration has no floor (WCAG 1.4.11), and translucent roles sit behind. */
const unmeasured = ['line-subtle', ...translucent];

// A literal color anywhere: hex or a color function. In a style sheet, where copy cannot trip it, a named color too.
const literalColor =
  /#[0-9a-f]{3,8}\b|\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color|color-mix|light-dark)\(/i;
const namedColors = [
  'aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood',
  'cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray',
  'darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen',
  'darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue',
  'firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew',
  'hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan',
  'lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue',
  'lightslategray lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine',
  'mediumblue mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise',
  'mediumvioletred midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab orange',
  'orangered orchid palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum',
  'powderblue purple rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown seagreen seashell sienna',
  'silver skyblue slateblue slategray slategrey snow springgreen steelblue tan teal thistle tomato turquoise violet',
  'wheat white whitesmoke yellow yellowgreen canvas canvastext linktext visitedtext activetext buttonface',
  'buttontext buttonborder field fieldtext highlight highlighttext selecteditem selecteditemtext mark marktext',
  'graytext accentcolor accentcolortext',
]
  .join(' ')
  .split(' ');
const namedColor = new RegExp(`(?<![\\w-])(?:${namedColors.join('|')})(?![\\w-])`, 'i');

/** The values of a style sheet's declarations, so a selector such as `.field` or `mark` is never read as a color. */
function declarationValues(sheet: string): string[] {
  return [...sheet.matchAll(/:\s*([^;{}]+)[;}]/g)].map((match) => match[1]);
}

const namesAColor = (sheet: string): boolean =>
  declarationValues(sheet).some((value) => namedColor.test(value));

/** Every style source of the platform home's design system and app, but the token file and the measuring helper. */
function styleSources(): string[] {
  const roots = ['projects/pantrypress-ui', 'projects/pantrypress-admin/src'];
  const exempt = [
    'projects/pantrypress-ui/src/styles/tokens.css',
    'projects/pantrypress-ui/src/styles/contrast.ts',
  ];
  return roots
    .flatMap((dir) =>
      readdirSync(dir, { recursive: true, encoding: 'utf8' }).map((file) =>
        `${dir}/${file}`.replace(/\\/g, '/'),
      ),
    )
    .filter((file) => /\.(?:css|scss|html|ts)$/.test(file) && !file.endsWith('.spec.ts'))
    .filter((file) => !exempt.includes(file));
}

const surfaces = ['bg', 'surface'];
const textOnSurfaces = [
  'ink',
  'muted',
  'accent-text',
  'danger-text',
  'warning-text',
  'success-text',
  'info-text',
];
// The focus ring is drawn outside its element with an offset, so it sits on a surface, never on the control's fill.
const uiOnSurfaces = ['line', 'focus', 'accent'];
const inkOnFills: [string, string][] = [
  ['accent-ink', 'accent'],
  ['accent-ink', 'accent-hover'],
  ['accent-ink', 'accent-active'],
  ['hl-ink', 'hl'],
  ['danger-ink', 'danger'],
  ['warning-ink', 'warning'],
  ['success-ink', 'success'],
  ['info-ink', 'info'],
];

const pairs: { fg: string; bg: string; min: number }[] = [
  ...surfaces.flatMap((bg) => textOnSurfaces.map((fg) => ({ fg, bg, min: 4.5 }))),
  ...surfaces.flatMap((bg) => uiOnSurfaces.map((fg) => ({ fg, bg, min: 3 }))),
  ...inkOnFills.map(([fg, bg]) => ({ fg, bg, min: 4.5 })),
];

describe('contrastRatio', () => {
  it('matches the WCAG reference values', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrastRatio('#fff', '#fff')).toBeCloseTo(1, 5);
    expect(contrastRatio('#777777', '#ffffff')).toBeCloseTo(4.48, 2);
    expect(contrastRatio('#ffffff', '#777777')).toBeCloseTo(
      contrastRatio('#777777', '#ffffff'),
      10,
    );
  });

  it('refuses a color it cannot measure', () => {
    expect(() => contrastRatio('rgb(0 0 0)', '#fff')).toThrow();
    expect(() => contrastRatio('#12345', '#fff')).toThrow();
  });
});

describe('@pantrypress/ui tokens', () => {
  it('declares a light and a dark value for every color role', () => {
    expect(roles.length).toBeGreaterThan(0);
    for (const role of roles) {
      expect(root.has(`--D-${role}`), `--D-${role}`).toBe(true);
    }
    const darkRoles = [...root.keys()]
      .filter((name) => name.startsWith('--D-'))
      .map((name) => name.slice(4));
    expect(darkRoles.sort()).toEqual([...roles].sort());
  });

  it('declares every role the contrast checks name', () => {
    for (const { fg, bg } of pairs) {
      expect(roles, fg).toContain(fg);
      expect(roles, bg).toContain(bg);
    }
  });

  it('measures every color role, or names it as exempt', () => {
    const measured = new Set(pairs.flatMap(({ fg, bg }) => [fg, bg]));
    const unaccounted = roles.filter((role) => !measured.has(role) && !unmeasured.includes(role));
    expect(unaccounted).toEqual([]);
    for (const role of unmeasured) {
      expect(roles, role).toContain(role);
    }
  });

  it('puts dark and auto after light, which their equal specificity depends on', () => {
    const at = (pattern: RegExp): number => {
      const index = css.search(pattern);
      expect(index, String(pattern)).toBeGreaterThanOrEqual(0);
      return index;
    };
    const lightAt = at(/\[data-mode='auto'\]\s*\{\s*color-scheme:\s*light/);
    expect(at(/\[data-mode='dark'\]\s*\{/)).toBeGreaterThan(lightAt);
    expect(at(/@media \(prefers-color-scheme: dark\)/)).toBeGreaterThan(lightAt);
  });

  it.each([
    ['light', light, 'L', 'light'],
    ['dark', dark, 'D', 'dark'],
    ['auto, when the reader prefers dark', auto, 'D', 'dark'],
  ] as const)('switches every role and the color scheme in %s mode', (_, rule, mode, scheme) => {
    expect(rule.get('color-scheme')).toBe(scheme);
    const active = [...rule.keys()].filter((name) => name.startsWith('--'));
    expect(active.sort()).toEqual(roles.map((role) => `--${role}`).sort());
    for (const role of roles) {
      expect(rule.get(`--${role}`)).toBe(`var(--${mode}-${role})`);
    }
  });

  it.each([
    ['light', 'L'],
    ['dark', 'D'],
  ] as const)('meets WCAG 2.2 AA contrast in %s mode', (_, mode) => {
    const failures = pairs
      .map(({ fg, bg, min }) => ({
        fg,
        bg,
        min,
        ratio: contrastRatio(pairValue(mode, fg), pairValue(mode, bg)),
      }))
      .filter(({ ratio, min }) => ratio < min)
      .map(({ fg, bg, min, ratio }) => `${fg} on ${bg}: ${ratio.toFixed(2)} < ${min}`);
    expect(failures).toEqual([]);
  });

  it('keeps every opaque color role an opaque hex, so it can be measured', () => {
    for (const role of roles.filter((r) => !translucent.includes(r))) {
      for (const mode of ['L', 'D'] as const) {
        expect(pairValue(mode, role), `--${mode}-${role}`).toMatch(/^#[0-9a-f]{6}$/);
      }
    }
  });

  it('writes literal colors only as light and dark values', () => {
    const offenders = css
      .split(/[;{}]/)
      .map((declaration) => declaration.trim())
      .filter(
        (declaration) =>
          literalColor.test(declaration) ||
          namedColor.test(declaration.slice(declaration.indexOf(':') + 1)) ||
          /\b(?:transparent|currentcolor)\b/i.test(declaration),
      )
      .filter((declaration) => !/^--[LD]-[a-z-]+\s*:/.test(declaration));
    expect(offenders).toEqual([]);
  });

  it('keeps literal colors out of every other style source of the library and the admin app', () => {
    const sources = styleSources();
    expect(sources).toContain('projects/pantrypress-admin/src/styles.css');
    const offenders = sources.filter((file) => {
      const text = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
      return literalColor.test(text) || (/\.s?css$/.test(file) && namesAColor(text));
    });
    expect(offenders).toEqual([]);
  });

  it('catches the literal colors it is meant to', () => {
    const literals = [
      'color: #fff',
      'fill: rgb(0 0 0)',
      'color: oklch(70% 0.1 200)',
      'color: light-dark(a, b)',
    ];
    for (const sample of literals) {
      expect(literalColor.test(sample), sample).toBe(true);
    }
    for (const sample of [
      'a { color: red; }',
      'b { border: 1px solid Teal }',
      'c { background: Canvas; }',
    ]) {
      expect(namesAColor(sample), sample).toBe(true);
    }
    for (const sample of ['.field { padding: 0; }', 'mark:hover { font-weight: 700; }']) {
      expect(namesAColor(sample), sample).toBe(false);
    }
    const clean = [
      'color: var(--ink)',
      'border-color: var(--line-subtle)',
      '--text-1: 0.875rem',
      'font: var(--f-body)',
    ];
    for (const sample of clean) {
      expect(literalColor.test(sample) || namedColor.test(sample), sample).toBe(false);
    }
  });

  it('gives tokens that do not change with mode no light and dark pair', () => {
    for (const name of [
      'f-display',
      'f-body',
      'base',
      'measure',
      'r',
      'space-1',
      'text-1',
      'dur-1',
    ]) {
      expect(root.has(`--${name}`), `--${name}`).toBe(true);
      expect(root.has(`--L-${name}`) || root.has(`--D-${name}`), `--${name} pair`).toBe(false);
    }
  });

  it('loads no font from anywhere', () => {
    expect(css).not.toMatch(/@font-face|@import|url\(/i);
  });

  it('declares every shadow wherever a mode is set, so it takes that mode’s shadow color', () => {
    const elevation = block(':root, [data-mode]');
    for (const step of [1, 2, 3]) {
      expect(elevation.get(`--shadow-${step}`), `--shadow-${step}`).toContain('var(--shadow)');
    }
  });

  it('makes every motion duration instant when the reader asks for reduced motion', () => {
    const durations = [...root.keys()].filter((name) => name.startsWith('--dur-'));
    expect(durations.length).toBeGreaterThan(0);
    const reduced = block(':root', media('prefers-reduced-motion: reduce'));
    for (const name of durations) {
      expect(reduced.get(name), name).toBe('0.01ms');
    }
  });
});
