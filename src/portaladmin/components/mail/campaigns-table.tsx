"use client";

import Link from "next/link";
import { ChartHistogramIcon, Mail01Icon, PencilEdit02Icon, ArrowUpRight01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { emailDocument } from "@/components/templates/email-preview";
import { thumbnailHtml } from "@/components/templates/thumbnail-html";
import styles from "./mail-list.module.css";
import { StatusPill } from "./status";
import type { CampaignRow, EventChoice, FormChoice } from "./types";

const dateFormat = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
const fullDateFormat = new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short", timeZone: "UTC" });
const number = new Intl.NumberFormat("en-US");

function CampaignAudience({ campaign, forms, events }: { campaign: CampaignRow; forms: FormChoice[]; events: EventChoice[] }) {
  const audience = campaign.audience;
  if (!audience) return <span className={styles.missing}>Not selected</span>;
  const name = audience.type === "formRespondents" ? forms.find(form => form.id === audience.sourceId)?.name
    : audience.type === "applicationStatus" ? events.find(event => event.id === audience.sourceId)?.name : null;
  const label = audience.type === "formRespondents" ? "Form respondents" : audience.type === "applicationStatus" ? "Applicants" : "Email list";
  const statusLabels = (audience.statuses ?? []).map(status => status.replaceAll("_", " ").replace(/^./, letter => letter.toUpperCase()));
  return <div className={styles.audience}>
    <span className={styles.audienceTag} title={name ?? label}>{name ?? label}</span>
    <span className={styles.audienceDetail}>
      {name ? <span>{label}</span> : audience.type === "explicitList" ? <span>{number.format(audience.count ?? 0)} {(audience.count ?? 0) === 1 ? "address" : "addresses"}</span> : null}
      {statusLabels.length ? <span title={statusLabels.join(", ")}>{statusLabels.length === 1 ? statusLabels[0] : `${statusLabels.length} statuses`}</span> : null}
    </span>
  </div>;
}

function CampaignStats({ campaign }: { campaign: CampaignRow }) {
  const summary = campaign.summary;
  const hasSent = campaign.status !== "draft" && !!summary?.sentEmails;
  const delivered = hasSent ? summary!.deliveredEmails : null;
  const opened = hasSent && summary!.openTrackedEmails > 0 ? summary!.openedEmails : null;
  const clicked = hasSent && summary!.trackedEmails > 0 ? summary!.clickedEmails : null;
  const metric = (value: number | null) => value === null ? "—" : number.format(value);
  return <div className={styles.stats}>
    <div title={hasSent ? "Confirmed deliveries reported by the email provider" : "Available after sending"}><strong>{metric(delivered)}</strong><span>Delivered</span></div>
    <div data-tone={opened !== null ? "open" : undefined} title={hasSent ? opened === null ? "Open tracking unavailable" : "Unique emails opened" : "Available after sending"}><strong>{metric(opened)}</strong><span>Opened</span></div>
    <div data-tone={clicked !== null ? "click" : undefined} title={hasSent ? clicked === null ? "No tracked links" : "Unique emails with a link click" : "Available after sending"}><strong>{metric(clicked)}</strong><span>Clicked</span></div>
  </div>;
}

export function CampaignsTable({ campaigns, forms, events, canCompose }: {
  campaigns: CampaignRow[]; forms: FormChoice[]; events: EventChoice[]; canCompose: boolean;
}) {
  return <div className={styles.tableWrap}>
    <table className={styles.table} aria-label="Campaigns">
      <thead><tr><th scope="col">Campaign</th><th scope="col">Audience</th><th scope="col">Performance</th><th scope="col">Actions</th></tr></thead>
      <tbody>{campaigns.map((campaign, index) => {
        const draft = campaign.status === "draft";
        const canEdit = draft && canCompose;
        const date = campaign.status === "sent" && campaign.sentAt ? campaign.sentAt : campaign.createdAt;
        const html = campaign.summary?.previewHtml;
        const action = canEdit ? "Edit campaign" : draft ? "View campaign" : "View report";
        return <tr key={campaign.id}>
          <td className={styles.campaignCell}><div className={styles.campaignIdentity}>
            <Link className={styles.thumbnail} href={`/mail/${campaign.id}`} tabIndex={-1} aria-hidden="true">
              {html ? <iframe title={`${campaign.name} template thumbnail`} aria-hidden="true" inert tabIndex={-1} loading={index < 4 ? "eager" : "lazy"} sandbox="" referrerPolicy="no-referrer" srcDoc={emailDocument(thumbnailHtml(html, 160))} /> : <Icon icon={Mail01Icon} size={23} />}
            </Link>
            <div className={styles.campaignCopy}>
              <Link className={styles.campaignName} href={`/mail/${campaign.id}`}>{campaign.name}</Link>
              <div className={styles.metadata}><StatusPill status={campaign.status} className={styles.status} />
                <time dateTime={date} title={`${fullDateFormat.format(new Date(date))} UTC`}>{campaign.status === "sent" && campaign.sentAt ? "Sent" : "Created"} {dateFormat.format(new Date(date))}</time>
              </div>
            </div>
          </div></td>
          <td className={styles.audienceCell}><CampaignAudience campaign={campaign} forms={forms} events={events} /></td>
          <td className={styles.statsCell}><CampaignStats campaign={campaign} /></td>
          <td className={styles.actionsCell}><Link className={styles.rowAction} href={`/mail/${campaign.id}`} aria-label={`${action}: ${campaign.name}`} title={action}>
            <Icon icon={canEdit ? PencilEdit02Icon : draft ? ArrowUpRight01Icon : ChartHistogramIcon} size={18} /><span>{canEdit ? "Edit" : draft ? "View" : "Report"}</span>
          </Link></td>
        </tr>;
      })}</tbody>
    </table>
  </div>;
}
