import { apiFetch, type DraftView, type FormField } from "@/lib/api";
import type { ResponseItem, ResponsePage } from "@/components/responses/types";

/**
 * Reading submissions.
 *
 * Server-side only. Everything the browser needs goes through the actions in
 * this folder, so no component ever holds a URL to the API or decides what a
 * failure means.
 */

/**
 * How many responses come back at a time.
 *
 * Fifty is about two screens of rows. Small enough that the first page is on
 * screen quickly on the morning registration closes, large enough that reading
 * a few hundred is a handful of clicks rather than a chore.
 */
const PAGE = 50;

export type PageRead =
  | { ok: true; page: ResponsePage }
  | { ok: false; status: number; error: string };

export type ItemRead =
  | { ok: true; item: ResponseItem }
  | { ok: false; status: number; error: string };

/**
 * What to say about a request that did not work.
 *
 * 403 names the permission, for the same reason the rest of this console does:
 * it turns "it doesn't work" into a request an admin can act on.
 */
function why(status: number): string {
  if (status === 403) {
    return "You do not have applications.view. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return "Responses could not be loaded.";
}

/** One page of submissions, newest first. */
export async function readPage(
  formId: string,
  cursor: string | null,
): Promise<PageRead> {
  const query = new URLSearchParams({ limit: String(PAGE) });
  if (cursor !== null && cursor !== "") {
    query.set("cursor", cursor);
  }

  let response: Response;
  try {
    response = await apiFetch(`/admin/forms/${formId}/responses?${query}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: why(response.status) };
  }

  const page = (await response.json()) as ResponsePage;
  return { ok: true, page };
}

/**
 * One submission, with its resume link.
 *
 * Asked for when somebody opens a response rather than with the list. The link
 * is signed and valid about five minutes; minting fifty of them to draw a
 * table would leave fifty live links behind to read none of the files.
 */
export async function readOne(
  formId: string,
  responseId: string,
): Promise<ItemRead> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/forms/${formId}/responses/${encodeURIComponent(responseId)}`,
    );
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error:
        response.status === 404
          ? "That response could not be found."
          : why(response.status),
    };
  }

  const item = (await response.json()) as ResponseItem;
  return { ok: true, item };
}

/**
 * The questions, for the labels.
 *
 * Answers are filed under each question's key and never under its label, which
 * is what lets a form be edited after somebody has answered it. The cost is
 * that a page of answers is unreadable on its own: the words have to be joined
 * back on from the form definition, here.
 */
export async function readFields(formId: string): Promise<FormField[]> {
  try {
    const response = await apiFetch(`/admin/forms/${formId}/draft`);
    if (!response.ok) {
      return [];
    }

    return ((await response.json()) as DraftView).draft.fields;
  } catch {
    return [];
  }
}
