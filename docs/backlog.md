# Backlog

Checked against the code on **2026-10-02**. Of what was here before, most is
either fixed or superseded by a more specific doc, so it has been cut rather
than carried forward stale. What is left is what is still actually true.

Going forward, a gap belongs in **GitHub Issues**, not this file — the
templates are in `.github/ISSUE_TEMPLATE/`. This page stops being where new
findings accumulate; it is not worth maintaining a second issue tracker by
hand.

## Still true

### `rsvp_deadline` is withheld from the applicant mail-merge catalogue for a reason that no longer holds

`ApplicantColumns.cs` withholds it with a comment that says why: *"nothing in
this system knows the event's timezone, so any rendering picks one on the
reader's behalf... When `applications.events` carries a timezone, this is the
first column to reconsider."*

`EventZone` (`MorganHacks.Applications/Domain/EventZone.cs`) exists now, and
`MergeFields.cs` already renders the `event.*` namespace's dates through it.
The condition the comment names has been met. Nobody has revisited
`rsvp_deadline` since.

### Postgres accepts connections from any Azure tenant

`platform.bicep` creates the `0.0.0.0-0.0.0.0` firewall rule — Azure's "allow
all Azure services" — on both `psql-mh-staging` and `psql-mh-prod`. Any
resource in any tenant can open a connection with only the password in the
way, and the connection string sets `Trust Server Certificate=true`, so the
TLS is encrypted but not authenticated. The fix is VNet integration with a
private endpoint, which means a new Container Apps environment — VNet cannot
be added to an existing one. This was worth deferring while only staging
existed; it is a live exposure now that `rg-mh-prod` holds real applicant
data.

### Cloudflare rate limiting is still not configured

`src/harbor/MorganHacks.Harbor/Program.cs` has always said the two layers do
different jobs: Cloudflare absorbs volume, harbor handles the per-identity
limits Cloudflare cannot express. Only the second half exists — there is no
Cloudflare rate-limiting rule anywhere in the repository, in either
environment. (The per-IP spoofing gap this used to sit next to — forwarded
headers believed from anyone — is fixed: see
`libs/observability/MorganHacks.Observability/ClientAddress.cs`, which now
only believes `X-Forwarded-For`/`X-Real-IP` when the request also carries the
`Network:ProxySecret` shared secret. Cloudflare absorbing raw volume in front
of that is the half still missing.)

### Abandoned resume uploads are never cleaned up

A row in `applications.resume_uploads` is written when the file arrives, and
closing the tab half-way through a form is the ordinary way to leave one
unspent. `0013_resume_uploads.sql` creates the partial index a sweeper would
read:

```sql
CREATE INDEX resume_uploads_unclaimed_idx
    ON applications.resume_uploads (created_at) WHERE claimed_at IS NULL;
```

Nothing reads it — there is still no `BackgroundService`/`IHostedService` in
atlas (lark's `SendLoop` is the only one in the repository), and
`IResumeStore` still has no delete method. What breaks: a storage bill, and a
pile of CVs belonging to people who never applied and never will.
