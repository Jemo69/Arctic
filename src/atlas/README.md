# atlas

The API. Applications, forms, people, templates, campaigns, audit — reached
only through harbor, never directly.

One service, multiple projects — not microservices. Only `MorganHacks.Api`
references the modules; modules never reference each other directly.
Cross-module calls go through DI-wired interfaces, and each module owns its
own tables: `identity.*` (sign-in, roles, sessions), `applications.*`
(applications, forms, check-in, announcements, resumes — most of what the
API does lives here), `profiles.*` in name only — see
[`MorganHacks.Profiles`](MorganHacks.Profiles/README.md).

See `doc-starter/morganhacks-stack.md` (local only — see `docs/README.md`).
