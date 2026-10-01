import { apiFetch, type FormsView } from "@/lib/api";
import type {
  Campaign,
  CampaignEmailSettings,
  CampaignSaveResult,
  CampaignUpdate,
  CampaignAnalytics,
  CampaignRecipientPage,
  CampaignRow,
  CampaignListFilters,
  CampaignListPage,
  CampaignStatus,
  EventChoice,
  FormChoice,
  FormQuestion,
  FormQuestionsRead,
  MessageProgress,
  PlaceholderCoverage,
  Preview,
  Render,
  RecipientRead,
  Segment,
} from "@/components/mail/types";
import type { TemplateRow } from "@/components/templates/types";
import { readTemplates } from "@/app/templates/api";

/**
 * Talking to the campaigns API.
 *
 * Server-side only. Everything the browser needs goes through the actions
 * beside this file, so no component holds a URL to the API or decides what a
 * failure means.
 */

export type ListRead =
  | ({ ok: true } & CampaignListPage)
  | { ok: false; status: number; error: string };

export type OneRead =
  | {
      ok: true;
      campaign: Campaign;

      /** Null on a draft, which has written no messages. */
      messages: MessageProgress | null;

      /**
       * The frozen list, a corner of it. Null when this person cannot read
       * addresses — the API checks `email.manage_templates` for the sample
       * separately from `email.view_stats` for the numbers, and a screen that
       * showed an empty list instead of nothing would be reporting that the
       * campaign reached nobody.
       */
      sample: string[] | null;
      recipients?: CampaignRecipientPage | null;
      analytics?: CampaignAnalytics | null;
    }
  | { ok: false; status: number; error: string };

export type Created = { ok: true; id: string } | { ok: false; error: string };

export type PreviewRead =
  | { ok: true; preview: Preview }
  | { ok: false; error: string };

export type Changed =
  | { ok: true; status: CampaignStatus; recipientCount: number }
  | { ok: false; error: string };

/**
 * What to say about a request that did not work.
 *
 * The two permissions are named separately because they are held by different
 * people: reading what has been sent is `email.view_stats`, which most of
 * comms has, and sending to several hundred people is `email.send_broadcast`,
 * which is on the sensitive list precisely because it is not. Naming the wrong
 * one sends somebody to an admin to ask for a grant they do not need.
 */
function whyRead(status: number): string {
  if (status === 403) {
    return "You do not have email.view_stats. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return "Campaigns could not be loaded.";
}

function whyWrite(status: number): string {
  if (status === 403) {
    return "You do not have email.send_broadcast. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return "That did not work.";
}

/**
 * A campaign exactly as the API describes one.
 *
 * Written out rather than assumed, because the two shapes had drifted: the API
 * has never sent `sentAt`, which is the field these screens read to fill the
 * "Sent" column, so every campaign that had gone out was rendering an em dash.
 * It sends `queuedAt` and `completedAt` instead, and which of those a reader
 * means by "sent" is a question this file answers once.
 */
type Described = {
  audience?: CampaignRow["audience"];
  summary?: CampaignRow["summary"];
  revision?: number;
  updatedBy?: string | null;
  trackingEnabled?: boolean;
  id: string;
  name: string;
  status: CampaignStatus;
  templateKey?: string | null;
  templateKind?: string | null;
  segment?: Segment | null;
  recipientCount: number;
  createdBy?: string | null;
  approvedBy?: string | null;
  queuedAt?: string | null;
  completedAt?: string | null;
  createdAt: string;
};

/**
 * When it went, from the two stamps the API keeps.
 *
 * `completedAt` where the sender has finished with it, `queuedAt` where it is
 * still working through the queue. Both are the moment a reader means by "this
 * left", and preferring the later of them means a campaign half-way through a
 * send reads as having started rather than as not having happened.
 */
function received(row: Described): Campaign {
  return {
    revision: row.revision ?? 0,
    updatedBy: row.updatedBy ?? null,
    id: row.id,
    name: row.name,
    status: row.status,
    recipientCount: row.recipientCount,
    createdAt: row.createdAt,
    sentAt: row.completedAt ?? row.queuedAt ?? null,
    templateKey: row.templateKey ?? null,
    templateKind: row.templateKind ?? null,
    trackingEnabled: row.trackingEnabled,
    segment: row.segment ?? null,
    createdBy: row.createdBy ?? null,
    approvedBy: row.approvedBy ?? null,
    audience: row.audience,
    summary: row.summary,
  };
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

/** Every campaign, newest first as the API returns them. */
export async function readCampaigns(filters?: CampaignListFilters): Promise<ListRead> {
  let response: Response;
  try {
    const query = filters ? new URLSearchParams({ search: filters.search, status: filters.status, sort: filters.sort, page: String(filters.page) }) : null;
    response = await apiFetch(`/admin/campaigns${query ? `?${query}` : ""}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: whyRead(response.status) };
  }

  // The API calls it campaigns, not items. This screen called it items,
  // nothing typed the boundary between them, and the page threw on undefined
  // the first time it was opened against a real API.
  const { campaigns, ...paging } = (await response.json()) as Omit<CampaignListPage, "items"> & { campaigns: Described[] };
  return { ok: true, items: (campaigns ?? []).map(received), ...paging };
}

/** One campaign, with its template and its segment. */
export async function readCampaign(id: string): Promise<OneRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/campaigns/${encodeURIComponent(id)}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: whyRead(response.status) };
  }

  // Wrapped, alongside the message counts and the sample. Both were being
  // thrown away here: the counts are the only account of what actually
  // happened to a send, and the sample is the only answer left to "who did we
  // mail" once the segment has moved on.
  const body = (await response.json()) as {
    campaign: Described;
    settings?: CampaignEmailSettings | null;
    messages?: MessageProgress | null;
    sample?: string[] | null;
    recipients?: CampaignRecipientPage | null;
    analytics?: CampaignAnalytics | null;
  };

  return {
    ok: true,
    campaign: { ...received(body.campaign), settings: body.settings },
    messages: body.messages ?? null,
    sample: body.sample ?? null,
    recipients: body.recipients ?? null,
    analytics: body.analytics ?? null,
  };
}

export async function updateCampaign(id: string, draft: CampaignUpdate): Promise<CampaignSaveResult> {
  try {
    const response = await apiFetch(`/admin/campaigns/${encodeURIComponent(id)}`, {
      method: "PUT", body: JSON.stringify(draft), headers: { "content-type": "application/json" },
    });
    if (!response.ok) return { ok: false, error: await said(response, "Campaign settings could not be saved.") };
    const body = await response.json() as { campaign: Described; settings: CampaignEmailSettings };
    return { ok: true, campaign: { ...received(body.campaign), settings: body.settings } };
  } catch {
    return { ok: false, error: "The API could not be reached. Your changes have not been saved." };
  }
}

export async function readCampaignRecipients(id: string, page: number): Promise<RecipientRead> {
  if (!Number.isSafeInteger(page) || page < 1) {
    return { ok: false, error: "Choose a valid page." };
  }

  try {
    const response = await apiFetch(`/admin/campaigns/${encodeURIComponent(id)}/recipients?page=${page}`);
    if (!response.ok) {
      return {
        ok: false,
        error: response.status === 401
          ? "Your session has ended. Sign in again."
          : response.status === 403
            ? "You do not have permission to view recipients."
            : "Recipients could not be loaded. Try again.",
      };
    }
    return { ok: true, recipients: await response.json() as CampaignRecipientPage };
  } catch {
    return { ok: false, error: "Recipients could not be loaded. Try again." };
  }
}

/** Starts a campaign as a draft. Nothing is sent by creating one. */
export async function createCampaign(body: {
  name: string;
  templateKey: string;
  segment: Segment | null;
  trackingEnabled?: boolean;
}): Promise<Created> {
  let response: Response;
  try {
    response = await apiFetch("/admin/campaigns", {
      method: "POST",
      body: JSON.stringify(body),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(response, whyWrite(response.status)),
    };
  }

  const { id } = (await response.json()) as { id: string };
  return { ok: true, id };
}

/**
 * Who this campaign would go to if it were sent now.
 *
 * Resolved on every call rather than cached. A preview from ten minutes ago is
 * a different set of people from the one a send would reach, and the whole
 * value of this screen is that the number in front of somebody is the number
 * that will be mailed.
 */
export async function previewCampaign(id: string): Promise<PreviewRead> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/campaigns/${encodeURIComponent(id)}/preview`,
      { method: "POST" },
    );
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(
        response,
        response.status === 403
          ? whyRead(response.status)
          : "The recipients could not be resolved.",
      ),
    };
  }

  const preview = (await response.json()) as Preview;
  return { ok: true, preview: checked(preview) };
}

/**
 * A preview body, reduced to the parts this screen can actually render.
 *
 * The cast above is a promise, not a check, and two of the fields it promises
 * are new: an API that has not shipped them yet sends nothing, and one that
 * has sends them in a shape this screen has never seen. Everywhere else that
 * costs a wrong number on a panel; here it costs a crash, because the coverage
 * list decides whether a warning appears at all and the renders are put
 * through an iframe.
 *
 * So the rule is the same for both: a field that is not the shape it was
 * promised as leaves this function absent rather than half-trusted. Absent is
 * a state the screen already knows how to be in — it was the only state before
 * these fields existed.
 */
function checked(preview: Preview): Preview {
  return {
    ...preview,
    sample: strings(preview.sample),

    // Counted rather than trusted, like the coverage numbers: it is rendered
    // through toLocaleString, so a string here is a crash on the one screen
    // that must not have one. Absent and zero are shown the same way — as
    // nothing — so collapsing them loses nothing.
    unreachableCount: counted(preview.unreachableCount),
    problems: Array.isArray(preview.problems)
      ? strings(preview.problems)
      : undefined,
    placeholderCoverage: coverage(preview.placeholderCoverage),
    renders: renders(preview.renders),
  };
}

function strings(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string")
    : [];
}

function counted(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0
    ? Math.trunc(value)
    : 0;
}

function coverage(value: unknown): PlaceholderCoverage[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }

  return value
    .filter(
      (entry): entry is PlaceholderCoverage =>
        typeof entry?.placeholder === "string" && entry.placeholder !== "",
    )
    .map((entry) => ({
      placeholder: entry.placeholder,
      missing: counted(entry.missing),
      total: counted(entry.total),
      examples: strings(entry.examples),
    }));
}

function renders(value: unknown): Render[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }

  return value
    .filter(
      (entry): entry is Render =>
        typeof entry?.email === "string" &&
        typeof entry.html === "string" &&
        typeof entry.text === "string",
    )
    .map((entry) => ({
      email: entry.email,
      subject: typeof entry.subject === "string" ? entry.subject : "",
      html: entry.html,
      text: entry.text,
      unfilled: strings(entry.unfilled),
    }));
}

/** Hands the campaign to the sender. There is no undo past this. */
export async function sendCampaign(id: string, revision?: number): Promise<Changed> {
  return change(id, "send", revision);
}

/** Stops a campaign that is queued. */
export async function cancelCampaign(id: string): Promise<Changed> {
  return change(id, "cancel");
}

async function change(id: string, verb: "send" | "cancel", revision?: number): Promise<Changed> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/campaigns/${encodeURIComponent(id)}/${verb}`,
      { method: "POST", ...(revision === undefined ? {} : {
        body: JSON.stringify({ revision }), headers: { "content-type": "application/json" },
      }) },
    );
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(response, whyWrite(response.status)),
    };
  }

  // The API answers with queued -- how many messages were written -- rather
  // than recipientCount. They are the same number and it used the clearer
  // name; reading the wrong one showed "sent to 0 recipients" after a send
  // that had just written several hundred rows.
  const body = (await response.json()) as {
    status: CampaignStatus;
    queued?: number;
    suppressed?: number;
    recipientCount?: number;
  };

  return {
    ok: true,
    status: body.status,
    recipientCount: body.queued ?? body.recipientCount ?? 0,
  };
}

/**
 * The forms, for the segment that picks one.
 *
 * Failure is not fatal. Without `applications.view` this comes back empty, the
 * form segment offers nothing, and the other two still work — which is less
 * useful than the whole screen and better than none of it.
 */
export async function readForms(): Promise<{
  forms: FormChoice[];
  events: EventChoice[];
  error: string | null;
}> {
  try {
    const response = await apiFetch("/admin/forms");
    if (!response.ok) {
      return {
        forms: [], events: [],
        error: response.status === 401
          ? "Your session has ended. Sign in again."
          : response.status === 403
            ? "You do not have permission to load forms and events."
            : "Forms and events could not be loaded. Refresh the page to try again.",
      };
    }

    // The events ride along with the forms rather than being fetched
    // separately, because that endpoint already returns them and an
    // applicantStatus segment cannot be built without one.
    const { forms, events } = (await response.json()) as FormsView & {
      events?: EventChoice[];
    };

    return {
      forms: forms.map((form) => ({ id: form.id, name: form.name })),
      events: (events ?? []).map((event) => ({ id: event.id, name: event.name })),
      error: null,
    };
  } catch {
    return { forms: [], events: [], error: "Forms and events could not be loaded. Refresh the page to try again." };
  }
}

/**
 * One form's questions, for the segment that picks an answer.
 *
 * The middle and last steps of form → question → value in one read, because
 * the options a choice question declares are part of the form and come back
 * with it. Behind `applications.view`, the same permission the form list above
 * needs — deliberately not `applications.view_responses`, because nothing here
 * is anybody's answer.
 *
 * The error comes back rather than an empty list. "This form asks nothing we
 * can match on" and "you are not allowed to read this form" put somebody on
 * completely different errands, and so does a connection that dropped.
 */
export async function readFormQuestions(formId: string): Promise<FormQuestionsRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/forms/${encodeURIComponent(formId)}/questions`);
  } catch {
    return { ok: false, error: "The form's questions could not be loaded. Try again." };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: response.status === 401
        ? "Your session has ended. Sign in again."
        : response.status === 403
          ? "You do not have permission to read this form's questions."
          : response.status === 404
            ? "That form no longer exists."
            : "The form's questions could not be loaded. Try again.",
    };
  }

  const body = (await response.json()) as {
    version?: number | null;
    questions?: unknown;
  };

  return {
    ok: true,
    version: typeof body.version === "number" ? body.version : null,
    questions: questions(body.questions),
  };
}

/**
 * A question list reduced to what the picker can actually draw.
 *
 * The cast above is a promise rather than a check, and this list decides what a
 * dropdown offers — so a question with no key is dropped instead of becoming an
 * option that stores an empty criterion.
 */
function questions(value: unknown): FormQuestion[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .filter(
      (entry): entry is FormQuestion =>
        typeof entry?.key === "string" && entry.key !== "",
    )
    .map((entry) => ({
      key: entry.key,
      label: typeof entry.label === "string" && entry.label !== "" ? entry.label : entry.key,
      type: typeof entry.type === "string" ? entry.type : "",
      values: Array.isArray(entry.values)
        ? entry.values
            .filter((option) => typeof option?.value === "string")
            .map((option) => ({
              value: option.value,
              label: typeof option.label === "string" && option.label !== "" ? option.label : option.value,
            }))
        : [],
      unmatchable: typeof entry.unmatchable === "string" ? entry.unmatchable : null,
    }));
}

/**
 * The templates a campaign is allowed to send.
 *
 * Broadcast only, filtered here rather than on the screen. A campaign given a
 * transactional template is refused when somebody tries to send it, with a
 * message about a lane and a subdomain that reads like a bug — and by then the
 * campaign has been named, segmented and previewed. Offering only what can be
 * chosen means the refusal cannot happen.
 *
 * The list is the templates screen's fetch, not a second one. One reader of
 * the endpoint means one answer to what a template is.
 *
 * The error comes back rather than an empty list, because "there are no
 * templates yet" and "you are not allowed to see them" put somebody on
 * completely different errands.
 */
export async function readBroadcastTemplates(): Promise<{
  templates: TemplateRow[];
  error: string | null;
}> {
  const read = await readTemplates(false, true);

  if (!read.ok) {
    return { templates: [], error: read.error };
  }

  return {
    templates: read.items.filter((template) => template.kind === "broadcast"),
    error: null,
  };
}
