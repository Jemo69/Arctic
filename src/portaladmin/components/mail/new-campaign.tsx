"use client";

import Link from "next/link";
import { useActionState, useEffect, useRef, useState } from "react";
import { ArrowRight02Icon, Cancel01Icon, Layout01Icon, Mail01Icon } from "@hugeicons/core-free-icons";
import { ErrorToast } from "@/components/ui/error-toast";
import { Icon } from "@/components/ui/icon";
import { Select } from "@/components/ui/select";
import { emailDocument } from "@/components/templates/email-preview";
import type { FormState } from "@/app/mail/actions";
import type { TemplateRow } from "@/components/templates/types";
import styles from "./new-campaign.module.css";

function TemplatePreview({ template }: { template: TemplateRow | undefined }) {
  const paper = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0.5);

  useEffect(() => {
    if (!paper.current) return;
    const observer = new ResizeObserver(([entry]) => setScale(entry.contentRect.width / 600));
    observer.observe(paper.current);
    return () => observer.disconnect();
  }, []);

  return <section className={styles.preview} aria-label="Selected template preview">
    <div className={styles.previewHeading}><Icon icon={Mail01Icon} size={16} /><span>Template preview</span></div>
    <div ref={paper} className={styles.paper}>
      {template?.previewHtml ? <iframe key={template.key} title={`${template.name || template.key} preview`} tabIndex={-1} sandbox="" referrerPolicy="no-referrer" srcDoc={emailDocument(template.previewHtml)} style={{ transform: `scale(${scale})`, height: `${260 / scale}px` }} />
        : <div className={styles.previewEmpty}><Icon icon={Layout01Icon} size={30} /><strong>{template ? "Preview unavailable" : "Your email starts here"}</strong><p>{template ? "You can review this email during setup." : "Choose a template to see your message."}</p></div>}
    </div>
    {template ? <div className={styles.previewCaption}><span>Subject</span><p>{template.subject || "No subject yet"}</p></div> : <p className={styles.previewNote}>A saved template, ready for your next campaign.</p>}
  </section>;
}

export function NewCampaign({ templates, templatesError, create, onClose }: {
  templates: TemplateRow[];
  templatesError: string | null;
  create: (state: FormState, form: FormData) => Promise<FormState>;
  onClose: () => void;
}) {
  const [state, action, pending] = useActionState(create, {});
  const [templateKey, setTemplateKey] = useState(templates.length === 1 ? templates[0].key : "");
  const template = templates.find(item => item.key === templateKey);
  const dialog = useRef<HTMLDialogElement>(null);
  const nameInput = useRef<HTMLInputElement>(null);
  const unavailable = templatesError !== null || !template;

  useEffect(() => {
    dialog.current?.showModal();
    nameInput.current?.focus();
  }, []);

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="new-campaign-title" aria-describedby="new-campaign-description" onClose={onClose}
    onCancel={event => { if (pending) event.preventDefault(); }}>
    <header className={styles.header}>
      <div><h2 id="new-campaign-title">New campaign</h2><p id="new-campaign-description">Start with a name and the email you want to send.</p></div>
      <button type="button" className={styles.close} aria-label="Close new campaign" disabled={pending} onClick={() => dialog.current?.close()}><Icon icon={Cancel01Icon} size={20} /></button>
    </header>
    <form action={action} className={styles.form}>
      <div className={styles.body}>
        <fieldset disabled={pending} className={styles.fields}>
          <div className={styles.field}>
            <label htmlFor="new-campaign-name">Campaign name</label>
            <input ref={nameInput} id="new-campaign-name" name="name" required maxLength={200} autoComplete="off" placeholder="e.g. Welcome to MorganHacks" aria-describedby="campaign-name-hint" />
            <p id="campaign-name-hint" className={styles.hint}>A name for your team. Recipients won’t see it.</p>
          </div>
          <div className={styles.field}>
            <label htmlFor="new-campaign-template">Email template</label>
            {templatesError ? <p className={styles.unavailable} role="alert">Templates could not be loaded. Close this dialog and refresh to try again.</p>
              : templates.length === 0 ? <div className={styles.noTemplates}><Icon icon={Layout01Icon} size={22} /><strong>Create your first template</strong><p>You’ll use it as the starting point for your campaign.</p><Link href="/templates/new">Create a template <Icon icon={ArrowRight02Icon} size={15} /></Link></div>
                : <Select id="new-campaign-template" name="templateKey" required value={templateKey} onChange={event => setTemplateKey(event.target.value)}><option value="" disabled>Choose a template</option>{templates.map(item => <option key={item.key} value={item.key}>{item.name || item.key}</option>)}</Select>}
            {templates.length > 0 && !templatesError ? <Link href="/templates/new" className={styles.templateLink}><Icon icon={Layout01Icon} size={15} />Create a new template<Icon icon={ArrowRight02Icon} size={14} /></Link> : null}
          </div>
          <p className={styles.nextStep}>Next, personalize your sender details and choose your recipients.</p>
        </fieldset>
        <TemplatePreview template={template} />
      </div>
      <footer className={styles.footer}>
        <span>Your campaign starts as a draft.</span>
        <div><button type="button" className={styles.cancel} disabled={pending} onClick={() => dialog.current?.close()}>Cancel</button><button type="submit" className={styles.continue} disabled={pending || unavailable}>{pending ? "Creating…" : "Continue"}<Icon icon={ArrowRight02Icon} size={16} /></button></div>
      </footer>
      {state.error ? <ErrorToast message={state.error} revision={state} /> : null}
    </form>
  </dialog>;
}
