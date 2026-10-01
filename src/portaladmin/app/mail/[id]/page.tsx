import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { ArrowLeft01Icon, Layout01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "@/components/mail/mail.module.css";
import { Sending } from "@/components/mail/sending";
import { CampaignDraft } from "@/components/mail/campaign-draft";
import { Recipients } from "@/components/mail/recipients";
import { CampaignContent, CampaignMetrics, CampaignReports, ReportRefresh } from "@/components/mail/campaign-report";
import { StatusPill } from "@/components/mail/status";
import { describeSegment, when } from "@/components/mail/types";
import { currentPerson } from "@/lib/api";
import { readPlaceholders } from "@/app/templates/api";
import { Shell } from "../../shell";
import { loadFormQuestions, loadSavedRecipients, previewRecipients, saveCampaignSettings, sendNow, stopSending } from "../actions";
import { readBroadcastTemplates, readCampaign, readForms } from "../api";

/**
 * One campaign, and the only place it can be sent from.
 *
 * What the campaign is, beside what sending it would do. The second is the
 * reason the page exists — the count and a sample of the addresses, resolved
 * now, before anything goes out — and it is why this page looks unlike the
 * rest of the console: a screen whose only job is to slow somebody down for
 * ten seconds has to look different from the thirty screens that do not.
 */
export default async function Campaign({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const canManageTemplates = person.permissions.has("email.manage_templates");
  const [read, forms, templates] = await Promise.all([
    readCampaign(id),
    readForms(),
    canManageTemplates ? readBroadcastTemplates() : Promise.resolve({ templates: [] }),
  ]);

  if (!read.ok) {
    if (read.status === 404) {
      notFound();
    }

    return (
      <Shell personId={person.personId}>
        <Link href="/mail" className="back">
          ← Mail
        </Link>
        <h1>Campaign</h1>
        <div className="empty">
          {read.status === 403 ? (
            <>
              You do not have <code>email.view_stats</code>. Ask an admin.
            </>
          ) : (
            read.error
          )}
        </div>
      </Shell>
    );
  }

  const { campaign, messages, analytics } = read;
  if (campaign.status === "draft" && canManageTemplates && analytics?.content) {
    const fields = await readPlaceholders(campaign.segment ? campaign.id : undefined);
    return (
      <Shell personId={person.personId}>
        <nav className={styles.breadcrumb} aria-label="Breadcrumb">
          <Link href="/mail"><Icon icon={ArrowLeft01Icon} size={17} />Email Campaign</Link>
          <span aria-hidden="true">/</span><span>Campaign</span>
        </nav>
        <CampaignDraft
          key={campaign.id}
          campaign={campaign}
          content={analytics.content}
          available={fields.ok ? fields.items : null}
          forms={forms.forms}
          events={forms.events}
          audienceError={forms.error}
          me={person.personId}
          canSend={person.permissions.has("email.send_broadcast")}
          save={saveCampaignSettings.bind(null, id)}
          preview={previewRecipients.bind(null, id)}
          send={sendNow.bind(null, id)}
          loadQuestions={loadFormQuestions}
        />
      </Shell>
    );
  }
  const template = templates.templates.find((item) => item.key === campaign.templateKey);

  // The form's name where the segment names a form and the form is one this
  // person can read. Its id otherwise, which is less useful and still true.
  const segment = campaign.segment;
  const formName =
    segment?.type === "formRespondents" || segment?.type === "formAnswer"
      ? (forms.forms.find((form) => form.id === segment.formId)?.name ?? null)
      : null;

  /*
   * What the campaign is.
   *
   * Handed to the sending component rather than rendered beside it, because
   * the two sit in one grid and only that component knows how many rows the
   * grid has — a draft with a resolved preview has a send region under it and
   * a sent campaign does not.
   */
  const metadata = (
        <dl className={styles.facts}>
          <dt>Template</dt>
          <dd>
            {campaign.templateKey ? canManageTemplates ? (
              <Link className={styles.templateLink} href={`/templates/${encodeURIComponent(campaign.templateKey)}`}>
                {analytics?.content?.templateName || template?.name || campaign.templateKey}
              </Link>
            ) : <span>{campaign.templateKey}</span> : "—"}
          </dd>

          <dt>Audience</dt>
          <dd>{campaign.segment ? describeSegment(campaign.segment, formName) : "Not recorded"}</dd>

          {campaign.trackingEnabled !== undefined ? <>
            <dt>Tracking</dt>
            <dd>{campaign.trackingEnabled ? "Enabled" : "Disabled"}</dd>
          </> : null}

          {campaign.templateKind ? <>
            <dt>Type</dt>
            <dd className={styles.kind}>{campaign.templateKind}</dd>
          </> : null}
        </dl>
  );

  const facts = (
    <section key="overview" className={styles.card}>
      <div className={styles.cardHead}>
        <h2><Icon icon={Layout01Icon} size={19} />{analytics?.content ? "Email overview" : "Campaign details"}</h2>
      </div>
      <div className={styles.cardBody}>
        {analytics?.content ? <CampaignContent content={analytics.content}>{metadata}</CampaignContent> : metadata}
      </div>
    </section>
  );

  /*
    The frozen list, once there is one. After a send this is the only
    place the question "who did we actually mail" has an answer at all —
    the segment resolves to somebody else by then — and it is null rather
    than empty when this person may not read addresses.
  */
  const recipientPage = read.recipients;
  const recipients = recipientPage && recipientPage.total > 0 ? (
    <Recipients key="recipients" initial={recipientPage} loadPage={loadSavedRecipients.bind(null, id)} />
  ) : null;

  return (
    <Shell personId={person.personId}>
      <nav className={styles.breadcrumb} aria-label="Breadcrumb">
        <Link href="/mail"><Icon icon={ArrowLeft01Icon} size={17} />Email Campaign</Link>
        <span aria-hidden="true">/</span><span>Campaign</span>
      </nav>

      <div className={styles.head}>
        <div>
          <div className={styles.title}>
            <h1>{campaign.name}</h1>
            <StatusPill status={campaign.status} className={styles.status} />
          </div>
          <p className={styles.created}>
            Created <time dateTime={campaign.createdAt}>{when(campaign.createdAt)}</time>
          </p>
        </div>

        {/* Said at the top rather than at the button, and only while there is
            still something irreversible to do here. Somebody who learns this
            before scrolling has time to check they opened the right campaign;
            somebody looking at a campaign that has already gone is being told
            about a decision they no longer have. */}
        {campaign.status === "draft" ? (
          <p className={styles.irreversible}>Cannot be undone</p>
        ) : <ReportRefresh />}
      </div>

      <Sending
        campaign={campaign}
        canSend={person.permissions.has("email.send_broadcast")}
        me={person.personId}
        messages={messages}
        facts={facts}
        recipients={recipients}
        metrics={analytics ? <CampaignMetrics key="metrics" analytics={analytics} messages={messages} /> : null}
        reports={analytics ? <CampaignReports key="reports" analytics={analytics} /> : <p className="meta">The tracking report is currently unavailable. Refresh to try again.</p>}
        preview={previewRecipients.bind(null, id)}
        send={sendNow.bind(null, id)}
        cancel={stopSending.bind(null, id)}
      />
    </Shell>
  );
}
