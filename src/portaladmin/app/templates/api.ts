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
/**
 * Why a chosen form's questions did not all become placeholders.
 *
 * `alreadyFields` and `withheld` are separate counts because they are separate
 * sentences: one says the answer is offered under another name, the other says
 * it is deliberately never offered. Told only the first, an author goes looking
 * for a name that is not coming.
 */
export type AnswerSummary = {
  offered: number;
  alreadyFields: number;
  withheld: number;
  files: number;
};

export type PlaceholderRead =
  | { ok: true; items: Placeholder[]; answers: AnswerSummary | null }
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
 * Why a chosen form contributed the number of questions it did.
 *
 * Taken apart rather than cast to, like every other reader here. Absent is
 * null and means no form was named, which the screen says differently from a
 * form that had questions and offered none of them.
 */
function summary(body: unknown): AnswerSummary | null {
  if (body === null || typeof body !== "object") return null;
  const read = (key: string) => {
    const value = (body as { [k: string]: unknown })[key];
    return typeof value === "number" && Number.isFinite(value) ? value : 0;
  };
  return {
    offered: read("offered"),
    alreadyFields: read("alreadyFields"),
    withheld: read("withheld"),
    files: read("files"),
  };
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
  formId?: string | null,
): Promise<PlaceholderRead> {
  // The campaign's own list where there is a campaign, because it also narrows
  // by what its segment can fill. Otherwise the general one, told which form
  // the editor currently has chosen so it can offer the form group.
  //
  // The form the author has picked, not the one on the saved template. The
  // editor works on a working draft and choosing a form writes it there — so
  // reading the live row meant the group never appeared until a save and a
  // reload, which read as the feature simply not working.
  const path = campaignId
    ? `/admin/campaigns/${encodeURIComponent(campaignId)}/placeholders`
    : formId
      ? `/admin/templates/placeholders?form=${encodeURIComponent(formId)}`
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

  let body: { placeholders?: unknown; answers?: unknown };
  try {
    body = (await response.json()) as { placeholders?: unknown };
  } catch {
    return { ok: false, error: "Placeholders could not be loaded." };
  }

  return { ok: true, items: named(body.placeholders), answers: summary(body.answers) };
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

  return { ok: true, forms: formChoices(rows) };
}

/**
 * One form, taken apart rather than cast to.
 *
 * Shared by the two readers that need a picker's worth of forms. A row without
 * a usable id would be an option nobody can choose, so it is dropped.
 */
function formChoices(rows: unknown): FormChoice[] {
  if (!Array.isArray(rows)) {
    return [];
  }

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

  return forms;
}

// ------------------------------------------------------- automatic emails ---

/**
 * One binding: when this happens, send that email.
 *
 * The API's shape. `occasion` is one of the two values the check constraint in
 * 0049 allows, and `status` is the stored spelling of an application status —
 * the same string the applicants screen filters on, because two spellings of
 * one status is one of them being wrong somewhere.
 *
 * `templateMissing` is the cost of binding by key rather than by id, surfaced.
 * Templates are copy-on-write, so a binding follows the name and keeps working
 * when somebody fixes a typo; the other side of that is that deleting the
 * template leaves the automation pointing at nothing. The screen has to say
 * so, because the only other way anybody finds out is an applicant who was
 * accepted and never told.
 */
export type EmailTrigger = {
  id: string;
  occasion: "form_submitted" | "status_reached";
  formId: string | null;

  /** Null where the form has been removed since the binding was made. */
  formName: string | null;

  status: string | null;
  templateKey: string;
  templateMissing: boolean;
  enabled: boolean;

  /** How many messages it has queued. Zero means it has never fired. */
  sent: number;
  updatedAt: string;
};

export type EmailTriggersRead =
  | {
      ok: true;
      triggers: EmailTrigger[];

      /** The season these belong to, so the screen can name it. */
      eventId: string | null;
      eventName: string | null;

      /** The statuses the API will accept, in its own order. */
      statuses: string[];

      /** And the forms a submission trigger makes sense on. */
      forms: FormChoice[];
    }
  | { ok: false; status: number; error: string };

export type EmailTriggerWrite =
  | { ok: true; triggers: EmailTrigger[] }
  | { ok: false; error: string };

/**
 * The automations on the season being run.
 *
 * No event named, so the API answers for the newest one — the same defaulting
 * the applicants screen relies on. A console that had to know an event id
 * before it could draw its own screen would need a picker in front of a
 * picker.
 */
export async function readEmailTriggers(): Promise<EmailTriggersRead> {
  let response: Response;
  try {
    response = await apiFetch("/admin/email-triggers");
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: why(response.status, "Automatic emails could not be loaded."),
    };
  }

  const body = await readJson(response);
  const chosen = body?.chosen as { id?: unknown; name?: unknown } | null | undefined;

  return {
    ok: true,
    triggers: emailTriggers(body),
    eventId: typeof chosen?.id === "string" ? chosen.id : null,
    eventName: typeof chosen?.name === "string" ? chosen.name : null,
    statuses: Array.isArray(body?.statuses)
      ? body.statuses.filter((status): status is string => typeof status === "string")
      : [],
    forms: formChoices(body?.forms),
  };
}

export async function writeEmailTrigger(binding: {
  eventId: string | null;
  occasion: EmailTrigger["occasion"];
  formId: string | null;
  status: string | null;
  templateKey: string;
}): Promise<EmailTriggerWrite> {
  return await emailTriggerWrite("/admin/email-triggers", {
    method: "PUT",
    body: JSON.stringify(binding),
  });
}

export async function setEmailTriggerEnabled(
  id: string,
  enabled: boolean,
): Promise<EmailTriggerWrite> {
  return await emailTriggerWrite(
    `/admin/email-triggers/${encodeURIComponent(id)}/enabled`,
    { method: "PUT", body: JSON.stringify({ enabled }) },
  );
}

export async function removeEmailTrigger(id: string): Promise<EmailTriggerWrite> {
  return await emailTriggerWrite(
    `/admin/email-triggers/${encodeURIComponent(id)}`,
    { method: "DELETE" },
  );
}

/**
 * One write, and the whole list back.
 *
 * The same contract the saved values endpoint has, and for the same reason:
 * two people editing at once means one of them sees the other's row appear
 * rather than a stale screen that disagrees with what a decision will send.
 */
async function emailTriggerWrite(
  path: string,
  init: RequestInit,
): Promise<EmailTriggerWrite> {
  let response: Response;
  try {
    response = await apiFetch(path, init);
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    // The API's own sentence where it has one. It knows why a template or a
    // form was refused and this does not.
    const body = await readJson(response);
    const error = typeof body?.error === "string" ? body.error : null;
    return { ok: false, error: error ?? why(response.status, "That could not be saved.") };
  }

  return { ok: true, triggers: emailTriggers(await readJson(response)) };
}

/**
 * Taken apart rather than cast to, like every other reader here.
 *
 * A row without an id could not be switched off or removed, so it is dropped
 * rather than drawn as a binding nobody can act on.
 */
function emailTriggers(body: { [key: string]: unknown } | null): EmailTrigger[] {
  if (!Array.isArray(body?.triggers)) {
    return [];
  }

  const triggers: EmailTrigger[] = [];

  for (const entry of body.triggers) {
    if (typeof entry !== "object" || entry === null) continue;

    const row = entry as { [key: string]: unknown };
    const { id, occasion } = row;

    if (typeof id !== "string" || id === "") continue;
    if (occasion !== "form_submitted" && occasion !== "status_reached") continue;

    triggers.push({
      id,
      occasion,
      formId: typeof row.formId === "string" ? row.formId : null,
      formName:
        typeof row.formName === "string" && row.formName !== "" ? row.formName : null,
      status: typeof row.status === "string" ? row.status : null,
      templateKey: typeof row.templateKey === "string" ? row.templateKey : "",

      // Absent reads as present, which is the safe way round: a console
      // talking to an older API would otherwise warn about every binding it
      // can see.
      templateMissing: row.templateMissing === true,
      enabled: row.enabled !== false,
      sent: typeof row.sent === "number" ? row.sent : 0,
      updatedAt: typeof row.updatedAt === "string" ? row.updatedAt : "",
    });
  }

  return triggers;
}
