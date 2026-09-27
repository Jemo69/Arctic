"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useActionState } from "react";
import { addNote } from "@/app/applicants/actions";
import styles from "./applicant-detail.module.css";
import { ApplicantTime } from "./applicant-time";
import type { Note } from "./types";

/**
 * What organizers have written about this applicant.
 *
 * Internal, and never shown to the applicant — the schema says so and it is
 * worth saying on the screen as well, because a reviewer who is not sure will
 * either write nothing useful or write something they would not want read
 * back.
 *
 * There is no edit and no delete, which is not a gap to fill in later. A note
 * is one reviewer's contemporaneous opinion; a version of it that can be
 * rewritten after the decision it justified is worth less than no note at all.
 *
 * The author is a person id rather than a name, because that is what the table
 * holds — resolving one needs `people.view`, which most of the registration
 * team does not have.
 */
export function Notes({ id, notes }: { id: string; notes: Note[] }) {
  const [state, submit, pending] = useActionState(addNote, {});

  return (
    <div className={styles.notes}>
      {notes.length === 0 ? (
        <p className={styles.emptyNotes}>No notes yet.</p>
      ) : (
        <ul>
          {notes.map((note) => (
            <li key={note.id}>
              <div className={styles.byline}>
                <span>Organizer {note.authorId.slice(0, 8)}</span>
                <ApplicantTime value={note.createdAt} />
              </div>
              <p className={styles.body}>{note.body}</p>
            </li>
          ))}
        </ul>
      )}

      {/* Ruled off from the notes above it. Reading what somebody else wrote
          and writing your own are different jobs, and a composer that sits
          flush against the list reads as the newest note. */}
      <form action={submit} className={styles.composer}>
        <input type="hidden" name="id" value={id} />
        <label htmlFor="body">Add a note</label>
        <textarea
          id="body"
          name="body"
          rows={3}
          required
          disabled={pending}
          maxLength={4000}
          placeholder="Add context for your team…"
        />
        <div className={styles.composerFoot}>
          <span>Only visible to organizers.</span>
          <button type="submit" disabled={pending}>
            {pending ? "Saving…" : "Add note"}
          </button>
        </div>
        {state.error ? <ErrorToast message={state.error} revision={state} /> : null}
      </form>
    </div>
  );
}
