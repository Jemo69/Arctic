# Local hacker directory fixtures

From the repository root, start the existing local Postgres and Azurite stack and apply migrations, then run:

```sh
./deploy/local/seed-hackers.sh --directory
./deploy/local/seed-hackers.sh --directory --apply
```

The first command is a preview. The second verifies the local Docker Postgres cluster before writing. `--directory --staging` is refused. The separate `mock-directory-v1` event has a past directory opening date; no real event's date is changed.

Fixtures create 41 suppressed-email hacker identities, 40 confirmed applications, 30 opted-in profiles before moderation/opt-out adjustments, and ten generated photos processed with SkiaSharp. Other profiles use initials. Existing profiles and application decisions are preserved on reruns. Existing photo records and stored blobs are left intact. This is not a production repair tool.

Use the existing local development sign-in flow with `mock-directory-01@example.com` through `mock-directory-41@example.com`. Number 41 is accepted but has not RSVP'd. Numbers 28 and 29 are hidden; 30 is opted out. Never send email to these fixture addresses.

Enable `ENABLE_HACKER_PORTAL_FEATURE=true` and `ENABLE_DIRECTORY_FEATURE=true` on the local API and portal; the console also needs `ENABLE_DIRECTORY_FEATURE=true`. Defaults remain off. Production/staging opening dates must be chosen separately by an organizer.

Teams contain at most four hackers, including the profile owner. “Teammates needed” allows one, two, or three.

The seed executable no longer depends on ImageSharp or a license key. Directory seeding writes generated photos to private local Azurite storage. See [the migration notes](directory-image-migration.md) for processor and native runtime requirements.
