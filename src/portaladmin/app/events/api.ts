import { apiFetch } from "@/lib/api";
import type { EventRow } from "@/components/events/types";

/**
 * What reading the list came back with.
 *
 * A refusal is told apart from a failure because they are different sentences
 * and only one of them is anybody's fault. Everything else collapses into
 * `failed`: an organizer can do the same thing about a 500 and an unreachable
 * API, which is try again.
 */
export type EventsResult =
  | { state: "ok"; events: EventRow[] }
  | { state: "forbidden" }
  | { state: "signed-out" }
  | { state: "failed" };

export async function listEvents(): Promise<EventsResult> {
  let response: Response;

  try {
    response = await apiFetch("/admin/events");
  } catch {
    return { state: "failed" };
  }

  if (response.status === 403) {
    return { state: "forbidden" };
  }

  if (response.status === 401) {
    return { state: "signed-out" };
  }

  if (!response.ok) {
    return { state: "failed" };
  }

  try {
    const body = (await response.json()) as { events?: unknown };
    return { state: "ok", events: readEvents(body.events) };
  } catch {
    return { state: "failed" };
  }
}

/**
 * The API's answer, read field by field.
 *
 * Defensive on purpose. A date this response does not carry reads as a date
 * nobody has set, which is the truthful thing for a screen whose whole subject
 * is fields that are usually empty — and it means a response shaped slightly
 * differently from what was expected renders a row rather than a stack trace.
 */
function readEvents(value: unknown): EventRow[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.flatMap((entry) => {
    if (typeof entry !== "object" || entry === null) {
      return [];
    }

    const row = entry as Record<string, unknown>;
    const id = text(row.id);
    if (id === null) {
      return [];
    }

    return [
      {
        id,
        slug: text(row.slug) ?? "",
        name: text(row.name) ?? "",
        startsAt: text(row.startsAt),
        endsAt: text(row.endsAt),
        registrationOpensAt: text(row.registrationOpensAt),
        registrationClosesAt: text(row.registrationClosesAt),
        decisionsAnnouncedAt: text(row.decisionsAnnouncedAt),
        capacity: typeof row.capacity === "number" ? row.capacity : null,
      },
    ];
  });
}

function text(value: unknown): string | null {
  return typeof value === "string" && value !== "" ? value : null;
}
