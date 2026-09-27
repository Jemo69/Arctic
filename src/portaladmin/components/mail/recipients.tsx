"use client";

import { Fragment, useEffect, useState, useTransition } from "react";
import { ArrowLeft01Icon, ArrowRight01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { when, type CampaignRecipientPage, type RecipientRead } from "./types";
import styles from "./mail.module.css";

export function Recipients({ initial, loadPage }: {
  initial: CampaignRecipientPage;
  loadPage: (page: number) => Promise<RecipientRead>;
}) {
  const [data, setData] = useState(initial);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  useEffect(() => {
    setData(initial);
    setError(null);
  }, [initial]);
  const totalPages = Math.max(1, Math.ceil(data.total / data.pageSize));
  const first = data.items.length ? (data.page - 1) * data.pageSize + 1 : 0;
  const last = data.items.length ? first + data.items.length - 1 : 0;
  const pages = [...new Set([1, 2, data.page - 1, data.page, data.page + 1, totalPages])]
    .filter(page => page > 0 && page <= totalPages)
    .sort((a, b) => a - b);

  function goTo(page: number) {
    if (pending || page === data.page) return;
    setError(null);
    startTransition(async () => {
      try {
        const result = await loadPage(page);
        if (result.ok) setData(result.recipients);
        else setError(result.error);
      } catch {
        setError("Recipients could not be loaded. Try again.");
      }
    });
  }

  return (
    <section className={styles.recipientsSection} aria-labelledby="recipients-heading">
      <header className={styles.recipientsHeader}>
        <div>
          <h2 id="recipients-heading">Recipients</h2>
          <p>Saved recipient details for this campaign.</p>
        </div>
        <span className={styles.sampleCount}>{data.total.toLocaleString("en-US")} recipients</span>
      </header>
      <div className={styles.recipientTableWrap} tabIndex={0} role="region" aria-label="Campaign recipients" aria-busy={pending}>
        <table className={styles.recipientTable}>
          <thead>
            <tr>
              <th scope="col">Email address</th>
              <th scope="col">First name</th>
              <th scope="col">Last name</th>
              <th scope="col">Sent at <span>(UTC)</span></th>
            </tr>
          </thead>
          <tbody>
            {data.items.map(recipient => (
              <tr key={recipient.id}>
                <td>{recipient.email}</td>
                <td>{recipient.firstName || <span className={styles.missingRecipientValue} title="Not recorded">—</span>}</td>
                <td>{recipient.lastName || <span className={styles.missingRecipientValue} title="Not recorded">—</span>}</td>
                <td>{recipient.sentAt
                  ? <time dateTime={recipient.sentAt}>{when(recipient.sentAt)}</time>
                  : <span className={styles.missingRecipientValue}>Not sent</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {error ? <p className={styles.recipientError} role="alert">{error}</p> : null}
      <footer className={styles.recipientFooter}>
        <p role="status" aria-live="polite">
          {pending ? "Loading recipients…" : `${first.toLocaleString("en-US")}–${last.toLocaleString("en-US")} of ${data.total.toLocaleString("en-US")} recipients`}
        </p>
        <nav className={styles.recipientPagination} aria-label="Recipient pages">
          <button type="button" disabled={pending || data.page === 1} onClick={() => goTo(data.page - 1)} aria-label="Previous recipient page">
            <Icon icon={ArrowLeft01Icon} size={16} /><span>Previous</span>
          </button>
          <div className={styles.recipientPageNumbers}>
            {pages.map((page, index) => (
              <Fragment key={page}>
                {index > 0 && page - pages[index - 1] > 1 ? <span aria-hidden="true">…</span> : null}
                <button type="button" aria-label={`Recipient page ${page}`} aria-current={data.page === page ? "page" : undefined} disabled={pending} onClick={() => goTo(page)}>{page}</button>
              </Fragment>
            ))}
          </div>
          <span className={styles.recipientMobilePage}>Page {data.page} of {totalPages}</span>
          <button type="button" disabled={pending || data.page === totalPages} onClick={() => goTo(data.page + 1)} aria-label="Next recipient page">
            <span>Next</span><Icon icon={ArrowRight01Icon} size={16} />
          </button>
        </nav>
      </footer>
    </section>
  );
}
