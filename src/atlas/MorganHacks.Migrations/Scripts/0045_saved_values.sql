-- Values an organizer types once and reuses in any template.
--
-- The Discord invite, the venue address, the wifi password on the day. Each
-- one is a sentence that appears in several emails and changes in one place
-- when it changes at all -- which is the case a template variable exists for,
-- and the case the catalogue could not cover because every name in it was
-- derived from a column of applications.applications.
--
-- ---------------------------------------------------------- the name rule ---
--
-- No dots. That is what the CHECK is for, and it is the whole collision story:
-- these names are offered to an author as {{saved.<name>}}, and a name that
-- could itself contain a dot could impersonate a namespace -- somebody saving
-- a value called "portal" under the name "link.portal" would shadow a real
-- one. Forbidding the character means the two sets cannot overlap however
-- either grows, rather than a check somewhere that has to be remembered.
--
-- camelCase to match every other placeholder an author types. {{firstName}}
-- and {{saved.discordInvite}} should not be two conventions in one editor.
CREATE TABLE notify.saved_values (
    name        text PRIMARY KEY
                CHECK (name ~ '^[a-z][a-zA-Z0-9]*$' AND length(name) <= 40),

    -- What gets substituted. Free text on purpose: a URL, an address, a room
    -- number. Bounded because it lands in an email rather than on a page, and
    -- a value long enough to be a paragraph belongs in the template.
    value       text NOT NULL CHECK (length(value) <= 2000),

    -- Not decoration. {{saved.discordInvite}} read in somebody else's draft
    -- six months later is only obvious if the picker can say what it is, and
    -- the person who saved it has almost certainly graduated.
    description text CHECK (description IS NULL OR length(description) <= 200),

    -- Who to ask about it. Nullable and ON DELETE SET NULL for the same reason
    -- applications.decided_by is: deleting the person must not delete the
    -- value, and a name attached to a row nobody recorded is worse than an
    -- empty column.
    updated_by  uuid REFERENCES identity.people (id) ON DELETE SET NULL,
    updated_at  timestamptz NOT NULL DEFAULT now()
);

-- Touched on every write, so the screen can say when a value last changed.
-- A trigger rather than the caller, for the reason 0006 gives about
-- updated_at: the one write that forgets is the hand-written one during the
-- event, and that is exactly the write somebody later needs the date of.
--
-- applications.touch_updated_at rather than a notify copy of it, and that is a
-- cross-schema reach worth saying out loud. The function sets one column to
-- now() and has nothing in it that could drift, so a second copy would be two
-- things to find and no safer. If notify ever has to stand without
-- applications, this is the line that says so.
CREATE TRIGGER saved_values_touch_updated_at
    BEFORE INSERT OR UPDATE ON notify.saved_values
    FOR EACH ROW EXECUTE FUNCTION applications.touch_updated_at();
