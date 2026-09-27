"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { Select } from "@/components/ui/select";
import { useActionState } from "react";
import { changeStatus } from "@/app/applicants/actions";
import styles from "./applicant-detail.module.css";
import { label } from "./status";
import type { Status } from "./types";

/**
 * Moving an applicant to a new status.
 *
 * The menu is built from `allowedNext`, which the API works out from the
 * lifecycle table it already has. Not a copy of that table over here: a
 * console that offered a move the API refuses would be a button whose only
 * outcome is an error message, and a console that hid a legal one would be a
 * decision nobody can make.
 *
 * That is still only a courtesy. Two reviewers on the same applicant is
 * ordinary — that is what a shared queue is — so the record can be out of date
 * by the time this is pressed, and the API refuses the move against what is
 * actually true rather than against what this screen last saw. The 409 that
 * comes back then is the system working, and it arrives here as a sentence
 * saying where the application actually is.
 *
 * The reason goes onto an append-only history row that can never be edited,
 * which is why it is optional and why it is short. Anything that wants
 * revising belongs in a note.
 */
export function Decision({
  id,
  allowedNext,
  canDecide,
}: {
  id: string;
  allowedNext: Status[];

  /**
   * Whether this reader holds `applications.decide`.
   *
   * `allowedNext` does not answer this and should not: it describes the
   * application, not the reader, and the same lifecycle is true whoever is
   * looking at it. Two different reasons for there being no button — the
   * record has nowhere to go, and this person may not move it — need two
   * different sentences, and only one of them is worth asking an admin about.
   */
  canDecide: boolean;
}) {
  const [state, submit, pending] = useActionState(changeStatus, {});
  const needsReview = allowedNext.includes("under_review") && !allowedNext.includes("accepted");

  if (!canDecide) {
    return (
      <p className={styles.refusal}>
        You do not have <code>applications.decide</code>. Ask an admin.
      </p>
    );
  }

  if (allowedNext.length === 0) {
    return (
      <p className={styles.terminal}>
        Nowhere left to go. Reversing this would be a new application rather
        than an edit, so that the history keeps saying what happened.
      </p>
    );
  }

  return (
    <form action={submit} className={styles.decide}>
      <input type="hidden" name="id" value={id} />

      <div>
        <label htmlFor="status">Update status</label>
        <Select id="status" name="status" defaultValue="" required disabled={pending} aria-describedby={needsReview ? "status-help" : undefined}>
          <option value="" disabled>
            Choose a status
          </option>
          {allowedNext.map((status) => (
            <option key={status} value={status}>
              {label(status)}
            </option>
          ))}
        </Select>
        {needsReview ? (
          <p id="status-help" className={styles.statusHelp}>
            Move to Under review first. You can then accept, waitlist, or reject this application.
          </p>
        ) : null}
      </div>

      <div>
        <label htmlFor="reason">Reason <span className={styles.optional}>(optional)</span></label>
        <textarea id="reason" name="reason" maxLength={500} rows={3} disabled={pending} placeholder="Add context for this decision…" />
      </div>

      <div>
        <button type="submit" className={`button primary ${styles.decisionButton}`} disabled={pending}>
          {pending ? "Saving…" : "Change status"}
        </button>
      </div>

      {state.error ? <ErrorToast message={state.error} revision={state} /> : null}
    </form>
  );
}
