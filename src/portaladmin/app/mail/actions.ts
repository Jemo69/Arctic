"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { CampaignListFilters, CampaignSaveResult, CampaignUpdate, Preview, RecipientRead } from "@/components/mail/types";
import {
  cancelCampaign,
  createCampaign,
  previewCampaign,
  readCampaignRecipients,
  readCampaigns,
  sendCampaign,
  updateCampaign,
} from "./api";

/**
 * The four things somebody can do to a campaign.
 *
 * Actions rather than route handlers, so the API's address and what its
 * failures mean stay on the server. None of them is a gate: the API refuses
 * the send on `email.send_broadcast` whoever asks, and these forward its
 * refusal rather than deciding anything themselves.
 */

/** What a form got back. Empty is the state before anything was submitted. */
export type FormState = { error?: string };

export async function loadCampaigns(filters: CampaignListFilters) {
  return readCampaigns(filters);
}

export type PreviewResult =
  | { ok: true; preview: Preview }
  | { ok: false; error: string };

/**
 * A send that did not happen, and why.
 *
 * `preview` comes back with the refusal when the reason is that the recipients
 * moved: the screen has to show the new number, not just say that there is
 * one.
 */
export type SendResult =
  | { ok: true; recipientCount: number }
  | { ok: false; error: string; preview?: Preview };

export type CancelResult = { ok: true } | { ok: false; error: string };

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

/**
 * Starts a campaign and opens it.
 *
 * A draft, always. Creating one sends nothing — the send is a separate act on
 * the campaign's own page, behind the preview, which is the only place it can
 * be done at all.
 */
export async function newCampaign(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const name = text(form, "name");
  if (name === "") {
    return { error: "A name is required." };
  }

  const templateKey = text(form, "templateKey");
  if (templateKey === "") {
    return { error: "A template key is required." };
  }

  const created = await createCampaign({ name, templateKey, segment: null });
  if (!created.ok) {
    return { error: created.error };
  }

  revalidatePath("/mail");
  // Outside the checks above on purpose: redirect works by throwing, so it
  // must never sit where a catch could swallow it.
  redirect(`/mail/${created.id}`);
}

/** Resolves who the campaign would go to, now. */
export async function previewRecipients(id: string): Promise<PreviewResult> {
  return previewCampaign(id);
}

export async function loadSavedRecipients(id: string, page: number): Promise<RecipientRead> {
  return readCampaignRecipients(id, page);
}

export async function saveCampaignSettings(id: string, draft: CampaignUpdate): Promise<CampaignSaveResult> {
  const result = await updateCampaign(id, draft);
  if (result.ok) {
    revalidatePath("/mail");
    revalidatePath(`/mail/${id}`);
  }
  return result;
}

/**
 * Sends, if the recipients are still the ones that were previewed.
 *
 * `seen` is the count the person actually had in front of them. The recipients
 * are resolved again here and the two are compared, so the gate is not the
 * disabled button — someone who reloads, or whose segment gained forty people
 * while they read the sample, is stopped by the server rather than by the
 * screen. The button is the courtesy; this is the control.
 */
export async function sendNow(id: string, seen: number, revision?: number): Promise<SendResult> {
  const preview = await previewCampaign(id);
  if (!preview.ok) {
    return { ok: false, error: preview.error };
  }

  if (revision !== undefined && preview.preview.revision !== revision) {
    return { ok: false, error: "The campaign changed since your preview. Review it again before sending.", preview: preview.preview };
  }

  if (preview.preview.recipientCount !== seen) {
    return {
      ok: false,
      error: "The recipients changed since you previewed. Check them again.",
      preview: preview.preview,
    };
  }

  if (preview.preview.recipientCount === 0) {
    return { ok: false, error: "Nobody matches this segment." };
  }

  const sent = await sendCampaign(id, revision);
  if (!sent.ok) {
    return { ok: false, error: sent.error };
  }

  revalidatePath("/mail");
  revalidatePath(`/mail/${id}`);
  return { ok: true, recipientCount: sent.recipientCount };
}

/** Stops a queued campaign. */
export async function stopSending(id: string): Promise<CancelResult> {
  const cancelled = await cancelCampaign(id);
  if (!cancelled.ok) {
    return { ok: false, error: cancelled.error };
  }

  revalidatePath("/mail");
  revalidatePath(`/mail/${id}`);
  return { ok: true };
}
