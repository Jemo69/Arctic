"use client";

import { useState, useTransition } from "react";
import { ErrorToast } from "@/components/ui/error-toast";
import {
  deleteEmailTrigger,
  saveEmailTrigger,
  switchEmailTrigger,
} from "@/app/templates/actions";
import type { EmailTrigger } from "@/app/templates/api";
import type { FormChoice, TemplateRow } from "@/components/templates/types";
import styles from "./automatic-emails.module.css";

/**
 * The emails that send themselves.
 *
 * Here, on the templates screen, rather than on the form screen or a screen of
 * its own. The question an organizer has is "which email goes out when", and
 * that is a sentence about a template — this is the only screen that already
 * answers "what emails can this system send", so it is the one where a missing
 * automation is noticeable. The form screen was the other candidate and splits
 * the concept in half: a status trigger has no form to live on, so "when
 * somebody is accepted" would have to go somewhere else, and nowhere would
 * answer "what sends automatically" in one place.
 *
 * Below the gallery, beside the saved values, and styled as quietly. Both
 * panels are things somebody reads while they are writing an email rather than
 * the thing they came to the screen for.
 *
 * The whole list comes back from every write, so this never has to work out
 * what the list now looks like — which is what stops two people editing at
 * once from leaving one of them looking at a binding that is gone.
 */
export function AutomaticEmails({
  initial,
  eventName,
  eventId,
  statuses,
  forms,
  templates,
  canManage,
  loadError,
}: {
  initial: EmailTrigger[];
  eventName: string | null;
  eventId: string | null;
  statuses: string[];
  forms: FormChoice[];
  templates: TemplateRow[];
  canManage: boolean;

  /** Why the list is empty, when it is empty because something broke. */
  loadError: string | null;
}) {
  const [triggers, setTriggers] = useState(initial);
  const [open, setOpen] = useState(false);
  const [occasion, setOccasion] = useState<EmailTrigger["occasion"]>("status_reached");
  const [status, setStatus] = useState(statuses[0] ?? "");
  const [formId, setFormId] = useState(forms[0]?.id ?? "");
  const [templateKey, setTemplateKey] = useState("");
  const [error, setError] = useState("");
  const [pending, start] = useTransition();

  // Only transactional templates. kind decides the sending lane and the
  // subdomain, so a broadcast template here would send somebody's decision
  // from the announcements domain behind whatever blast is draining — the API
  // refuses it, and offering it would be offering a choice that is about to be
  // refused.
  const sendable = templates.filter((template) => template.kind === "transactional");

  function add() {
    setOccasion("status_reached");
    setStatus(statuses[0] ?? "");
    setFormId(forms[0]?.id ?? "");
    setTemplateKey(sendable[0]?.key ?? "");
    setError("");
    setOpen(true);
  }

  /**
   * Opens the form on an existing binding.
   *
   * The occasion comes back disabled, because changing it would not be editing
   * this automation — it would be making a different one and leaving this one
   * behind. Removing and adding is the honest way to do that, and it is one
   * press each.
   */
  function edit(trigger: EmailTrigger) {
    setOccasion(trigger.occasion);
    setStatus(trigger.status ?? statuses[0] ?? "");
    setFormId(trigger.formId ?? forms[0]?.id ?? "");
    setTemplateKey(trigger.templateKey);
    setError("");
    setOpen(true);
  }

  function save() {
    start(async () => {
      const result = await saveEmailTrigger({
        eventId,
        occasion,
        formId: formId === "" ? null : formId,
        status: status === "" ? null : status,
        templateKey,
      });

      if (!result.ok) {
        setError(result.error);
        return;
      }

      setTriggers(result.triggers);
      setOpen(false);
    });
  }

  function flip(trigger: EmailTrigger) {
    start(async () => {
      const result = await switchEmailTrigger(trigger.id, !trigger.enabled);
      if (!result.ok) {
        setError(result.error);
        return;
      }

      setTriggers(result.triggers);
    });
  }

  function remove(trigger: EmailTrigger) {
    start(async () => {
      const result = await deleteEmailTrigger(trigger.id);
      if (!result.ok) {
        setError(result.error);
        return;
      }

      setTriggers(result.triggers);
    });
  }

  return (
    <section className={styles.panel} aria-labelledby="automatic-emails-heading">
      <div className={styles.header}>
        <div>
          <h2 id="automatic-emails-heading" className={styles.heading}>
            Automatic emails
          </h2>
          <p className={styles.blurb}>
            Send an email the moment something happens, without anybody pressing
            send. Each one goes out once per person, however many times you move
            them.
            {eventName ? <> These are {eventName}&apos;s.</> : null}
          </p>
        </div>

        {canManage && eventId ? (
          <button type="button" className="button" onClick={add} disabled={pending}>
            Add an email
          </button>
        ) : null}
      </div>

      {loadError ? (
        <p className={styles.empty} role="status">{loadError}</p>
      ) : !eventId ? (
        <p className={styles.empty}>
          There is no event yet. An automatic email belongs to a season, so make
          one first.
        </p>
      ) : triggers.length === 0 ? (
        <p className={styles.empty}>
          Nothing sends automatically. An acceptance letter and a
          &ldquo;we have your application&rdquo; are the two most teams set up
          first.
        </p>
      ) : (
        <ul className={styles.list}>
          {triggers.map((trigger) => (
            <li key={trigger.id} className={styles.row}>
              <div className={styles.about}>
                <span className={styles.when}>{describe(trigger)}</span>
                <span className={styles.sent}>
                  {trigger.sent === 0
                    ? "Not sent yet"
                    : `Sent ${trigger.sent} ${trigger.sent === 1 ? "time" : "times"}`}
                </span>
              </div>

              <span className={styles.target}>
                <code className={styles.token}>{trigger.templateKey}</code>

                {/* Loud, because the automation is configured, enabled, and
                    sending nothing. The only other way anybody finds out is an
                    applicant who was accepted and never told. */}
                {trigger.templateMissing ? (
                  <span className={styles.warning}>
                    That template no longer exists, so nothing is being sent.
                  </span>
                ) : null}
              </span>

              <span className={styles.state}>
                {trigger.enabled ? "On" : "Off"}
              </span>

              {canManage ? (
                <span className={styles.actions}>
                  <button
                    type="button"
                    className={styles.action}
                    onClick={() => flip(trigger)}
                    disabled={pending}
                  >
                    {trigger.enabled ? "Turn off" : "Turn on"}
                  </button>
                  <button
                    type="button"
                    className={styles.action}
                    onClick={() => edit(trigger)}
                    disabled={pending}
                  >
                    Change
                  </button>
                  <button
                    type="button"
                    className={styles.action}
                    onClick={() => remove(trigger)}
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
            <span className={styles.label}>When</span>
            <select
              value={occasion}
              onChange={(event) =>
                setOccasion(event.target.value as EmailTrigger["occasion"])
              }
              disabled={pending}
            >
              <option value="status_reached">somebody reaches a status</option>
              <option value="form_submitted">somebody completes a form</option>
            </select>
          </label>

          {occasion === "status_reached" ? (
            <label className={styles.field}>
              <span className={styles.label}>Status</span>
              <select
                value={status}
                onChange={(event) => setStatus(event.target.value)}
                disabled={pending}
              >
                {statuses.map((name) => (
                  <option key={name} value={name}>{statusLabel(name)}</option>
                ))}
              </select>
            </label>
          ) : (
            <label className={styles.field}>
              <span className={styles.label}>Form</span>
              <select
                value={formId}
                onChange={(event) => setFormId(event.target.value)}
                disabled={pending}
              >
                {forms.length === 0 ? (
                  <option value="">No application form on this event</option>
                ) : null}
                {forms.map((form) => (
                  <option key={form.id} value={form.id}>{form.name}</option>
                ))}
              </select>
            </label>
          )}

          <label className={styles.field}>
            <span className={styles.label}>Send</span>
            <select
              value={templateKey}
              onChange={(event) => setTemplateKey(event.target.value)}
              disabled={pending}
            >
              {sendable.length === 0 ? (
                <option value="">No transactional template to send</option>
              ) : null}
              {sendable.map((template) => (
                <option key={template.key} value={template.key}>
                  {template.name || template.key}
                </option>
              ))}
            </select>
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

/**
 * The binding as a sentence.
 *
 * Read out rather than shown as two columns, because the thing somebody is
 * checking on this screen is whether the automation says what they meant —
 * and "when somebody is accepted" is how they would have said it.
 */
function describe(trigger: EmailTrigger): string {
  if (trigger.occasion === "form_submitted") {
    // The form may have been removed since the binding was made, and the row
    // still has to read as something. 0049 keeps the binding rather than
    // deleting it, so this is reachable.
    return `When somebody completes ${trigger.formName ?? "a form that has been removed"}`;
  }

  return `When somebody reaches ${statusLabel(trigger.status ?? "")}`;
}

/**
 * A stored status as a person reads it.
 *
 * Derived from the string rather than a map, so a status added to the
 * lifecycle reads correctly here without anybody remembering this file. The
 * API decides which statuses can be bound; this only has to spell them.
 */
function statusLabel(status: string): string {
  return status.replace(/_/g, " ");
}
