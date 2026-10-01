# Mail is not arriving

**The failure this page exists for looks exactly like success.** Queue drains,
messages go to `sent`, no errors anywhere, and nothing reaches an inbox. Three
unrelated causes produce that same picture, and **testing with an address you
have verified hides all three.**

Check these in order. The first two take a minute each.

---

## 1. Which AWS account is the deploy actually using

The identities, the DKIM records and the production access all live in
**`602773792443`**. A key from any other account authenticates perfectly and
then fails every send, because the domains it is trying to send from do not
exist there.

This is easy to get wrong because a laptop usually has two profiles:

```bash
aws sts get-caller-identity                        # whatever your shell defaults to
aws sts get-caller-identity --profile morganhacks  # should be 602773792443
```

For the deployed worker, the key is the `AWS_ACCESS_KEY_ID` secret on the
GitHub environment. **GitHub will not show a secret back**, so there is no way
to read it — the ways to find out are:

- CloudTrail in the suspect account: a `SendEmail` denial there names it
- `SesEmailProvider` logs `ErrorCode` and `StatusCode` on a refusal, so a
  `MessageRejected` reading "Email address is not verified" is the signature
- Or send a real link, below, which settles it

## 2. Sandbox, or production

```bash
aws sesv2 get-account --profile morganhacks --region us-east-2 \
  --query "{Production:ProductionAccessEnabled, Sending:SendingEnabled, Quota:SendQuota.Max24HourSend}"
```

**`ProductionAccessEnabled: true` is the pre-launch check.** In the sandbox,
SES accepts a send to a verified recipient and refuses everyone else — and
`SesEmailProvider` turns that refusal into an ordinary `SendOutcome`, so the
queue records it and the worker carries on. Nothing in the system shouts.

Checked 2026-10-01: production access on, sending on, 50,000/day, in
`us-east-2`.

## 3. Region

```bash
gh api repos/MorganHacks/Arctic/environments/Staging/variables \
  --jq '.variables[] | select(.name=="AWS_REGION") | .value'
```

Should be `us-east-2` on Staging and Production. `apps.bicep` passes it to lark
as `AWS_REGION`.

A missing region does **not** silently default: `lark/Program.cs` reads it and
registers `UnconfiguredEmailProvider` when it is absent, so mail queues at
`pending` and never sends. That is a different symptom — nothing moves out of
`pending` at all.

## 4. The identities themselves

```bash
aws sesv2 get-email-identity --email-identity auth.morganhacks.com \
  --profile morganhacks --region us-east-2 \
  --query "{verified:VerifiedForSendingStatus, dkim:DkimAttributes.Status, mailFrom:MailFromAttributes.MailFromDomainStatus}"
```

Each sending domain has its **own** bounce subdomain, and that is correct
rather than a mismatch:

| Identity | MAIL FROM |
|---|---|
| `auth.morganhacks.com` — sign-in links | `bounce.auth.morganhacks.com` |
| `morganhacks.com` — broadcast | `bounce.morganhacks.com` |
| `mail.morganhacks.com` | `bounce.mail.morganhacks.com` |

All three verified with DKIM `SUCCESS` and MAIL FROM `SUCCESS` as of
2026-10-01. Note that no migration names a bounce domain — MAIL FROM is set in
SES, not in the schema, so there is nothing in the repository to keep in step
with it.

---

## The test that actually proves it

**Send one real sign-in link to an address that is not on the verified list.**

That single send exercises the credentials, the account, the region, the
identity and the MAIL FROM together. Every other check tests one of them.

Using a verified recipient proves almost nothing: a sandboxed account and a
wrong-account key both deliver successfully to a verified address and fail for
everybody else.

Then read what actually happened, which the queue records whatever the outcome:

```sql
SELECT status, attempts, last_error, sent_at
  FROM notify.messages
 WHERE to_email = '<the address>'
 ORDER BY created_at DESC LIMIT 1;
```

| What you see | What it means |
|---|---|
| `sent`, and it arrives | the whole path works |
| `sent`, and it does not arrive | delivery, not configuration — check the bounce domain and the recipient's spam folder |
| `failed_perm`, "Email address is not verified" | **sandbox, or the wrong account** |
| stuck at `pending`, no attempts | no region configured, so `UnconfiguredEmailProvider` is registered |
| `failed_temp` with attempts climbing | throttling or a transient refusal; `RetrySchedule` is backing off |

---

## While you are here: the sender name

If mail arrives from **"mail"** rather than the name you set, that is not SES.
A campaign's `email_settings` holds `""` for a field nobody filled in, and
`->>` returns that empty string as a value — so a `COALESCE` against the
template's name preferred the empty override. Fixed by wrapping the campaign
side in `NULLIF` before the `COALESCE`; if it reappears, that is the line.

---

**Escalate to:** the tech lead. Before changing anything in SES, check the
account first — most of what looks like a broken identity is a key pointed at
the wrong one.
