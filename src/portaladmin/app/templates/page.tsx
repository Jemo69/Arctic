import { AutomaticEmails } from "@/components/templates/automatic-emails";
import { SavedValues } from "@/components/templates/saved-values";
import { TemplatesTable } from "@/components/templates/templates-table";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { readEmailTriggers, readSavedValues, readTemplates } from "./api";

/**
 * Every email this system can send.
 *
 * Until this screen existed a template could only be written by hand in SQL,
 * which is why no campaign has ever gone out. The list is the whole of it:
 * what each one is called, which lane it sends down, and one press to open it.
 */
export default async function Templates() {
  // All three at once. None needs another's answer, and the saved values and
  // the automations are small lists on the same screen — awaiting them in
  // sequence would make the page arrive later for nothing.
  const [{ person, data: templates }, saved, automatic] = await Promise.all([
    readPageData(() => readTemplates(true, true)),
    readSavedValues(),
    readEmailTriggers(),
  ]);

  if (!templates.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Templates</h1>
        <div className="empty">
          {templates.status === 403 ? (
            <>
              You do not have permission to view templates. Ask an admin.
            </>
          ) : (
            templates.error
          )}
        </div>
      </Shell>
    );
  }

  // Cosmetic. The API refuses the write whether or not this link rendered, so
  // hiding it is a courtesy to somebody who cannot use it rather than a
  // control over anything.
  const canManage = person.permissions.has("email.manage_templates");
  const canDelete = person.permissions.has("email.delete_templates");

  return (
    <Shell personId={person.personId} templateCount={templates.items.length}>
      <TemplatesTable key={person.personId} templates={templates.items} personId={person.personId}
        initialHiddenKeys={templates.hiddenKeys}
        canManage={canManage} canDelete={canDelete} />

      {/* Above the saved values, because this is the panel that sends mail and
          that one is a reference. Somebody scrolling past the gallery is more
          likely to be asking "does an acceptance letter go out on its own"
          than "what is the venue set to". */}
      <AutomaticEmails
        initial={automatic.ok ? automatic.triggers : []}
        eventId={automatic.ok ? automatic.eventId : null}
        eventName={automatic.ok ? automatic.eventName : null}
        statuses={automatic.ok ? automatic.statuses : []}
        forms={automatic.ok ? automatic.forms : []}
        templates={templates.items}
        canManage={canManage}
        loadError={automatic.ok ? null : automatic.error}
      />

      <SavedValues
        initial={saved.ok ? saved.values : []}
        canManage={canManage}
        loadError={saved.ok ? null : saved.error}
      />
    </Shell>
  );
}
