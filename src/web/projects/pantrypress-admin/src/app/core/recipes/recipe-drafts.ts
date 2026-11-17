import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';
import { RecipeContent } from './recipes-client';

/**
 * A save that was sent and never answered: it is sent again exactly - the same key, version, and recipe - until an
 * answer comes, because it may have landed, and only its replay says so (PP1.9).
 */
export interface OutstandingSave {
  readonly key: string;
  /** The version it was sent with; null for the save that creates the recipe. */
  readonly version: string | null;
  readonly content: RecipeContent;
  /** When it was first sent (UTC ISO 8601): the server remembers its key for 24 hours (PP1.9). */
  readonly sentAt?: string;
}

/**
 * What the recipe editor keeps on this device until the server has it (PNFR-007): the form as typed, which parts of it
 * were edited since the version it was made on, and any save still waiting for an answer. Its shape is the editor's;
 * this store only keeps it, and reads back an object or nothing.
 */
export interface RecipeDraft {
  /** The recipe's id; null for a new recipe the server has not made yet. */
  readonly recipeId: string | null;
  /** The version the edits were made on; null for a new recipe. */
  readonly baseVersion: string | null;
  readonly fields: Readonly<Record<string, unknown>>;
  readonly edited: readonly string[];
  readonly outstanding: OutstandingSave | null;
}

/** Where drafts sit; every name starts with it, so signing out can forget them all. */
export const DRAFT_PREFIX = 'pp-recipe-draft:';

/**
 * Unsaved recipe edits, kept in this browser's local storage so a reload, a closed tab, or a lost connection never loses
 * what was typed (PNFR-007). A draft is filed under the signed-in account, the site's slug, and the recipe (or `new`), so
 * another account signing in here never meets it, and one site's draft never reaches another's screen. It holds the
 * blogger's own words, so it lives only until the server has them, and signing out from the account menu forgets every
 * draft. Where storage cannot be used, drafts are kept for this page only.
 */
@Injectable({ providedIn: 'root' })
export class RecipeDrafts {
  private readonly storage = storageOf(inject(DOCUMENT));
  private readonly memory = new Map<string, string>();
  /** Set on signing out: the page is on its way out, and a save answered meanwhile must not write a draft back. */
  private cleared = false;

  read(account: string, slug: string, recipeId: string | null): RecipeDraft | null {
    const name = draftName(account, slug, recipeId);
    let raw: string | null | undefined;
    try {
      raw = this.storage?.getItem(name);
    } catch {
      raw = undefined;
    }
    raw ??= this.memory.get(name) ?? null;
    if (raw === null) {
      return null;
    }
    try {
      return readDraft(JSON.parse(raw));
    } catch {
      return null;
    }
  }

  write(account: string, slug: string, recipeId: string | null, draft: RecipeDraft): void {
    if (this.cleared) {
      return;
    }
    const name = draftName(account, slug, recipeId);
    const raw = JSON.stringify(draft);
    this.memory.set(name, raw);
    try {
      this.storage?.setItem(name, raw);
    } catch {
      // Kept for this page only: a full or refused storage must never stop the typing.
    }
  }

  remove(account: string, slug: string, recipeId: string | null): void {
    const name = draftName(account, slug, recipeId);
    this.memory.delete(name);
    try {
      this.storage?.removeItem(name);
    } catch {
      // Nothing was kept there.
    }
  }

  /** Forgets every draft on this device, for every account: signing out. */
  clearAll(): void {
    this.cleared = true;
    this.memory.clear();
    try {
      const storage = this.storage;
      if (storage === null) {
        return;
      }
      const names: string[] = [];
      for (let i = 0; i < storage.length; i += 1) {
        const name = storage.key(i);
        if (name?.startsWith(DRAFT_PREFIX)) {
          names.push(name);
        }
      }
      names.forEach((name) => storage.removeItem(name));
    } catch {
      // Nothing could be kept there.
    }
  }
}

function draftName(account: string, slug: string, recipeId: string | null): string {
  return `${DRAFT_PREFIX}${encodeURIComponent(account)}:${encodeURIComponent(slug)}:${recipeId ?? 'new'}`;
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const isText = (value: unknown): value is string => typeof value === 'string';
const isTextOrNull = (value: unknown): value is string | null => value === null || isText(value);

function readDraft(value: unknown): RecipeDraft | null {
  if (
    !isRecord(value) ||
    !isTextOrNull(value['recipeId']) ||
    !isTextOrNull(value['baseVersion']) ||
    !isRecord(value['fields']) ||
    !Array.isArray(value['edited']) ||
    !(value['edited'] as unknown[]).every(isText)
  ) {
    return null;
  }
  const outstanding = value['outstanding'];
  let save: OutstandingSave | null = null;
  if (outstanding !== null && outstanding !== undefined) {
    if (
      !isRecord(outstanding) ||
      !isText(outstanding['key']) ||
      outstanding['key'] === '' ||
      !isTextOrNull(outstanding['version']) ||
      !isRecord(outstanding['content'])
    ) {
      return null;
    }
    save = {
      key: outstanding['key'],
      version: outstanding['version'],
      // Sent back exactly as it was sent; the server reads it again, as it reads every save.
      content: outstanding['content'] as unknown as RecipeContent,
      ...(isText(outstanding['sentAt']) ? { sentAt: outstanding['sentAt'] } : {}),
    };
  }
  return {
    recipeId: value['recipeId'],
    baseVersion: value['baseVersion'],
    fields: value['fields'],
    edited: value['edited'] as string[],
    outstanding: save,
  };
}

function storageOf(document: Document): Storage | null {
  try {
    return document.defaultView?.localStorage ?? null;
  } catch {
    return null;
  }
}
