import axe from 'axe-core';

/**
 * axe over a rendered primitive, for the specs only (never exported). WCAG 2.x A and AA rules, 2.2 included. Color
 * contrast is left out because jsdom computes no styles; tokens.spec.ts measures every token pair instead, and the
 * showcase's browser run (PP3.6) checks both modes as drawn.
 */
export async function axeViolations(context: Element): Promise<string[]> {
  const results = await axe.run(context, {
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
    rules: { 'color-contrast': { enabled: false } },
  });
  return results.violations.map(
    (violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
}
