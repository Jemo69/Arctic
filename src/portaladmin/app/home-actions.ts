"use server";

import { apiFetch } from "@/lib/api";
import { schoolBreakdown } from "@/lib/schools";
import type {
  AnalyticsResult, AnalyticsView, BestEmailsResult, BestEmailsView, EmailResult, EmailView,
} from "./home-analytics";

/**
 * Reads one analytics view for one event, or says why it could not.
 *
 * Every analytics read is scoped to an event now, and with no event named each
 * answers for the most recent one — so the query string is the same shape for
 * all of them and only the endpoint and the permission complaint differ.
 */
async function read<T>(path: string, eventId: string | undefined, denied: string):
  Promise<{ data: T; updatedAt: string } | { error: string }> {
  try {
    const query = eventId ? `?${new URLSearchParams({ eventId })}` : "";
    const response = await apiFetch(`${path}${query}`, { signal: AbortSignal.timeout(10000) });
    if (!response.ok) return { error: response.status === 403 ? denied
      : response.status === 401 ? "Your session has expired. Sign in again to view analytics."
      : "Analytics couldn’t load. Please try again." };
    return { data: await response.json() as T, updatedAt: new Date().toISOString() };
  } catch {
    return { error: "Analytics couldn’t load. Please try again." };
  }
}

export async function loadApplicantAnalytics(eventId?: string): Promise<AnalyticsResult> {
  const result = await read<AnalyticsView>("/admin/analytics/applicants", eventId,
    "You need access to applications to view these analytics.");
  if ("error" in result) return result;
  if (result.data.analytics) {
    result.data.analytics.schools = schoolBreakdown(result.data.analytics.schools,
      process.env.LOGO_DEV_PUBLISHABLE_KEY);
  }
  return result;
}

export async function loadEmailAnalytics(eventId?: string): Promise<EmailResult> {
  return read<EmailView>("/admin/analytics/email", eventId,
    "You need access to email to view these analytics.");
}

export async function loadBestEmails(eventId?: string): Promise<BestEmailsResult> {
  return read<BestEmailsView>("/admin/analytics/email/campaigns", eventId,
    "You need access to email to view these analytics.");
}
