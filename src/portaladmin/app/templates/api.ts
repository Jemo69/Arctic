import { apiFetch } from "@/lib/api";
import type {
  FormChoice,
  Placeholder,
  Rendered,
  Template,
  TemplateDraft,
  TemplateFormat,
  TemplateRow,
} from "@/components/templates/types";

/**
 * Talking to the templates API.
 *
 * Server-side only. Everything the browser needs goes through the actions
 * beside this file, so no component holds a URL to the API or decides what a
 * failure means.
 */

export type ListRead =
  | { ok: true; items: TemplateRow[]; hiddenKeys: string[] }
  | { ok: false; status: number; error: string };

export type OneRead =
  | { ok: true; template: Template }
  | { ok: false; status: number; error: string };

/**
 * What the API did with a write.
 *
 * `version` and `note` are the API's own answer, passed through rather than
 * summarised. An edit bumps the version, and what that means for a campaign
 * already pointed at this key is the API's business to decide and say — this
 * screen repeats it and does not paraphrase it.
 */
export type Saved =
  | { ok: true; key: string; name: string | null; version: number | null; note: string | null }
  | { ok: false; error: string; conflict?: boolean };

export type PreviewRead =
  | { ok: true; rendered: Rendered }
  | { ok: false; error: string };

/**
 * The names a send can fill in.
 *
 * A failure here is not an error on the page. The editor simply stops offering
 * a menu and stops calling anything unknown, because the only thing worse than
 * not knowing which placeholders resolve is being told the wrong ones.
 */
export type PlaceholderRead =
  | { ok: true; items: Placeholder[] }
  | { ok: false; error: string };

/**
 * What to say about a request that did not work.
 *
 * `email.manage_templates` is named because it is the grant that is missing,
 * and it is not the one the compose screen names — somebody who can send a
 * broadcast cannot necessarily write one. Naming the wrong permission sends
 * somebody to an admin to ask for a grant they do not need.
 */
function why(status: number, fallback: string): string {
  if (status === 403) {
    return "You do not have email.manage_templates. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return fallback;
}

/** The API's own sentence about a refusal, where it gave one. */
async function said(response: Response, fallback: string): Promise<string> {
  try {
    const { error } = (await response.json()) as { error?: string };
    return error ?? fallback;
  } catch {
    return fallback;
  }
}

/**
 * Whatever the API wants said about a write that worked.
 *
 * Read rather than assumed. The version an edit lands on is the API's to
 * decide, and if it has something to add about a template that has already
 * been used, that sentence is its own and is shown as it was written.
 */
function noted(body: Record<string, unknown>): string | null {
  for (const field of ["note", "warning", "message"]) {
    const value = body[field];
    if (typeof value === "string" && value !== "") {
      return value;
    }
  }

  return null;
}

/** Every template, as the API orders them. */
export async function readTemplates(includeDrafts = false, includePreviews = false): Promise<ListRead> {
  let response: Response;
  const params = new URLSearchParams();
  if (includeDrafts) params.set("includeDrafts", "true");
  if (includePreviews) params.set("includePreviews", "true");
  const query = params.size > 0 ? `?${params}` : "";
  try {
    response = await apiFetch(`/admin/templates${query}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: why(response.status, "Templates could not be loaded."),
    };
  }

  const { templates, hiddenKeys = [] } = (await response.json()) as { templates: TemplateRow[]; hiddenKeys?: string[] };
  return { ok: true, items: templates, hiddenKeys };
}

/** One template, with its body and everything rendered from it. */
export async function readTemplate(key: string): Promise<OneRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/templates/${encodeURIComponent(key)}?draft=true`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: why(response.status, "That template could not be loaded."),
    };
  }

  const template = (await response.json()) as Template;
  return { ok: true, template };
}

/** Writes a template that did not exist. */
export async function createTemplate(draft: TemplateDraft): Promise<Saved> {
  return write("POST", "/admin/templates", draft);
}

export async function saveSettingsDraft(draft: TemplateDraft): Promise<Saved> {
  return write("POST", "/admin/templates/settings", draft);
}

export async function fetchTemplateHtml(url: string): Promise<{ ok: true; body: string } | { ok: false; error: string }> {
  try {
    const response = await apiFetch("/admin/templates/import", {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ url }),
    });
    if (!response.ok) return { ok: false, error: await said(response, why(response.status, "The email could not be imported. Try another URL.")) };
    const { body } = await response.json() as { body: string };
    return { ok: true, body };
  } catch {
    return { ok: false, error: "The API could not be reached. Try again." };
  }
}

export async function queueTemplateTest(draft: TemplateDraft, recipient: string, requestId: string): Promise<{ ok: true } | { ok: false; error: string }> {
  try {
    const response = await apiFetch("/admin/templates/test", {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ draft, recipient, requestId }),
    });
    if (response.ok) return { ok: true };
    const fallback = response.status === 403 ? "You need permission to send test emails. Ask an admin." : "The test email could not be queued. Try again.";
    return { ok: false, error: await said(response, response.status === 401 ? "Your session has ended. Sign in again." : fallback) };
  } catch {
    return { ok: false, error: "The API could not be reached. Try again." };
  }
}

export async function discardSettingsDraft(key: string): Promise<{ ok: true } | { ok: false; error: string }> {
  try {
    const response = await apiFetch(`/admin/templates/${encodeURIComponent(key)}/draft`, { method: "DELETE" });
    return response.ok ? { ok: true } : { ok: false, error: await said(response, why(response.status, "The draft could not be discarded.")) };
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }
}

export async function removeTemplate(key: string, version: number): Promise<{ ok: true } | { ok: false; error: string }> {
  try {
    const response = await apiFetch(`/admin/templates/${encodeURIComponent(key)}?version=${version}`, { method: "DELETE" });
    const fallback = response.status === 403
      ? "You do not have permission to delete templates. Ask an admin."
      : why(response.status, "The template could not be deleted. Try again.");
    return response.ok ? { ok: true } : { ok: false, error: await said(response, fallback) };
  } catch {
    return { ok: false, error: "The API could not be reached. Try again." };
  }
}

export async function saveTemplateVisibility(keys: string[], hidden: boolean): Promise<
  { ok: true; hiddenKeys: string[] } | { ok: false; error: string }
> {
  try {
    const response = await apiFetch("/admin/templates/preferences/visibility", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ keys, hidden }),
    });
    if (!response.ok) {
      const fallback = response.status === 403
        ? "You do not have permission to view templates. Ask an admin."
        : why(response.status, "Your hidden templates could not be saved. Try again.");
      return { ok: false, error: await said(response, fallback) };
    }
    const { hiddenKeys } = await response.json() as { hiddenKeys: string[] };
    return { ok: true, hiddenKeys };
  } catch {
    return { ok: false, error: "The API could not be reached. Try again." };
  }
}

/** Writes over a template that did. The API bumps the version. */
export async function updateTemplate(
  key: string,
  draft: TemplateDraft,
): Promise<Saved> {
  return write("PUT", `/admin/templates/${encodeURIComponent(key)}`, draft);
}

async function write(
  method: "POST" | "PUT",
  path: string,
  draft: TemplateDraft,
): Promise<Saved> {
  let response: Response;
  try {
    response = await apiFetch(path, {
      method,
      body: JSON.stringify(draft),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      conflict: response.status === 409,
      error: await said(response, why(response.status, "That did not work.")),
    };
  }

  let body: Record<string, unknown> = {};
  try {
    body = (await response.json()) as Record<string, unknown>;
  } catch {
    // A write that answered with no body still worked. The version is then
    // simply not known here, which is better than inventing one.
  }

  const key = typeof body.key === "string" ? body.key : draft.key;
  if (!key) {
    return { ok: false, error: "The template was saved, but its identifier could not be read. Reload the templates list to open it." };
  }

  return {
    ok: true,
    key,
    name: typeof body.name === "string" ? body.name : null,
    version: typeof body.version === "number" ? body.version : null,
    note: noted(body),
  };
}

/**
 * What the body would come out as.
 *
 * Rendered by the API on every keystroke's worth of pause rather than in the
 * browser. There is one markdown renderer in this system and it is the one the
 * sender uses; a second one here would agree with it right up until the day
 * somebody types the thing they disagree about, and the copy that goes to four
 * hundred people is the one this screen never showed.
 */
export async function renderPreview(input: {
  subject: string;
  body: string;
  format: TemplateFormat;
  previewText?: string;
  values?: Record<string, string>;
}): Promise<PreviewRead> {
  let response: Response;
  try {
    response = await apiFetch("/admin/templates/preview", {
      method: "POST",
      body: JSON.stringify(input),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(
        response,
        why(response.status, "The preview could not be rendered."),
      ),
    };
  }

  return { ok: true, rendered: (await response.json()) as Rendered };
}

/**
 * Which placeholders resolve, from the only thing that knows.
 *
 * Two endpoints behind one function. With no campaign this is the general
 * list, which is the editor's ordinary case: a template is written long before
 * anybody decides who it goes to. Given a campaign it is that campaign's,
 * narrowed to what its segment can actually fill.
 *
 * A campaign that cannot be read is never quietly widened to the general list.
 * The narrow list exists precisely because the general one contains names this
 * segment has no value for, so falling back would hand somebody a placeholder
 * that renders empty — or refuses — for the exact audience they were writing
 * to.
 */
export async function readPlaceholders(
  campaignId?: string | null,
  templateKey?: string | null,
): Promise<PlaceholderRead> {
  // The campaign's own list where there is a campaign, because it also narrows
  // by what its segment can fill. Otherwise the template's, named so the API
  // can decide whether to offer the form group — it only does when that
  // template actually names a form.
  //
  // The key is the saved template rather than the draft, so picking a form and
  // not saving leaves the group out until the autosave lands. Offering it from
  // the draft would mean the menu disagreeing with what a send can fill, which
  // is the one thing this list exists to prevent.
  const path = campaignId
    ? `/admin/campaigns/${encodeURIComponent(campaignId)}/placeholders`
    : templateKey
      ? `/admin/templates/placeholders?template=${encodeURIComponent(templateKey)}`
      : "/admin/templates/placeholders";

  let response: Response;
  try {
    response = await apiFetch(path);
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: why(response.status, "Placeholders could not be loaded."),
    };
  }

  let body: { placeholders?: unknown };
  try {
    body = (await response.json()) as { placeholders?: unknown };
  } catch {
    return { ok: false, error: "Placeholders could not be loaded." };
  }

  return { ok: true, items: named(body.placeholders) };
}

/**
 * The list, taken apart rather than cast to.
 *
 * Every name in here ends up in a menu somebody inserts from, so a row without
 * a usable name is dropped instead of becoming `{{undefined}}` in an email.
 * A missing description is null and not an empty string, because the editor
 * lays the row out differently when there is nothing to say.
 */
function named(value: unknown): Placeholder[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const items: Placeholder[] = [];

  for (const entry of value) {
    if (typeof entry !== "object" || entry === null) {
      continue;
    }

    const { name, description, group } = entry as {
      name?: unknown;
      description?: unknown;
      group?: unknown;
    };

    if (typeof name !== "string" || name === "") {
      continue;
    }

    items.push({
      name,
      description:
        typeof description === "string" && description !== ""
          ? description
          : null,
      group: typeof group === "string" && group !== "" ? group : null,
    });
  }

  return items;
}

// ----------------------------------------------------------- saved values ---

export type SavedValue = {
  name: string;
  value: string;
  description: string | null;
  updatedAt: string;
};

export type SavedValuesRead =
  | { ok: true; values: SavedValue[] }
  | { ok: false; status: number; error: string };

export type SavedValueWrite =
  | { ok: true; values: SavedValue[] }
  | { ok: false; error: string };

/**
 * The values an organizer saved, newest spelling of each.
 *
 * Read on the templates screen rather than its own, because that is where they
 * are used and the permission is the same one.
 */
export async function readSavedValues(): Promise<SavedValuesRead> {
  let response: Response;
  try {
    response = await apiFetch("/admin/saved-values");
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: why(response.status, "Saved values could not be loaded."),
    };
  }

  return { ok: true, values: savedValues(await readJson(response)) };
}

export async function writeSavedValue(
  name: string,
  value: string,
  description: string | null,
): Promise<SavedValueWrite> {
  return await savedValueWrite(
    `/admin/saved-values/${encodeURIComponent(name)}`,
    { method: "PUT", body: JSON.stringify({ value, description }) },
  );
}

export async function removeSavedValue(name: string): Promise<SavedValueWrite> {
  return await savedValueWrite(
    `/admin/saved-values/${encodeURIComponent(name)}`,
    { method: "DELETE" },
  );
}

/**
 * One write, and the whole list back.
 *
 * The API answers every write with the current list, so the screen never has
 * to guess what it now looks like — which is what stops two people editing at
 * once from leaving one of them looking at a row that is gone.
 */
async function savedValueWrite(
  path: string,
  init: RequestInit,
): Promise<SavedValueWrite> {
  let response: Response;
  try {
    response = await apiFetch(path, init);
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    // The API's own sentence where it has one. It knows why a name was
    // refused and this does not.
    const body = await readJson(response);
    const error = typeof body?.error === "string" ? body.error : null;
    return { ok: false, error: error ?? why(response.status, "That could not be saved.") };
  }

  return { ok: true, values: savedValues(await readJson(response)) };
}

async function readJson(response: Response): Promise<{ [key: string]: unknown } | null> {
  try {
    return (await response.json()) as { [key: string]: unknown };
  } catch {
    return null;
  }
}

/**
 * Taken apart rather than cast to, like `named` above.
 *
 * A row without a usable name would become a `{{saved.undefined}}` somebody
 * inserts, so it is dropped instead.
 */
function savedValues(body: { [key: string]: unknown } | null): SavedValue[] {
  if (!Array.isArray(body?.values)) {
    return [];
  }

  const values: SavedValue[] = [];

  for (const entry of body.values) {
    if (typeof entry !== "object" || entry === null) continue;

    const { name, value, description, updatedAt } = entry as {
      name?: unknown;
      value?: unknown;
      description?: unknown;
      updatedAt?: unknown;
    };

    if (typeof name !== "string" || name === "") continue;

    values.push({
      name,
      value: typeof value === "string" ? value : "",
      description:
        typeof description === "string" && description !== "" ? description : null,
      updatedAt: typeof updatedAt === "string" ? updatedAt : "",
    });
  }

  return values;
}

// --------------------------------------------------- forms, for the picker ---

export type FormChoicesRead =
  | { ok: true; forms: FormChoice[] }
  | { ok: false; error: string };

/**
 * The forms a template can say it is about.
 *
 * Behind `applications.view`, which is wider than the permission that edits
 * templates — so somebody who may write an email and may not read the
 * application queue gets an empty list rather than an error. An empty picker
 * is a screen that says "no forms"; a failed read would be a screen that will
 * not draw.
 *
 * Removed forms never arrive: `ForEventAsync` filters `removed_at IS NULL`
 * in the query. Worth knowing rather than re-filtering here, because a second
 * filter would imply the API sends them and quietly rot when it does not.
 *
 * Scoped to one event, which is what the endpoint serves. A template is about
 * this season's form, so that is the right list — but it does mean a template
 * bound to an older season's form cannot be re-bound to it from here.
 */
export async function readFormChoices(): Promise<FormChoicesRead> {
  let response: Response;
  try {
    response = await apiFetch("/admin/forms");
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return { ok: false, error: why(response.status, "Forms could not be loaded.") };
  }

  let body: unknown;
  try {
    body = await response.json();
  } catch {
    return { ok: false, error: "Forms could not be loaded." };
  }

  const rows = Array.isArray(body)
    ? body
    : Array.isArray((body as { forms?: unknown })?.forms)
      ? (body as { forms: unknown[] }).forms
      : [];

  const forms: FormChoice[] = [];

  for (const entry of rows) {
    if (typeof entry !== "object" || entry === null) continue;

    const { id, name, code } = entry as {
      id?: unknown;
      name?: unknown;
      code?: unknown;
    };

    if (typeof id !== "string" || id === "") continue;

    forms.push({
      id,
      name: typeof name === "string" && name !== "" ? name : "Untitled form",
      code: typeof code === "string" ? code : "",
    });
  }

  return { ok: true, forms };
}
