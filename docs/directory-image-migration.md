# Directory image processor migration

## Preparation state

New photo uploads are temporarily unavailable (HTTP 503 after existing session,
feature, eligibility and archive checks). The API does not parse or persist their
contents. Request size limits remain. Existing photo reads, removals, ownership
checks, moderation and cleanup remain in place. Profile editing is unaffected.
There is no database migration and no existing photo is deleted by this change.

The former ImageSharp processor and its library-specific tests are removed
with its dependency. This is an intentional temporary feature withdrawal, not
a completed replacement. Regression tests cover the 503 response, no photo
writes/replacement, continued reads/removals, and eligibility/archive checks.

## Required before re-enabling uploads with SkiaSharp

- Accept only decoded JPEG, PNG and WebP; reject malformed/truncated files and
  disguised GIF/SVG/PDF input. Do not trust extensions or content types.
- Enforce the 5 MiB input limit and 200–8000 pixel side limits before allocating
  full pixel buffers; bound decode concurrency (previously two).
- Honor EXIF orientation, including mirrored orientations, before center-cropping
  to 400 by 400. Process only one frame, including animated inputs.
- Encode WebP at quality 82; verify GPS/EXIF, XMP, IPTC and ICC metadata is absent.
  Cover transparent inputs and cancellation/resource disposal.
- Restore processor tests using fixed input fixtures and independent output
  inspection. Retain upload ownership, visibility and cleanup coverage.
- Verify native assets on macOS and in the actual Linux AMD64 API and seed images.
- Re-enable the UI only with a working server processor; never pass original
  upload bytes through to storage as a fallback.

Roll back by reverting preparation commits, which restores the old dependency
and its license requirement. No data rollback is necessary.

## Branch scope and local preparation

The original PR heads have not been rewritten or pushed. Local preparation
branches retain their original feature head as an ancestor, then merge the
prepared predecessor. This avoids force pushes and preserves review changes.

| Existing branch / PR | Local prepared branch | Change needed |
|---|---|---|
| Features 1–4 / #226–229 | None | No ImageSharp usage |
| `feat/directory-photos` / #230 | `prep/directory-photos` | Remove API decoder/package and shared CI/Docker license wiring; disable upload writes; add regression tests |
| `feat/directory-browse-api` / #231 | `prep/directory-browse-api` | Inherit photo changes; browse behavior unchanged |
| `chore/directory-seed-data` / #232 | `prep/directory-seed-data` | Remove seed encoder/package, seed license wiring and unused seed storage adapter; test rerun preservation |
| `feat/portal-directory-profile` / #233 | `prep/portal-directory-profile` | Remove upload control/handler; explain temporary unavailability; retain photo removal |
| `feat/portal-directory-browse` / #234 | `prep/portal-directory-browse` | Inherit changes; existing photo/initials rendering remains |
| `feat/console-directory-moderation` / #235 | `prep/console-directory-moderation` | Inherit changes and update handoff/fixture documentation |
| `codex/directory-opening-timezone` / #240 | `prep/directory-opening-timezone` | Merge prepared profile predecessor, preserving timezone fix |
| `codex/directory-unsaved-status` / #242 | `prep/directory-unsaved-status` | Merge prepared profile predecessor, preserving unsaved-status fix |
| `codex/directory-pagination-errors` / #244 | `prep/directory-pagination-errors` | Merge prepared browse predecessor, preserving pagination fix |

Main, staging, and unrelated branches have no directory ImageSharp dependency.
The original PR #226 contracts relocation has not yet propagated through the
original stack; that separate integration remains necessary before landing.

The preparation checkout is `directory-image-prep` beside `review-fixes`.
The `upstream/*` refs are snapshots of GitHub heads fetched on October 20.
Before any later push, fetch GitHub again and reconcile new review changes.
Apply prepared changes in stack order, preserving ancestry. Do not deploy this
interim version expecting working uploads or advertise the photo PR as complete.

No stored secrets were read or deleted. Only their source-code references and
build-secret mounts were removed. Existing secret-store entries can be retired
separately after deciding not to return to ImageSharp.

## Verification on October 20

- Photo branch: 156 selected API regression tests pass in Release; the new
  unavailable-upload test was observed failing before the implementation change.
- Complete prepared stack: 159 directory/form-validation/form-submission/portal
  regression tests pass in Release (including photo, browse and moderation).
- Seed suite: 19 tests pass, including no-photo seeding and rerun preservation.
- Actual API and seed Docker `build` stages publish successfully for Linux AMD64
  with no license secrets. Final runtime stages and deployment were not run.
- Portal production build and TypeScript checks pass on the prepared profile
  branch. No browser end-to-end smoke test was performed for this preparation.
- .NET formatting verification, workflow YAML parsing, shell syntax and diff
  whitespace checks pass. API and seed resolved package graphs contain no
  SixLabors packages. All nine prepared branch tips have no ImageSharp/SixLabors
  references under source, workflows, deployment or scripts, and each retains
  its original GitHub feature head as an ancestor.

The full Atlas suite is **not green**. Two form-builder failures were reproduced
in isolation: `Reordering_questions_keeps_the_order_it_was_sent_in` and
`History_records_each_version_and_what_became_of_it`. Both reject the original
`directory_contactLinks`, `directory_buildIdea` and `directory_lookingFor` form
keys. Those source files are unchanged from the original full-stack head; the
corrections on PR #226 have not propagated to its descendants. The broad run was
stopped after these failures and several minutes without further progress; it
was not reported as a completed full-suite pass. Integrate the PR #226 fixes
through the stack separately, including their shared-library path updates, then
rerun the full suite before merging.

The unchanged portal lockfile also reports three high-severity npm audit entries
in the `@vercel/config` → `@vercel/routing-utils` → `path-to-regexp` chain
(GHSA-9wv6-86v2-598j). No dependencies were upgraded or audit checks suppressed as
part of this image-library removal.
