"use server";

import { revalidatePath } from "next/cache";
import type {
  Rendered,
  TemplateDraft,
  TemplateFormat,
  TemplateRow,
} from "@/components/templates/types";
import { createTemplate, discardSettingsDraft, fetchTemplateHtml, queueTemplateTest, readPlaceholders, readSavedValues, removeEmailTrigger, removeSavedValue, removeTemplate, renderPreview, saveSettingsDraft, saveTemplateVisibility, setEmailTriggerEnabled, updateTemplate, writeEmailTrigger, writeSavedValue } from "./api";
import { validateDesign, validateSettings, type TemplateFieldErrors } from "@/components/templates/validation";

/**
 * The two things somebody can do to a template, and the one that only looks.
 *
 * Actions rather than route handlers, so the API's address and what its
 * failures mean stay on the server. Neither of the writes is a gate: the API
 * refuses on `email.manage_templates` whoever asks, and these forward its
 * refusal rather than deciding anything themselves.
 */

/**
 * A write that landed, in the API's own terms.
 *
 * `version` is what the API says the template is now at, and `note` is
 * anything it wanted said about having written it. Both are repeated to the
 * person rather than interpreted — this screen does not know what an edit
 * means for a campaign that already used the key, and guessing would be worse
 * than showing the answer.
 */
export type SaveResult =
  | { ok: true; key: string; name: string | null; version: number | null; note: string | null }
  | { ok: false; error: string; fieldErrors?: TemplateFieldErrors; conflict?: boolean };

export type PreviewResult =
  | { ok: true; rendered: Rendered }
  | { ok: false; error: string };

/** Everything that must be there before the API is asked. */
function checked(draft: TemplateDraft): string | null {
  if (draft.fromLocal === "" || draft.fromDomain === "") {
    return "A from address is required.";
  }

  return null;
}

function trimmed(draft: TemplateDraft): TemplateDraft {
  const replyTo = draft.replyTo?.trim() ?? "";
  const fromName = draft.fromName?.trim() ?? "";

  return {
    key: draft.key?.trim() || undefined,
    name: draft.name?.trim() ?? "",
    kind: draft.kind,
    subject: draft.subject.trim(),
    previewText: draft.previewText?.trim() || null,
    clickTracking: draft.clickTracking,
    // Not trimmed to the edge on purpose: leading whitespace can be a list's
    // indentation in Markdown or an indented tag in HTML, and the API is the
    // thing that decides what the body means.
    body: draft.body.replace(/\s+$/, ""),
    format: draft.format,
    // Empty and unset are the same thing here: both mean the inbox shows the
    // address on its own.
    fromName: fromName === "" ? null : fromName,
    fromLocal: draft.fromLocal.trim(),
    fromDomain: draft.fromDomain.trim(),
    replyTo: replyTo === "" ? null : replyTo,

    // Passed through rather than trimmed: it is an id or it is nothing, and
    // an id with whitespace around it is a bug somewhere upstream rather than
    // something to quietly repair here.
    formId: draft.formId ?? null,
  };
}

export async function saveTemplateSettings(draft: TemplateDraft): Promise<SaveResult> {
  const body = trimmed(draft);
  const fieldErrors = validateSettings(body);
  if (Object.keys(fieldErrors).length) return { ok: false, error: "Check the fields below.", fieldErrors };
  const saved = await saveSettingsDraft(body);
  if (saved.ok) {
    revalidatePath("/templates");
    revalidatePath(`/templates/${saved.key}`);
  }
  return saved;
}

export async function autosaveTemplateDraft(draft: TemplateDraft): Promise<SaveResult> {
  const body = trimmed(draft);
  const fieldErrors = validateSettings(body);
  if (Object.keys(fieldErrors).length) return { ok: false, error: "Check the fields below.", fieldErrors };
  return saveSettingsDraft(body);
}

export async function discardTemplateDraft(key: string) {
  const result = await discardSettingsDraft(key);
  if (result.ok) {
    revalidatePath("/templates");
    revalidatePath(`/templates/${key}`);
  }
  return result;
}

export async function deleteTemplates(templates: Pick<TemplateRow, "key" | "version">[]) {
  if (!Array.isArray(templates) || templates.length === 0 || templates.length > 200
    || templates.some((template) => !template || typeof template.key !== "string"
      || !/^[a-z0-9][a-z0-9_-]{0,63}$/.test(template.key)
      || !Number.isSafeInteger(template.version) || template.version < 0)) {
    return { deleted: [], failed: [], error: "Select between 1 and 200 templates to delete." };
  }
  const deleted: string[] = [];
  const failed: { key: string; error: string }[] = [];
  for (const template of new Map(templates.map((item) => [item.key, item])).values()) {
    const result = await removeTemplate(template.key, template.version);
    if (result.ok) {
      deleted.push(template.key);
      revalidatePath(`/templates/${template.key}`);
    } else {
      failed.push({ key: template.key, error: result.error });
    }
  }
  if (deleted.length > 0) {
    revalidatePath("/templates");
    revalidatePath("/mail", "layout");
  }
  return { deleted, failed, error: null };
}

export async function setTemplateVisibility(keys: string[], hidden: boolean) {
  if (!Array.isArray(keys) || keys.length === 0 || keys.length > 200 || typeof hidden !== "boolean"
    || keys.some((key) => typeof key !== "string" || !/^[a-z0-9][a-z0-9_-]{0,63}$/.test(key))) {
    return { ok: false as const, error: "Select between 1 and 200 templates." };
  }
  const result = await saveTemplateVisibility([...new Set(keys)], hidden);
  if (result.ok) revalidatePath("/templates");
  return result;
}

export async function sendTemplateTest(draft: TemplateDraft, recipient: string, requestId: string) {
  const body = trimmed(draft);
  const errors = { ...validateSettings(body), ...validateDesign(body) };
  const error = Object.values(errors)[0];
  if (error) return { ok: false as const, error };
  return queueTemplateTest(body, recipient, requestId);
}

export async function importTemplateHtml(url: string) {
  return fetchTemplateHtml(url.trim());
}

export async function addTemplate(draft: TemplateDraft): Promise<SaveResult> {
  const body = trimmed(draft);
  const fieldErrors = { ...validateSettings(body), ...validateDesign(body) };
  if (Object.keys(fieldErrors).length) {
    return { ok: false, error: "Check the highlighted fields.", fieldErrors };
  }

  const wrong = checked(body);
  if (wrong) {
    return { ok: false, error: wrong };
  }

  const saved = await createTemplate(body);
  if (!saved.ok) {
    return saved;
  }

  revalidatePath("/templates");
  revalidatePath("/mail");
  return saved;
}

/**
 * Writes over a template that already exists.
 *
 * `key` is passed separately from the body because the key is the address of
 * the thing being written to, and a screen that let it drift would be creating
 * a second template while looking like it was editing the first.
 */
export async function editTemplate(
  key: string,
  draft: TemplateDraft,
): Promise<SaveResult> {
  const body = { ...trimmed(draft), key };
  const fieldErrors = { ...validateSettings(body), ...validateDesign(body) };
  if (Object.keys(fieldErrors).length) {
    return { ok: false, error: "Check the highlighted fields.", fieldErrors };
  }

  const wrong = checked(body);
  if (wrong) {
    return { ok: false, error: wrong };
  }

  const saved = await updateTemplate(key, body);
  if (!saved.ok) {
    return saved;
  }

  revalidatePath("/templates");
  revalidatePath(`/templates/${key}`);
  return saved;
}

/** What the subject and body would come out as, rendered by the sender. */
export async function previewBody(input: {
  subject: string;
  body: string;
  format: TemplateFormat;
  previewText?: string;
}): Promise<PreviewResult> {
  return renderPreview(input);
}

// ----------------------------------------------------------- saved values ---

/**
 * What a name may be, checked here for the sentence.
 *
 * The constraint in 0045 is the one that holds and the API checks it again;
 * this is so somebody typing into the form is told before a round trip. Three
 * spellings of one rule, and the database's is the one that decides.
 */
const SAVED_NAME = /^[a-z][a-zA-Z0-9]{0,39}$/;

export async function saveSavedValue(
  name: string,
  value: string,
  description: string,
) {
  if (typeof name !== "string" || !SAVED_NAME.test(name)) {
    return {
      ok: false as const,
      error:
        "A name starts with a lower-case letter and holds only letters and "
        + "numbers. No dots: those separate a group from a name.",
    };
  }

  if (typeof value !== "string" || value.trim().length === 0 || value.length > 2000) {
    return { ok: false as const, error: "A value is between 1 and 2000 characters." };
  }

  if (typeof description !== "string" || description.length > 200) {
    return { ok: false as const, error: "A description is at most 200 characters." };
  }

  const result = await writeSavedValue(
    name,
    value.trim(),
    description.trim() === "" ? null : description.trim(),
  );

  if (result.ok) {
    // Every template screen offers these in its placeholder menu, so all of
    // them are now showing a stale list.
    revalidatePath("/templates", "layout");
    revalidatePath("/mail", "layout");
  }

  return result;
}

export async function deleteSavedValue(name: string) {
  if (typeof name !== "string" || !SAVED_NAME.test(name)) {
    return { ok: false as const, error: "No saved value by that name." };
  }

  const result = await removeSavedValue(name);

  if (result.ok) {
    revalidatePath("/templates", "layout");
    revalidatePath("/mail", "layout");
  }

  return result;
}

export async function loadSavedValues() {
  const read = await readSavedValues();
  return read.ok
    ? { ok: true as const, values: read.values }
    : { ok: false as const, error: read.error };
}

// ------------------------------------------------------- automatic emails ---

/**
 * Binds a template to an occasion, or rebinds one.
 *
 * None of the checks here is the one that holds. The API refuses a broadcast
 * template, a form from another season and a status it does not recognise
 * whoever asks, and 0049's constraints refuse the row underneath that; these
 * are so somebody pressing Save gets an answer without a round trip when the
 * form is obviously incomplete.
 */
export async function saveEmailTrigger(input: {
  eventId: string | null;
  occasion: "form_submitted" | "status_reached";
  formId: string | null;
  status: string | null;
  templateKey: string;
}) {
  if (typeof input?.templateKey !== "string" || input.templateKey === "") {
    return { ok: false as const, error: "Choose an email to send." };
  }

  if (input.occasion === "form_submitted" && !input.formId) {
    return { ok: false as const, error: "Choose a form." };
  }

  if (input.occasion === "status_reached" && !input.status) {
    return { ok: false as const, error: "Choose what has to happen." };
  }

  const result = await writeEmailTrigger({
    eventId: input.eventId,
    occasion: input.occasion,

    // Only the one the occasion uses. Sending both would be sending a row the
    // check constraint refuses, and the refusal would arrive as a 500 rather
    // than as the sentence above.
    formId: input.occasion === "form_submitted" ? input.formId : null,
    status: input.occasion === "status_reached" ? input.status : null,
    templateKey: input.templateKey,
  });

  if (result.ok) {
    revalidatePath("/templates");
  }

  return result;
}

export async function switchEmailTrigger(id: string, enabled: boolean) {
  if (typeof id !== "string" || id === "" || typeof enabled !== "boolean") {
    return { ok: false as const, error: "No such automatic email." };
  }

  const result = await setEmailTriggerEnabled(id, enabled);
  if (result.ok) {
    revalidatePath("/templates");
  }

  return result;
}

export async function deleteEmailTrigger(id: string) {
  if (typeof id !== "string" || id === "") {
    return { ok: false as const, error: "No such automatic email." };
  }

  const result = await removeEmailTrigger(id);
  if (result.ok) {
    revalidatePath("/templates");
  }

  return result;
}

/**
 * The placeholder list again, for a form the author has just chosen.
 *
 * The page reads this once on the server so the menu is there on the first
 * keystroke. This is the other case: binding a form adds a whole group, and
 * waiting for a save and a reload to see it reads as the picker being broken.
 */
export async function loadPlaceholders(formId: string | null) {
  if (formId !== null && !/^[0-9a-f-]{36}$/i.test(formId)) {
    return { ok: false as const, error: "That is not a form." };
  }

  const read = await readPlaceholders(null, formId);
  return read.ok
    ? { ok: true as const, items: read.items, answers: read.answers }
    : { ok: false as const, error: read.error };
}
