"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Add01Icon, ArrowDown01Icon, ArrowLeft01Icon, ArrowRight01Icon, Cancel01Icon, FilterHorizontalIcon, Layout01Icon, Search01Icon, Sorting05Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { loadCampaigns, type FormState } from "@/app/mail/actions";
import type { TemplateRow } from "@/components/templates/types";
import { CampaignsTable } from "./campaigns-table";
import { NewCampaign } from "./new-campaign";
import { NoCampaigns } from "./no-campaigns";
import type { CampaignListFilters, CampaignListPage, CampaignStatus, EventChoice, FormChoice } from "./types";
import styles from "./mail-list.module.css";

const statuses: { value: CampaignStatus; label: string }[] = [
  { value: "draft", label: "Drafts" }, { value: "queued", label: "Queued" },
  { value: "sending", label: "Sending" }, { value: "sent", label: "Sent" },
  { value: "failed", label: "Failed" }, { value: "cancelled", label: "Cancelled" },
];

const initialFilters: CampaignListFilters = { search: "", status: "all", sort: "newest", page: 1 };

export function MailList({ initialPage, forms, events, templates, templatesError, canCompose, create }: {
  initialPage: CampaignListPage; forms: FormChoice[]; events: EventChoice[];
  templates: TemplateRow[]; templatesError: string | null; canCompose: boolean;
  create: (state: FormState, form: FormData) => Promise<FormState>;
}) {
  const [filters, setFilters] = useState(initialFilters);
  const [result, setResult] = useState(initialPage);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);
  const [composing, setComposing] = useState(false);

  useEffect(() => {
    if (filters === initialFilters && retry === 0) return;
    let active = true;
    setPending(true);
    setError(null);
    const timer = setTimeout(async () => {
      try {
        const next = await loadCampaigns(filters);
        if (!active) return;
        if (next.ok) setResult(next);
        else setError(next.error);
      } catch {
        if (active) setError("Campaigns could not be loaded. Please try again.");
      } finally {
        if (active) setPending(false);
      }
    }, 200);
    return () => { active = false; clearTimeout(timer); };
  }, [filters, retry]);

  const change = (patch: Partial<CampaignListFilters>) => setFilters(current => ({ ...current, page: 1, ...patch }));
  const hasFilters = filters.search.trim().length > 0 || filters.status !== "all";
  const pageCount = Math.max(1, Math.ceil(result.total / result.pageSize));
  const first = (result.page - 1) * result.pageSize + 1;
  const composeAction = canCompose ? <button type="button" className={`button primary ${styles.newButton}`} onClick={() => setComposing(true)}>
    <Icon icon={Add01Icon} size={17} />New campaign
  </button> : null;

  return <div className={styles.page}>
    <header className={styles.header}>
      <div><h1>Email Campaign</h1><p>Create campaigns, reach your audience, and follow their performance.</p></div>
      {canCompose ? <Link href="/templates" className={styles.templatesLink}><Icon icon={Layout01Icon} size={17} />Templates</Link> : null}
    </header>
    <div className={styles.toolbar}>
      <div className={styles.tools}>
        <div className={styles.search}>
          <Icon icon={Search01Icon} size={18} />
          <input type="search" aria-label="Search campaigns" placeholder="Find a campaign…" maxLength={200} value={filters.search} onChange={event => change({ search: event.target.value })} />
          {filters.search ? <button type="button" aria-label="Clear search" onClick={() => change({ search: "" })}><Icon icon={Cancel01Icon} size={14} /></button> : null}
        </div>
        <label className={styles.selectControl}>
          <Icon icon={FilterHorizontalIcon} size={17} />
          <select aria-label="Filter campaigns by status" value={filters.status} onChange={event => change({ status: event.target.value as CampaignListFilters["status"] })}>
            <option value="all">All statuses</option>
            {statuses.map(status => <option key={status.value} value={status.value}>{status.label} ({result.counts[status.value] ?? 0})</option>)}
          </select><Icon icon={ArrowDown01Icon} size={13} />
        </label>
        <label className={styles.selectControl}>
          <Icon icon={Sorting05Icon} size={17} />
          <select aria-label="Sort campaigns" value={filters.sort} onChange={event => change({ sort: event.target.value as CampaignListFilters["sort"] })}>
            <option value="newest">Newest first</option><option value="oldest">Oldest first</option><option value="name">Name A–Z</option>
          </select><Icon icon={ArrowDown01Icon} size={13} />
        </label>
      </div>{composeAction}
    </div>
    {error ? <div className={styles.error} role="alert">{error}<button type="button" onClick={() => setRetry(value => value + 1)}>Try again</button></div> : null}
    <div aria-busy={pending} className={styles.listRegion} data-loading={pending || undefined}>
      {result.items.length > 0 ? <CampaignsTable campaigns={result.items} forms={forms} events={events} canCompose={canCompose} /> : hasFilters ? (
        <EmptyState variant="data" size="page" title="No matching campaigns" description="Try another name or status to find your campaign."
          action={<button type="button" className={styles.clearButton} onClick={() => change({ search: "", status: "all" })}>Clear filters</button>} />
      ) : <NoCampaigns action={composeAction ?? undefined} />}
    </div>
    <footer className={styles.footer}>
      <p role="status">{pending ? "Updating campaigns…" : result.total ? `${first}–${Math.min(result.page * result.pageSize, result.total)} of ${result.total} ${result.total === 1 ? "campaign" : "campaigns"}` : "0 campaigns"}</p>
      {pageCount > 1 ? <nav className={styles.pagination} aria-label="Campaign pages">
        <button type="button" aria-label="Previous page" disabled={pending || result.page <= 1} onClick={() => change({ page: result.page - 1 })}><Icon icon={ArrowLeft01Icon} size={17} /></button>
        <span>Page {result.page} of {pageCount}</span>
        <button type="button" aria-label="Next page" disabled={pending || result.page >= pageCount} onClick={() => change({ page: result.page + 1 })}><Icon icon={ArrowRight01Icon} size={17} /></button>
      </nav> : null}
    </footer>
    {composing ? <NewCampaign templates={templates} templatesError={templatesError} create={create} onClose={() => setComposing(false)} /> : null}
  </div>;
}
