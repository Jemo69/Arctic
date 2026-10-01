-- Replies to our mail go to an inbox that exists.
--
-- 0005 seeded the sign-in template with reply_to hello@morganhacks.com and
-- 0025 did the same for the organizer welcome. The comment in 0005 says why
-- the column is set at all, and it is the right argument:
--
--   Replies go to an inbox somebody actually reads. People do reply to
--   sign-in emails to ask for help, and without this the reply goes to
--   login@auth.morganhacks.com, which has no mailbox behind it -- so it
--   disappears and the person thinks they were ignored.
--
-- hello@ is one address over from that failure. The apex MX is Cloudflare
-- Email Routing and the address it actually routes is info@morganhacks.com,
-- so a reply to hello@ lands in the same nowhere the comment was written to
-- avoid. Same reasoning, corrected address.
--
-- ------------------------------------------------- why not edit the seeds ---
--
-- 0005 and 0025 have already run everywhere, and DbUp runs a script once. An
-- edit there would change what a fresh database gets and leave every existing
-- one wrong, which is the worst of both -- so the seeds keep saying what they
-- said on the day and this says what changed.
--
-- --------------------------------------------- why the WHERE clause matters ---
--
-- Only rows that still say hello@. The console can edit a template now, so
-- somebody may already have pointed one of these somewhere deliberate, and a
-- blanket UPDATE would silently undo that. A migration correcting an old
-- default must not overwrite a new decision.
--
-- Live rows only. A superseded version is what was sent at the time, and
-- rewriting history to be more correct than it was makes the version trail
-- useless for working out what somebody actually received.
UPDATE notify.templates
   SET reply_to = 'info@morganhacks.com'
 WHERE reply_to = 'hello@morganhacks.com'
   AND superseded_at IS NULL;
