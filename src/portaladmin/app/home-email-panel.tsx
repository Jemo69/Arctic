import { apiFetch } from "@/lib/api";
import type { EmailView } from "./home-analytics";
import { HomeEmailView } from "./home-email";
import { HomeUpdatedAt } from "./home-updated-at";

/**
 * The email panel for somebody who cannot see applications, and so has no event
 * picker to scope it with. It answers for the most recent event — the one being
 * run — and names it in the heading rather than presenting a workspace-wide
 * total as if it belonged to any one event.
 */
export async function HomeEmail() {
  try {
    const response = await apiFetch("/admin/analytics/email", { signal: AbortSignal.timeout(10000) });
    if (!response.ok) return <HomeEmailView error />;
    const view = await response.json() as EmailView;
    return <><HomeEmailView data={view.analytics} scope={view.chosen?.name} />
      <HomeUpdatedAt at={new Date().toISOString()} /></>;
  } catch {
    return <HomeEmailView error />;
  }
}
