# Template variables, and mailing people by what they answered

**Status: built.** Written 2026-09-30 against `60394fb`, brought up to date
2026-10-01.

Everything below shipped, across #140, #142, #143, #146, #147, #148, #153,
#154 and #156. The plan is kept rather than deleted because the reasoning in it
is the reasoning in the code, and three things turned out differently once they
met the schema — each marked **changed in the build** where it happens.

Two features that were one idea until they were pulled apart:

1. **More variables.** Today a template fills nine things, all columns of
   `applications.applications`. Add the event, one form, general constants, and
   a place to keep values somebody types in by hand.
2. **Answer-based audiences.** Mail the people who gave a particular answer —
   "everyone who said they want a hardware track" — and ask them about it.

The second one is *not* a variable. Nothing about an answer is copied into the
email; the answer only decides who receives it. That distinction is what makes
it cheap, and it is why the privacy gate an earlier draft of this plan proposed
is not needed.

---

## What exists, in one paragraph

`ApplicantColumns.Mergeable` declares nine columns of
`applications.applications`; `MergeFields.NameFor` derives `first_name` →
`firstName`, so a name can never drift from its column. `MergeFields.Values`
turns one `SegmentMember` into a dictionary, and `TemplateRenderer.Render`
substitutes at **queue time**, freezing the result into
`notify.messages.rendered_subject/_body_html/_body_text`. A value it cannot
fill is left standing as literal `{{firstName}}`, and two checks —
`CampaignEndpoints.Unfillable` (is this name known at all?) and `Covered` /
`Missing` (does every individual recipient have one?) — refuse the send before
anybody receives a brace. The editor already has a real picker: a `{{`
combobox, CodeMirror completion, and a searchable popover, all fed by
`GET /admin/templates/placeholders`.

Read `MergeFields.cs` and `ApplicantColumns.cs` before touching any of this.
The reasoning in their comments is the design.

---

# Part 1 — More variables

## Three things to fix first

### 1. The renderer does not understand a dot — and the editor does

This is a live bug, not a new requirement.

| Where | Pattern | File |
|---|---|---|
| Renderer | `\{\{\s*(\w+)\s*\}\}` | `EmailTemplate.cs:66` |
| Editor, "Unknown" panel | `\{\{\s*([\w.]+)\s*\}\}` | `types.ts:152` |
| Editor, insert + completion | `^[\w.]*$` | `placeholders.ts:20`, `code-editor.tsx:82` |

So `{{event.name}}` **is** a placeholder to the editor and **is not** one to the
renderer. It is invisible to `PlaceholdersIn`, therefore invisible to
`Unfillable` and `Covered`, therefore ships to four hundred people as literal
text with every safety check green. `placeholders.ts:12-14` already warns that
the two character sets have to agree.

Widening `EmailTemplate.cs:66` to `[\w.]+` is a one-character fix and is the
precondition for every namespace below.

**It is not free.** A template already containing a dotted name starts being
*detected* after this change, and a campaign that sends today would begin
refusing at `Unfillable`. That is the check doing its job, but find it
deliberately rather than on send day:

```sql
SELECT key, 'subject' AS part FROM notify.templates WHERE subject   ~ '\{\{\s*\w+\.[\w.]+\s*\}\}'
UNION ALL
SELECT key, 'body'         FROM notify.templates WHERE body_html ~ '\{\{\s*\w+\.[\w.]+\s*\}\}';
```

### 2. Two placeholder syntaxes exist, and the second one is load-bearing

`{$unsubscribe_link}` (`EmailUnsubscribe.cs:5`) is replaced **at send time**, on
the claimed message, because the URL is per-address and minted late.

Do not unify these. The split is real and worth naming in the catalogue:

- **Queue-time `{{...}}`** — values known when the message is written. Frozen
  into `rendered_*`, so a retry cannot render differently from the first attempt.
- **Send-time `{$...}`** — values that must not be frozen, because they are
  per-delivery.

Everything proposed below is queue-time.

### 3. The catalogue does not contain the transactional names

`{{link}}` and `{{console}}` are supplied by `QueuedEmailSender.cs:54-90` and
are not in `MergeFields.All`. The seeded `magic_link` template lists `link`
under `Detail(...).placeholders` while the campaign endpoint calls the same name
unknown. Fold them in as part of this work.

## The grammar

Existing applicant names stay **flat and unchanged**: `{{firstName}}`,
`{{email}}`, and the other seven. Sixteen templates reference them; renaming
them to `applicant.firstName` would be a migration of live content to buy
consistency nobody asked for.

Everything new is **dotted**, one namespace segment then a name:

| Namespace | Example | Scope |
|---|---|---|
| `event.` | `{{event.name}}`, `{{event.startsAt}}` | the current event, always |
| `form.` | `{{form.link}}`, `{{form.closesAt}}` | the one form this template is bound to |
| `link.` | `{{link.portal}}`, `{{link.signIn}}` | our own addresses, per environment |
| `saved.` | `{{saved.discordInvite}}` | values an organizer typed in to reuse |

The names are chosen to be read by somebody writing an email, not by somebody
maintaining the renderer. `link.portal` is a link to the portal; `saved.` is a
value you saved once. An earlier draft called these `system.` and `custom.`,
which are both words from this side of the screen.

The same rule applies inside a namespace: it is `{{form.link}}`, not
`{{form.url}}`.

Namespacing is also what makes "make sure it doesn't clash" structural rather
than a runtime check. `saved.` can never collide with a derived name because the
prefix is reserved, and saved-versus-saved is a unique index. The only rule to
enforce in code is that a saved name may not itself contain a dot.

## The four namespaces

### `event.` — implicit, always the current event

Source is `applications.events` (`0004_applications.sql`): `slug`, `name`,
`starts_at`, `ends_at`, `registration_opens_at`, `registration_closes_at`,
`capacity`. Declared the same way applicant columns are — a list naming each
column, its kind, and the sentence the editor shows — so the divergence test in
`MergeFieldTests` can cover it too.

**Dates are the reason this was blocked before and the reason it is unblocked
now.** PR #71 withheld `rsvp_deadline` explicitly because *"nothing here knows
the event's timezone, so a midnight deadline rendered in UTC lands on the wrong
calendar day for exactly the people it matters to."* `EventZone`
(`MorganHacks.Applications/Domain/EventZone.cs:29`, `America/New_York` through
`TimeZoneInfo`) now exists, and `ApplicantView.cs:194` already renders through
it. A new `ColumnKind.Timestamp` renders through `EventZone` and carries the
zone abbreviation, matching what the console and the public form show.

While this is open, revisit `rsvp_deadline` on the applicant side — it was
withheld for a reason that no longer holds.

### `form.` — one form, chosen on the template

The template binds to a single form. One nullable column is enough:

```sql
ALTER TABLE notify.templates ADD COLUMN form_id uuid REFERENCES applications.forms (id);
```

Fields: `{{form.link}}` (built from `FORMS_BASE_URL` + the form's code),
`{{form.name}}`, `{{form.closesAt}}`.

> **Changed in the build.** There is no `{{form.opensAt}}`.
> `applications.forms` has no such column — a form is reachable from the moment
> it is published, and closing is the only date it carries. The plan invented a
> field by symmetry with the event's dates.

The editor gets one picker — "which form is this email about?" — and the `form.`
group only appears in the placeholder menu once a form is chosen. A template
with no form bound simply has no `form.` variables, and using one is unfillable,
which the existing check already reports.

**The failure to design against** is a bound form being deleted or unpublished;
`0038_form_removal.sql` exists, so this is real. `ON DELETE SET NULL` on the
column plus treating it as unfillable is right — a standing `{{form.link}}`
blocks the send rather than mailing a dead link.

### `link.` — the three origins, and the two names that already exist

Deliberately small. Two things belong here and nothing else does yet.

**The origins**: `{{link.portal}}`, `{{link.forms}}`,
`{{link.console}}`. Atlas already holds all three as configuration —
`AuthEndpoints.cs:54-73` reads `PublicBaseUrl`, `FormsBaseUrl` and
`ConsoleBaseUrl` to build sign-in links, with localhost fallbacks. Templates
have no way to reach them, so an author writing a link to the portal types the
production URL into a database row.

That is the failure worth fixing: **a hardcoded origin in a template links to
production from staging.** Preview looks right, the test send looks right, and
the only way to notice is to click through and find yourself on the live site.
It cannot be solved with a `saved.` value either, because saved values are rows
and the two environments have separate databases — somebody would have to set
the same name to different values twice and keep them straight. The environment
already knows.

`PUBLIC_BASE_URL` is unset on production today — see the
[production runbook](../runbooks/first-production-deploy.md) — so
`{{link.portal}}` is unfillable there until that is fixed.

**The two that already exist**: `{{link}}` and `{{console}}`, supplied by
`QueuedEmailSender.cs:54-90`. They work today and are simply absent from
`MergeFields.All`, which is why the picker cannot offer them and why the
campaign endpoint calls them unknown while `Detail(...).placeholders` lists them
as known. They move here as `{{link.signIn}}` and `{{link.console}}`, declared
with an audience so the catalogue can say *transactional only*. Keep the bare
names working as aliases — `magic_link` is seeded by a migration and rewriting
seeded content is not worth it.

The bare `{{link}}` sitting beside a `link.` namespace is a happy accident
rather than a problem: the old name is the sign-in link, and `{{link.signIn}}`
is what it was always called in prose.

Nothing else. A current year and an organisation name were in an earlier draft
and are cut: both are one word somebody can type, and neither changes in a way
a variable protects against.

### `saved.` — values an organizer types in once

The Discord invite, the venue address, the wifi password on the day.

```sql
CREATE TABLE notify.saved_values (
    name        text PRIMARY KEY CHECK (name ~ '^[a-z][a-zA-Z0-9]*$'),
    value       text NOT NULL,
    description text,
    updated_by  uuid REFERENCES identity.people (id),
    updated_at  timestamptz NOT NULL DEFAULT now()
);
```

The `CHECK` is what makes a clash impossible: no dots, so a saved name can never
impersonate a namespace. Behind `email.manage_templates`, edited on a small
screen under Templates, and included in the catalogue endpoint so the picker
offers them with their description like everything else.

The description field is worth insisting on. `{{saved.discordInvite}}` read in
somebody else's draft six months later is only obvious if the picker can say
what it is and when it was last changed.

Worth deciding: global, or per-event? Global is simpler and probably right for
one event a year — but `{{saved.discordInvite}}` is exactly the value that
changes each season and, if global, changes silently underneath last year's
templates.

## How the values get in

One resolver composing sources, merged in order, per-recipient last:

```
system  →  event  →  form  →  custom  →  applicant
```

Everything except applicant resolves once per send. Merging per-recipient last
means a personal value wins a collision, which the namespaces should make
impossible anyway.

**Where this code lives matters.** `TemplateRenderer` is in
`MorganHacks.Lark.Data`, which has **no project references at all** — it is a
leaf, deliberately. `EventZone`, `ApplicantColumns` and `MergeFields` are in
atlas, and rendering already happens in atlas at queue time, so the resolver
belongs there beside `MergeFields`. Do not pull the applications domain into
`Lark.Data` to reach `EventZone`; that inverts a dependency that is currently
clean.

`MergeFields.Fillable(segment)` is the precedent for context narrowing —
`Segment.Addresses` already gets `{{email}}` and nothing else. Extend the same
function: a typed address list can fill `link.`, `saved.`, `event.` and
`form.`, but no applicant values.

---

# Part 2 — Mailing people by what they answered

## What this is

A new segment kind. `Segment` already has three
(`Segments/Segment.cs:49-88`): `InStatus`, `FormRespondents(formId)`,
`Addresses`. This adds a fourth — respondents to a form **whose answer to a
chosen question matches a chosen value**.

Nothing renders. The answer is a `WHERE` clause and never reaches
`notify.messages`, so none of the PII reasoning that withheld `dietary_needs`
from the merge catalogue applies here. What gets stored is the *criterion*, in
`notify.campaigns.segment` jsonb, next to the criteria already stored there.

## The storage, and one wrinkle

There are two places an answer can live:

| Table | Column | Indexed for this? |
|---|---|---|
| `applications.applications` | `responses` jsonb | **Yes** — `applications_responses_gin` (`0004:143`) |
| `applications.form_submissions` | `answers` jsonb | **No index** |

The application side was designed for exactly this. `0004:143` says so:
*"So answers living in `responses` stay filterable without promoting them."*

The survey side was designed the other way. `0019`'s own comment says
*"a survey answer is not filtered, exported at check-in or read on a badge"* —
which was true when written and is what this feature changes. It needs its own
GIN index:

```sql
CREATE INDEX form_submissions_answers_gin
    ON applications.form_submissions USING gin (answers);
```

Worth saying in the migration that it contradicts 0019's comment on purpose, so
the next reader sees a decision rather than an oversight.

**Anonymous answers cannot be mailed.** `0027` made `person_id` nullable so
answers with nobody attached could be kept. Those rows have no person and no
address, so they are invisible to this segment by construction — the resolver
must filter `person_id IS NOT NULL`. The count shown in the console should say
so, or an organizer will read "40 responses" on the form screen and "31
recipients" here and assume something is broken.

## Matching

Answers are keyed by `FormField.Key` and their jsonb values are typed by
question kind. Start with equality and membership, which covers the real ask:

- choice / multiple-choice → *is* one of the selected values
- short text → exact match, case-insensitive
- boolean → yes / no
- number → equals, and possibly a range later

Leave long free-text out of the first cut. Substring matching over free text is
a search feature wearing a segment's clothes, it cannot use the GIN index the
same way, and "everyone who typed *hardware* somewhere in a paragraph" is a
much vaguer audience than it sounds.

The console picks the form, then the question, then the value — each step
narrowing the next, with the resolved recipient count shown before anything can
be sent. `/mail/[id]` already shows a resolved count and a sample of real
addresses before a send, which is where this lands.

## One thing to decide

**Which form version's questions does the picker offer?** A form is versioned,
and a question that existed in version 2 may be gone in version 5 while answers
to it still sit in older submissions. Offering the current draft's questions is
the obvious implementation and will quietly exclude people who answered a
question that has since been removed. Offering every question that has ever
existed is more honest and a longer list. I would start with the current
published version and say so on the screen.

---

## Order to build it

1. **Widen the renderer regex to `[\w.]+`**, run the audit query, add a test
   that the client and server patterns accept the same strings. Nothing
   user-visible; everything else depends on it.
2. **Namespaces in the catalogue** — a `Namespace` on the merge-field record,
   the endpoint grouping by it, the picker showing groups. Ship with `link.`
   only: three origins already in configuration, plus declaring the two
   transactional names that exist but are undeclared. No schema change.
3. **`event.`**, including `ColumnKind.Timestamp` through `EventZone`, and the
   divergence test extended to `applications.events`.
4. **`saved.`** — one table, one screen, one permission. Self-contained.
5. **`form.`** — one column, one picker, unfillable-on-deleted-form.
6. **Answer segments** — the GIN index, the fourth `Segment` kind, the
   three-step picker. Independent of 2–5; could go earlier if it is the more
   useful half.

Steps 1 and 2 are worth doing even if everything else waits: the first closes a
hole where a broken placeholder ships silently, and the second is what lets the
editor show more than a flat list of nine.

---

## What the build changed

Three things, beyond the `form.opensAt` note above.

**The naming.** `system.` became `link.` and `custom.` became `saved.`, because
the first pair are words from the maintainer's side of the screen and an
organizer writing an email is looking for a link to the portal. `{{form.url}}`
became `{{form.link}}` for the same reason. The sentence above about "an
earlier draft" is that change, recorded where it happened.

**Form answers came back.** The plan proposed an `answer.` namespace, it was
dropped in favour of answer-*segments*, and then the questions were wanted as
placeholders after all. They are `{{form.answer.<key>}}` — three segments, not
`{{form.<key>}}`, because a question keyed `link` or `name` would otherwise
collide with the form's own fields. Shipped **without** the per-question
opt-in the plan argued for, which was a deliberate call: a rendered answer is
frozen into `notify.messages`, a schema with different readers and retention
than `applications.*`, and that trade is recorded in `FormAnswers`' remarks.

**One form per template, not several.** The plan's slot design
(`{{form.apply.link}}` against `{{form.feedback.link}}`) asked an author to
invent a name before they could write a sentence. One form means one nullable
column and no grammar to learn.

## Decisions that were open, and how they went

- **Saved values: global or per-event?** → **Global.** One event a year, and
  per-event would have meant a composite key for a problem nobody has yet. The
  risk it leaves is real and unaddressed: updating `{{saved.discordInvite}}`
  changes what last season's templates say.
- **Question list for answer segments** → **the published version's**, with the
  version number on screen. Somebody who answered a question that has since
  been removed is therefore not reachable by it.
- **Free-text matching** → **excluded.** Substring search over a paragraph is a
  search feature wearing a segment's clothes, and it cannot use the GIN index
  the way equality can.
- **`rsvp_deadline`** → **still withheld, and still worth revisiting.** It was
  kept out of the applicant catalogue because nothing knew the event's
  timezone. `EventZone` exists now and `{{event.*}}` dates render through it,
  so the reason is gone and nobody has acted on it. The only item on this list
  that is still a question.
