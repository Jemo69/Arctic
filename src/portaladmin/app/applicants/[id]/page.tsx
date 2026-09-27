import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { ArrowLeft01Icon, ArrowUpRight01Icon, Clock01Icon, DocumentValidationIcon, FileAttachmentIcon, Mail01Icon, SchoolIcon } from "@hugeicons/core-free-icons";
import { Avatar } from "@/components/ui/avatar";
import { Icon } from "@/components/ui/icon";
import { Answers } from "@/components/applicants/answers";
import { ApplicantTime } from "@/components/applicants/applicant-time";
import { Decision } from "@/components/applicants/decision";
import { History } from "@/components/applicants/history";
import { Notes } from "@/components/applicants/notes";
import { StatusPill } from "@/components/applicants/status";
import styles from "@/components/applicants/applicant-detail.module.css";
import type { Applicant } from "@/components/applicants/types";
import { currentPerson } from "@/lib/api";
import { Shell } from "../../shell";
import { readApplicant } from "../api";

/**
 * One applicant, in full.
 *
 * Who this is sits across the top, because both columns underneath are read
 * against it and a name in the right-hand rail scrolls away from the answers
 * it is the heading for. Then answers on the left, everything that can be done
 * about them on the right. Not the other way round: the answers are what a
 * decision is made from and they are the long column, so the controls belong
 * beside the scroll rather than under it. Nobody should have to read to the
 * bottom of an essay to find the button.
 *
 * Four of the panels are behind permissions of their own and can be absent —
 * the answers, the notes, the resume link and the decision. Each says which
 * permission is missing rather than rendering empty, because "you cannot see
 * this" and "there is nothing here" are different sentences and only one of
 * them is true. Naming the permission is what turns "it doesn't work" into a
 * request an admin can act on, which matters most on a team that turns over
 * completely every year.
 *
 * Never cached. A decision is made from what this says, so a stale status
 * would be a decision made against a record somebody else has already changed.
 */
export default async function OneApplicant({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const read = await readApplicant(id);

  if (!read.ok) {
    if (read.status === 404) {
      notFound();
    }

    return (
      <Shell personId={person.personId}>
        <Link href="/applicants" className="back">
          ← Applicants
        </Link>
        <h1>Applicant</h1>
        <div className="empty">{read.error}</div>
      </Shell>
    );
  }

  const applicant = read.applicant;
  const answers = applicant.answers ?? null;

  return (
    <Shell personId={person.personId}>
      <div className={styles.page}>
        <div className={styles.toolbar}>
          <nav aria-label="Breadcrumb" className={styles.breadcrumb}>
            <Link href="/applicants" className={styles.back}>
              <Icon icon={ArrowLeft01Icon} size={18} />Applicants
            </Link>
            <span aria-hidden="true" className={styles.separator}>/</span>
            <span aria-current="page">Application</span>
          </nav>
        </div>

        <div className={styles.canvas}>
          <header className={styles.record}>
            <div className={styles.profile}>
              <Avatar name={name(applicant)} email={applicant.email} appearance="soft" className={styles.avatar} />
              <div className={styles.profileText}>
                <div className={styles.person}>
                  <h1>{name(applicant)}</h1>
                  <StatusPill status={applicant.status} className={styles.status} />
                </div>

                {/* The address as text, never in a link or a heading attribute. It is
                    what somebody typed into a public form. */}
                <div className={styles.identity}>
                  <span><Icon icon={Mail01Icon} size={16} />{applicant.email}</span>
                  {applicant.school ? <span><Icon icon={SchoolIcon} size={16} />{applicant.school}</span> : null}
                </div>
                <Dates applicant={applicant} />
              </div>
            </div>
          </header>

          <nav className={styles.sectionNav} aria-label="Application sections">
            <a href="#application-responses">Responses</a>
            <a href="#application-review">Review</a>
            <a href="#application-notes">Notes</a>
            <a href="#application-activity">Activity</a>
          </nav>

          <div className={styles.split}>
            <section className={`${styles.panel} ${styles.responses}`} id="application-responses" aria-labelledby="responses-heading">
              <div className={styles.panelHead}>
                <h2 id="responses-heading"><Icon icon={DocumentValidationIcon} size={20} />Responses</h2>
                {answers !== null ? <span className={styles.count}>{answers.length} questions</span> : null}
              </div>
              {answers === null ? (
                <p className={styles.refusal}>
                  You do not have <code>applications.view_responses</code>. Ask an
                  admin.
                </p>
              ) : (
                <Answers answers={answers} />
              )}
            </section>

            <aside className={styles.rail} aria-label="Application review">
              <section className={styles.panel} id="application-review" aria-labelledby="review-heading">
                <div className={styles.panelHead}>
                  <h2 id="review-heading">Review application</h2>
                </div>
                {/*
                  Whether this reader may decide is known before the button is
                  drawn, so it is said before the button is pressed. The permission
                  set is a courtesy and never the gate — the API refuses the change
                  whoever asks — but a reader on logistics who picks a status,
                  writes a reason and then reads "you do not have
                  applications.decide" has been told the same thing a minute later
                  and lost the reason they typed.
                */}
                <Decision
                  id={applicant.id}
                  allowedNext={applicant.allowedNext}
                  canDecide={person.permissions.has("applications.decide")}
                />
              </section>

              <section className={styles.panel} aria-labelledby="resume-heading">
                <div className={styles.panelHead}>
                  <h2 id="resume-heading">Resume</h2>
                </div>
                <Resume applicant={applicant} />
              </section>

              <section className={styles.panel} id="application-notes" aria-labelledby="notes-heading">
                <div className={styles.panelHead}>
                  <h2 id="notes-heading">Internal notes</h2>
                  {applicant.notes !== null ? <span className={styles.count}>{applicant.notes.length}</span> : null}
                </div>
                {applicant.notes === null ? (
                  <p className={styles.refusal}>
                    You do not have <code>applications.note</code>. Ask an admin.
                  </p>
                ) : (
                  <Notes id={applicant.id} notes={applicant.notes} />
                )}
              </section>

              <section className={styles.panel} id="application-activity" aria-labelledby="activity-heading">
                <div className={styles.panelHead}>
                  <h2 id="activity-heading"><Icon icon={Clock01Icon} size={19} />Activity</h2>
                </div>
                <History steps={applicant.history} />
              </section>
            </aside>
          </div>
        </div>
      </div>
    </Shell>
  );
}

/**
 * The resume, or why there is not a link to it.
 *
 * Three different absences and each gets its own sentence: no file was
 * attached, this reader may not open one, or the file is gone. The last is the
 * one worth telling somebody about — it means bytes we said we had are not
 * there — and it already shouts in the API's log.
 *
 * The middle one is the state this screen was drawn for. Logistics holds
 * `applications.view` and not `applications.view_resume` on purpose, because a
 * CV is more sensitive than a headcount, so this is not an edge case — it is
 * what a whole team sees on every record they open, and it has to read as a
 * boundary somebody drew rather than as a panel that failed to load.
 *
 * The link is signed and lives about five minutes. It is minted when this page
 * is rendered rather than with the list, because a page of fifty rows would
 * mean fifty live links to open none of the files.
 */
function Resume({ applicant }: { applicant: Applicant }) {
  if (!applicant.hasResume) {
    return <div className={styles.emptyAttachment}>
      <span className={styles.fileIcon}><Icon icon={FileAttachmentIcon} size={22} /></span>
      <p>No resume attached</p>
    </div>;
  }

  if (applicant.resume === null) {
    return (
      <p className={styles.refusal}>
        There is a resume. You do not have{" "}
        <code>applications.view_resume</code> to open it.
      </p>
    );
  }

  return (
    <div className={styles.resume}>
      {/* target and rel together. The file is one a stranger uploaded, and a
          new tab that can reach back into this one is a way for it to. */}
      <a
        href={applicant.resume.url}
        target="_blank"
        rel="noopener noreferrer"
        className={styles.filename}
      >
        <span className={styles.fileIcon}><Icon icon={FileAttachmentIcon} size={22} /></span>
        <span className={styles.fileDetails}>
          <span>{applicant.resume.filename}</span>
          <small>{applicant.resume.sizeBytes !== null ? `${kb(applicant.resume.sizeBytes)} · ` : ""}Open resume</small>
        </span>
        <Icon icon={ArrowUpRight01Icon} size={18} />
      </a>
      <p className={styles.fileExpiry}>Link expires in about five minutes.</p>
    </div>
  );
}

/**
 * The moments in this application's life, as things to compare.
 *
 * A row across the record rather than a column in the rail. They are read
 * against each other — how long it sat between submitting and a decision,
 * whether the RSVP has run out — and a stack of label-and-value pairs is a
 * stack nobody compares.
 *
 * Only the ones that happened. A grid of "—" against every stage an applicant
 * has not reached is a grid nobody reads, and the history panel below already
 * says what has and has not happened.
 */
function Dates({ applicant }: { applicant: Applicant }) {
  const rows: [string, string | null][] = [
    ["Started", applicant.createdAt],
    ["Submitted", applicant.submittedAt],
    ["Decided", applicant.decidedAt],
    ["RSVP by", applicant.rsvpDeadline],
    ["Confirmed", applicant.confirmedAt],
    ["Declined", applicant.declinedAt],
    ["Checked in", applicant.checkedInAt],
  ];

  return (
    <dl className={styles.stamps}>
      {rows
        .filter(([, at]) => at !== null)
        .map(([what, at]) => (
          <div key={what}>
            <dt>{what}</dt>
            <dd><ApplicantTime value={at!} /></dd>
          </div>
        ))}
      <div><dt>Form</dt><dd>v{applicant.formVersion}</dd></div>
    </dl>
  );
}

/**
 * What to call somebody.
 *
 * Both names are nullable: the row exists from the moment somebody opens the
 * form, and the constraint that insists on a name only applies once they
 * submit. A half-filled draft often has nothing but an address.
 */
function name(applicant: Applicant): string {
  const both = [applicant.firstName, applicant.lastName].filter(Boolean).join(" ");
  return both === "" ? applicant.email : both;
}

/** A file size somebody can judge at a glance. Never the exact byte count. */
function kb(bytes: number): string {
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
