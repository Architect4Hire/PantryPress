/** One part of a site's dashboard, as the shell's navigation lists it. */
export interface SiteSection {
  /** What the link says. */
  readonly label: string;
  /** Its address under `/dashboard/sites/{slug}`; empty is the site's own page. */
  readonly path: readonly string[];
}

/**
 * The parts of a site's dashboard, in the order the navigation shows them (PP3.9). Only what exists is listed: each
 * prompt that adds a screen adds its entry here (posts, recipes, media, design, settings). The API decides who may use
 * each one; the navigation hides nothing by role.
 */
export const SITE_SECTIONS: readonly SiteSection[] = [{ label: 'Overview', path: [] }];
