"use client";

import { useState } from "react";
import { ArrowLeft01Icon, ArrowRight01Icon, Cancel01Icon, Search01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { permissionAreas, permissionLabel } from "./permission-labels";
import styles from "./person-detail.module.css";

/** One reason a person holds a permission right now. */
export type Source = {
  kind: "team" | "grant";
  /** The team's name. Null for a grant, which has no name of its own. */
  label: string | null;
  expiresAt: string | null;
};

/** One line of the union, and why it is there. */
export type EffectiveRow = {
  permission: string;
  sources: Source[];
  /** When the last of its sources runs out. Null when one of them never does. */
  until: string | null;
  /** Held by something that never expires. Distinct from held by nothing. */
  permanent: boolean;
  sensitive: boolean;
};

/**
 * The union, with the reason for every line of it.
 *
 * The most useful thing on the screen, so it is the first thing on it. A flat
 * list of permission strings answers "what can they do" and stops; the question
 * an admin actually arrives with is "why", and the answer to that is in the
 * second column.
 *
 * It is a table because the three facts are read down their columns rather than
 * across their rows: somebody scanning for what a person can do reads the first,
 * somebody working out what to change reads the second, and somebody checking
 * what lapses before the event reads the third.
 *
 * Nothing here is coloured except an expiry, which is the one fact on the screen
 * that will change on its own while nobody is looking.
 */
export function Effective({ rows, revoked }: { rows: EffectiveRow[]; revoked: boolean }) {
  const [area, setArea] = useState("all");
  const [query, setQuery] = useState("");
  const [sensitiveOnly, setSensitiveOnly] = useState(false);
  const [page, setPage] = useState(0);
  const areas = [...new Set(rows.map(row => row.permission.split(".")[0]))].sort((a, b) =>
    (permissionAreas[a] ?? a).localeCompare(permissionAreas[b] ?? b));
  const needle = query.trim().toLowerCase();
  const filtered = rows.filter(row => (area === "all" || row.permission.startsWith(`${area}.`))
    && (!sensitiveOnly || row.sensitive)
    && [row.permission, permissionLabel(row.permission), ...row.sources.map(source => source.label ?? "Individual grant")].some(text => text.toLowerCase().includes(needle)));
  const pageSize = 10;
  const pages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const currentPage = Math.min(page, pages - 1);
  const shown = filtered.slice(currentPage * pageSize, (currentPage + 1) * pageSize);

  return <div className={styles.permissionsLayout}>
    <aside className={styles.areaRail} aria-label="Permission areas">
      <h2>Access by area</h2>
      <div className={styles.areaList} role="group" aria-label="Filter permissions by area">
        <button type="button" aria-pressed={area === "all"} onClick={() => { setArea("all"); setPage(0); }}>All permissions<span>{rows.length}</span></button>
        {areas.map(key => <button type="button" key={key} aria-pressed={area === key} onClick={() => { setArea(key); setPage(0); }}>
          {permissionAreas[key] ?? key}<span>{rows.filter(row => row.permission.startsWith(`${key}.`)).length}</span>
        </button>)}
      </div>
    </aside>
    <section className={styles.permissions} aria-labelledby="permissions-heading">
      <header className={styles.sectionHead}>
        <div><h2 id="permissions-heading">{area === "all" ? "Permissions" : permissionAreas[area] ?? area}</h2>
          <p>Access provided by active teams and individual grants.</p></div>
      </header>
      {revoked ? <p className={styles.notice}>This account is revoked. Its saved teams and grants do not allow sign-in.</p> : null}
      <div className={styles.permissionToolbar}>
        <div className={styles.search}>
          <Icon icon={Search01Icon} size={17} />
          <input type="search" aria-label="Search permissions" placeholder="Search permissions…" value={query} onChange={event => { setQuery(event.target.value); setPage(0); }} />
          {query ? <button type="button" aria-label="Clear permission search" onClick={() => { setQuery(""); setPage(0); }}><Icon icon={Cancel01Icon} size={14} /></button> : null}
        </div>
        <button type="button" className={styles.filterButton} aria-pressed={sensitiveOnly} onClick={() => { setSensitiveOnly(value => !value); setPage(0); }}>Sensitive only</button>
      </div>
      {shown.length ? <table className={styles.effective} aria-label="Effective permissions">
        <thead><tr><th scope="col">Permission</th><th scope="col">Granted through</th><th scope="col">Expires</th></tr></thead>
        <tbody>{shown.map(row => <tr key={row.permission}>
          <td><div className={styles.permissionName}>{permissionLabel(row.permission)}{row.sensitive ? <span className={styles.sensitive}>Sensitive</span> : null}</div><code className={styles.key}>{row.permission}</code></td>
          <td>{row.sources.length === 0 ? <span className={styles.blank}>Source unavailable</span> : <ul className={styles.sources}>
            {row.sources.map((source, index) => <li key={index} className={styles.source}><span>{source.label ?? "Individual grant"}</span>{source.kind === "team" ? <span className={styles.origin}>Team</span> : null}</li>)}
          </ul>}</td>
          <td className={styles.until}>{row.sources.length === 0 ? <span className={styles.blank}>—</span> : row.permanent ? "No expiry" : <time dateTime={row.until ?? undefined}>{row.until?.slice(0, 10)}</time>}</td>
        </tr>)}</tbody>
      </table> : <EmptyState variant="data" size="compact" title={rows.length ? "No matching permissions" : "No permissions yet"}
        description={rows.length ? "Try a different search or area." : "Permissions appear when a team or individual grant provides access."}
        action={rows.length ? <button className={styles.secondaryButton} type="button" onClick={() => { setQuery(""); setArea("all"); setSensitiveOnly(false); setPage(0); }}>Clear filters</button> : undefined} />}
      <footer className={styles.tableFooter}>
        <p role="status">{filtered.length ? `${currentPage * pageSize + 1}–${Math.min((currentPage + 1) * pageSize, filtered.length)} of ${filtered.length} ${filtered.length === 1 ? "permission" : "permissions"}` : "0 permissions"}</p>
        {pages > 1 ? <nav className={styles.pagination} aria-label="Permission pages">
          <button type="button" aria-label="Previous permission page" disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)}><Icon icon={ArrowLeft01Icon} size={16} /></button>
          <span>{currentPage + 1} / {pages}</span>
          <button type="button" aria-label="Next permission page" disabled={currentPage === pages - 1} onClick={() => setPage(currentPage + 1)}><Icon icon={ArrowRight01Icon} size={16} /></button>
        </nav> : null}
      </footer>
    </section>
  </div>;
}
