-- "When somebody is accepted, send them this email."
--
-- Every email this system sends is one of three things today: a magic link, an
-- organizer welcome, or a broadcast somebody drafted, approved and pressed
-- send on. The two an organizer actually asks for are neither -- they are
-- consequences. Accepting four hundred people and then drafting a campaign
-- aimed at `accepted` is the manual version, and it is wrong in the way manual
-- versions are wrong: it goes out once, in a batch, hours or days later, and
-- the four people decided on Thursday morning are either mailed twice or not
-- at all depending on which segment the next campaign happens to resolve.
--
-- Two tables, and the split is the whole design. notify.email_triggers is
-- configuration -- a sentence an organizer wrote once, that they can read back
-- and turn off. notify.email_trigger_sends is the ledger of what has actually
-- happened, and it exists because "send exactly once" is a uniqueness
-- constraint or it is a wish.

-- ----------------------------------------------------------- the sentence ---
-- One row per "when X happens, send Y".
CREATE TABLE notify.email_triggers (
    id       uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Per season, and NOT NULL.
    --
    -- "When we accept somebody, send them this" is a sentence about this
    -- year's event. Next year's team writes next year's copy, and a binding
    -- that carried over would mail 2028's acceptances with 2027's wording,
    -- naming 2027's dates, from a template nobody re-read -- and the first
    -- person to notice would be an applicant. Scoping it to the event means a
    -- new season starts with nothing switched on, which is the right default
    -- for something that sends mail without anybody watching.
    --
    -- ON DELETE CASCADE, though nothing deletes an event: IEventStore says so
    -- in as many words and explains why. The clause is here so the row cannot
    -- outlive the thing it is scoped to if that ever stops being true.
    event_id uuid NOT NULL REFERENCES applications.events (id) ON DELETE CASCADE,

    -- What has to happen. Two occasions, because these are the two an
    -- organizer asks for and each one has a different thing to point at.
    occasion text NOT NULL CHECK (occasion IN ('form_submitted', 'status_reached')),

    -- Which form, for 'form_submitted'.
    --
    -- ON DELETE CASCADE rather than 0047's SET NULL, and the difference is
    -- deliberate. There, a template that loses its form is still a template --
    -- the wording survives and the {{form.*}} placeholders simply stop
    -- resolving. Here, a trigger that loses its form is a sentence with no
    -- subject: "when somebody submits ... send this". There is nothing left to
    -- fire on, and keeping the row would mean a screen listing an automation
    -- that can never run again.
    form_id  uuid REFERENCES applications.forms (id) ON DELETE CASCADE,

    -- Which status, for 'status_reached'. The stored spelling, matching
    -- applications.applications.status, because two spellings of one status is
    -- one of them being wrong somewhere.
    --
    -- Every status except 'incomplete'. That one is where a row starts rather
    -- than somewhere it is moved to -- the form autosaves, so an application
    -- is created incomplete and nothing ever transitions into it -- so a
    -- trigger on it could never fire, and offering it would be offering an
    -- automation that silently does nothing.
    status   text CHECK (status IN ('submitted', 'under_review', 'accepted',
                                    'rejected', 'waitlisted', 'confirmed',
                                    'declined', 'expired', 'checked_in',
                                    'withdrawn')),

    -- Which email, by key rather than by id, and this is the one choice in the
    -- file worth arguing with.
    --
    -- 0017 made templates copy-on-write: editing one retires the current row
    -- and inserts a new one, so notify.campaigns.template_id names the exact
    -- version a campaign was approved against. That is right for a campaign --
    -- an approver signed off on wording, and a broadcast cannot be recalled --
    -- and it is wrong for an automation. A trigger pinned to an id would go on
    -- sending the version that was live the day somebody set it up, for ever,
    -- including after the typo in it was fixed. The alternative reading, that
    -- a superseded id stops the trigger firing, is worse: correcting a comma
    -- would silently stop acceptance emails.
    --
    -- So the key, which is the stable name, and the trigger sends whatever is
    -- live under it. The cost is that this cannot be a foreign key: 0017
    -- dropped templates_key_key precisely because a key is unique only among
    -- live rows, and there is no way to express "references whichever row is
    -- live for this key" as a constraint. That is a referential invariant
    -- deliberately not enforced here, which is worth saying out loud in a file
    -- whose neighbours argue the opposite -- the two shapes available are a
    -- foreign key to the wrong thing and no foreign key, and no foreign key is
    -- the honest one.
    --
    -- A key with no live row behind it therefore does not dangle so much as go
    -- quiet: the fire path finds nothing, logs loudly, and sends nothing. The
    -- console reads the same emptiness and says the template is gone. Deleting
    -- a template does not silently delete somebody's automation, and it does
    -- not refuse because of an automation nobody remembers setting up.
    template_key text NOT NULL CHECK (template_key ~ '^[a-z0-9][a-z0-9_-]*$'
                                      AND length(template_key) <= 64),

    -- The switch. Separate from deleting the row because the week decisions go
    -- out is exactly the week somebody needs to stop the mail for an hour
    -- while they fix the wording, and "delete it and set it up again
    -- afterwards" is how a binding comes back pointing at the wrong template.
    enabled  boolean NOT NULL DEFAULT true,

    -- Who to ask about it. Nullable and ON DELETE SET NULL for the reason 0045
    -- gives: deleting the person must not delete the automation, and a name
    -- attached to a row nobody recorded is worse than an empty column.
    created_by uuid REFERENCES identity.people (id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    -- The occasion decides which of the two targets is filled in, and the
    -- other has to be empty.
    --
    -- Both halves matter. Without the NOT NULL half a 'status_reached' row
    -- with no status is a trigger that matches nothing and reads, on a screen,
    -- as an automation that is set up. Without the IS NULL half a row could
    -- carry both, and then the question "what does this fire on" has two
    -- answers and the code picks one -- which is exactly the kind of thing
    -- that gets picked differently by the list query and the fire query.
    CONSTRAINT email_triggers_target_matches_occasion CHECK (
        (occasion = 'form_submitted' AND form_id IS NOT NULL AND status IS NULL)
     OR (occasion = 'status_reached' AND status IS NOT NULL AND form_id IS NULL))
);

-- One trigger per occasion per season. Duplicates are not allowed, and that is
-- a decision rather than an oversight.
--
-- Two rows both saying "when somebody is accepted" means two emails, and
-- nobody has ever wanted two emails -- somebody who wants to say two things
-- says them in one template, which is also the only version an applicant
-- reads as deliberate. Allowing duplicates would additionally make the
-- exactly-once story per-binding instead of per-occurrence: four hundred
-- people would get exactly two acceptance letters, correctly, and the
-- constraint below would be satisfied the whole time.
--
-- Partial rather than one index over both columns, because a unique index on
-- (event_id, form_id, status) would let two 'status_reached' rows coexist as
-- long as nothing collided on the null form_id -- which is how nulls work in a
-- unique index, and not what anybody reading the line would expect.
CREATE UNIQUE INDEX email_triggers_one_per_form
    ON notify.email_triggers (event_id, form_id)
    WHERE occasion = 'form_submitted';

CREATE UNIQUE INDEX email_triggers_one_per_status
    ON notify.email_triggers (event_id, status)
    WHERE occasion = 'status_reached';

-- The fire path's index: given an application, find the trigger for the status
-- it just reached. Read on every status change an organizer makes, which on
-- the evening decisions go out is four hundred times in a few minutes.
CREATE INDEX email_triggers_event_idx
    ON notify.email_triggers (event_id, occasion) WHERE enabled;

-- Touched on every write so the console can say when a binding last changed.
-- A trigger rather than the caller, for the reason 0006 gives: the one write
-- that forgets is the hand-written one during the event, and that is exactly
-- the write somebody later needs the date of.
CREATE TRIGGER email_triggers_touch_updated_at
    BEFORE INSERT OR UPDATE ON notify.email_triggers
    FOR EACH ROW EXECUTE FUNCTION applications.touch_updated_at();

-- ------------------------------------------------------------- the ledger ---
-- What has already been sent, and the whole of why it cannot be sent twice.
--
-- The failures this exists for are all ordinary. An organizer double-taps
-- Accept. The request times out at the browser and the console retries it. A
-- reviewer accepts somebody, realises it was the wrong row, moves them to
-- expired and back -- which StatusTransition explicitly permits, as
-- reinstatement. Somebody runs the decisions through twice because the first
-- run looked like it had stalled. None of those is a bug anywhere; all four
-- send a second acceptance letter if "have we already done this" is a SELECT
-- followed by an INSERT.
--
-- So it is a primary key instead. Two requests racing arrive at the same
-- (trigger, application) pair, one inserts and the other gets 23505, and the
-- loser rolls back the message it had queued in the same transaction. There is
-- no window: the constraint is checked by the thing that holds the row, not by
-- code that ran a moment earlier and has been true ever since.
--
-- Here rather than derived from notify.messages, which is the version that
-- looks like it would work. A message carries a campaign and an address, and
-- asking "did we already mail this person about being accepted" of that table
-- means matching on rendered content or on a campaign named after a template
-- key -- both of which are string comparisons standing in for a fact, and
-- neither of which survives somebody editing the template. The ledger records
-- the fact.
CREATE TABLE notify.email_trigger_sends (
    trigger_id uuid NOT NULL
               REFERENCES notify.email_triggers (id) ON DELETE CASCADE,

    -- The application the occasion happened to, and the whole of what "once"
    -- is counted per.
    --
    -- Not the status_history row, which is the other obvious candidate and is
    -- wrong in a way worth recording. There is one history row per transition,
    -- so keying on it would make accepted -> expired -> accepted two
    -- occurrences and send two letters -- which is precisely the case the
    -- ledger was written for. The trigger already names one status, so one row
    -- per (trigger, application) means "this person has been told about
    -- reaching this status once, ever", which is the sentence an applicant
    -- would use.
    --
    -- Nothing deletes an application. CASCADE is here for the same reason the
    -- one on event_id is.
    application_id uuid NOT NULL
                   REFERENCES applications.applications (id) ON DELETE CASCADE,

    -- What was queued, so "did they get it" is answerable from here rather
    -- than by searching the queue for an address.
    --
    -- NOT NULL, which makes the ledger mean exactly one thing: a message
    -- exists for this occurrence. A nullable column would let a row mean
    -- either "sent" or "considered and skipped", and the fire path would then
    -- have to know which skips are worth remembering -- a suppressed address
    -- today, a missing template tomorrow -- with nothing to stop the two
    -- drifting apart.
    --
    -- ON DELETE SET NULL is deliberately not available here, and that is the
    -- point: the claim has to outlive the message. If deleting a message
    -- released the claim, deleting one would let the email send again.
    message_id uuid NOT NULL REFERENCES notify.messages (id),

    queued_at  timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (trigger_id, application_id)
);

-- "What has this automation sent, and when." The primary key already serves
-- lookups by trigger; this is for the screen that asks in date order.
CREATE INDEX email_trigger_sends_recent_idx
    ON notify.email_trigger_sends (trigger_id, queued_at DESC);
