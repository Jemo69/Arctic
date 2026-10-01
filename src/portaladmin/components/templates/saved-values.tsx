"use client";

import { useState, useTransition } from "react";
import { ErrorToast } from "@/components/ui/error-toast";
import { deleteSavedValue, saveSavedValue } from "@/app/templates/actions";
import type { SavedValue } from "@/app/templates/api";
import styles from "./saved-values.module.css";

/**
 * The values an organizer types once and reuses in any template.
 *
 * Here rather than on a screen of its own, and rather than under a settings
 * page that does not exist. These are only ever used in templates, they are
 * behind the permission that edits templates, and a person looking for
 * "what does {{saved.discordInvite}} mean" is already on this screen.
 *
 * The whole list comes back from every write, so this never has to work out
 * what the list now looks like. Two people editing at once means one of them
 * sees the other's row appear rather than a stale screen that disagrees with
 * what a send will use.
 */
export function SavedValues({
  initial,
  canManage,
  loadError,
}: {
  initial: SavedValue[];
  canManage: boolean;

  /** Why the list is empty, when it is empty because something broke. */
  loadError: string | null;
}) {
  const [values, setValues] = useState(initial);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [value, setValue] = useState("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState("");
  const [pending, start] = useTransition();

  function edit(saved: SavedValue) {
    setEditing(saved.name);
    setName(saved.name);
    setValue(saved.value);
    setDescription(saved.description ?? "");
    setError("");
    setOpen(true);
  }

  function add() {
    setEditing(null);
    setName("");
    setValue("");
    setDescription("");
    setError("");
    setOpen(true);
  }

  function save() {
    start(async () => {
      const result = await saveSavedValue(name.trim(), value, description);
      if (!result.ok) {
        setError(result.error);
        return;
      }

      setValues(result.values);
      setOpen(false);
    });
  }

  function remove(saved: SavedValue) {
    start(async () => {
      const result = await deleteSavedValue(saved.name);
      if (!result.ok) {
        setError(result.error);
        return;
      }

      setValues(result.values);
    });
  }

  return (
    <section className={styles.panel} aria-labelledby="saved-values-heading">
      <div className={styles.header}>
        <div>
          <h2 id="saved-values-heading" className={styles.heading}>Saved values</h2>
          <p className={styles.blurb}>
            Type something once and use it in any template. A value saved as
            {" "}<code>discordInvite</code> is written <code>{"{{saved.discordInvite}}"}</code>.
          </p>
        </div>

        {canManage ? (
          <button type="button" className="button" onClick={add} disabled={pending}>
            Add a value
          </button>
        ) : null}
      </div>

      {loadError ? (
        <p className={styles.empty} role="status">{loadError}</p>
      ) : values.length === 0 ? (
        <p className={styles.empty}>
          Nothing saved yet. The Discord invite and the venue address are the
          two most templates end up wanting.
        </p>
      ) : (
        <ul className={styles.list}>
          {values.map((saved) => (
            <li key={saved.name} className={styles.row}>
              <div className={styles.about}>
                <code className={styles.token}>{`{{saved.${saved.name}}}`}</code>
                {saved.description ? (
                  <span className={styles.description}>{saved.description}</span>
                ) : null}
              </div>

              {/* Shown rather than hidden behind the edit form. The question
                  somebody has on this screen is almost always "what is it set
                  to", and making them open a dialog to find out is the reason
                  people retype a value into a template instead. */}
              <span className={styles.value} title={saved.value}>{saved.value}</span>

              {canManage ? (
                <span className={styles.actions}>
                  <button
                    type="button"
                    className={styles.action}
                    onClick={() => edit(saved)}
                    disabled={pending}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    className={styles.action}
                    onClick={() => remove(saved)}
                    disabled={pending}
                  >
                    Remove
                  </button>
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      {open ? (
        <div className={styles.form}>
          <label className={styles.field}>
            <span className={styles.label}>Name</span>
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              // The name is the identity, so changing it on an existing value
              // would be deleting one and making another — and every template
              // using the old name would quietly stop resolving.
              disabled={editing !== null || pending}
              placeholder="discordInvite"
              autoFocus
            />
          </label>

          <label className={styles.field}>
            <span className={styles.label}>Value</span>
            <input
              value={value}
              onChange={(event) => setValue(event.target.value)}
              disabled={pending}
              placeholder="https://discord.gg/..."
            />
          </label>

          <label className={styles.field}>
            <span className={styles.label}>What is it? (optional)</span>
            <input
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              disabled={pending}
              placeholder="The server everyone joins."
            />
          </label>

          <div className={styles.formActions}>
            <button type="button" className="button" onClick={save} disabled={pending}>
              {pending ? "Saving…" : "Save"}
            </button>
            <button
              type="button"
              className={styles.action}
              onClick={() => setOpen(false)}
              disabled={pending}
            >
              Cancel
            </button>
          </div>
        </div>
      ) : null}

      {error ? <ErrorToast message={error} /> : null}
    </section>
  );
}
