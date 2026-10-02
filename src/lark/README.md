# lark

The mail worker. Claims queued messages and sends them through AWS SES.

**No ingress.** Nothing routes to it and it answers no requests — it only
picks up work from Postgres and pushes results out. That is also why it
never scales to zero: a queue with no worker fails silently, because nothing
would ever wake it back up.

It does not own `notify.*` alone, though. The schema — campaigns, templates,
the message queue, delivery and unsubscribe tracking — lives in
`MorganHacks.Lark.Data`, a project `MorganHacks.Api` also references
directly: organizers manage campaigns and templates through atlas's API,
not through lark. `MorganHacks.Lark` itself is only the send loop and the
SES adapter.

See `doc-starter/morganhacks-notify.md` (local only — see `docs/README.md`).
