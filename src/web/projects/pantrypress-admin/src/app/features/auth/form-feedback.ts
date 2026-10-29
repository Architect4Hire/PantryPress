import { ApiProblem } from '../../core/api/api-problem';

/**
 * What a refused form shows (PP3.8): each field the server named, under that field, and anything it did not tie to one
 * as a notice above the form. The server's words are used as they are - its titles and field messages are written in
 * plain language for a person (api-contract.md) - so no rule of the API is restated here.
 */
export interface FormFeedback<TField extends string> {
  readonly fields: Partial<Readonly<Record<TField, string>>>;
  readonly notice: string | null;
}

export function feedbackFor<TField extends string>(
  problem: ApiProblem,
  fields: readonly TField[],
): FormFeedback<TField> {
  const named: Partial<Record<TField, string>> = {};
  let unplaced = false;
  for (const [key, messages] of Object.entries(problem.errors ?? {})) {
    const field = fields.find((candidate) => candidate === key);
    if (field !== undefined && messages.length > 0) {
      named[field] = messages[0];
    } else {
      unplaced = true;
    }
  }
  const placed = Object.keys(named).length > 0;
  return { fields: named, notice: placed && !unplaced ? null : problem.title };
}

/** Nothing to show. */
export function noFeedback<TField extends string>(): FormFeedback<TField> {
  return { fields: {}, notice: null };
}

/** Whether any field was marked, so the screen moves focus to it. */
export function marksFields(feedback: FormFeedback<string>): boolean {
  return Object.keys(feedback.fields).length > 0;
}

/**
 * Whether the server refused the link itself as malformed - its account id or token - rather than a field the person
 * typed. That is a link that does not work, said as one, not "check the details".
 */
export function refusesLink(problem: ApiProblem): boolean {
  return Object.keys(problem.errors ?? {}).some((key) => key === 'accountId' || key === 'token');
}
