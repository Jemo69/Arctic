# MorganHacks.Lark.Data

Campaigns, templates, the send queue, delivery tracking, unsubscribes — all
of `notify.*`.

Referenced by both `MorganHacks.Lark` (the worker that sends) and
`MorganHacks.Api` (organizers managing campaigns and templates through the
console). That is deliberate sharing, not a layering mistake: `notify.*` has
one schema and one data-access layer, used by two services for two
different jobs.
