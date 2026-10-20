# Directory image processor migration

## SkiaSharp implementation (October 20)

Photo uploads are restored using SkiaSharp and its Linux NoDependencies native
assets, both pinned to 4.153.1. No ImageSharp package or license-key configuration
remains. The shared `MorganHacks.Directory.Images` library serves the API and
local seed runner. No schema change or existing-photo rewrite is required.

Inputs must decode as JPEG, PNG or WebP, be at most 5 MiB, and have each side
between 200 and 8000 pixels. Two decoders may run concurrently. The first frame
is oriented (including mirrored EXIF orientations), center-cropped to 400×400,
and encoded as WebP at quality 82. Fresh pixels are encoded so input metadata
and animation chunks cannot pass through. Alpha is preserved. Cancellation is
checked between native operations; it cannot interrupt an in-progress native call.

Existing session, eligibility, ownership, archive and visibility checks remain.
Uploading produces a pending photo; saving the profile attaches it. Existing
photos remain attached until save, and abandoned uploads remain subject to cleanup.
Seed reruns preserve existing profiles and photos; new fixtures get ten generated
photos through the same processor. No original upload bytes are stored as fallback.

Rollback to the prior preparation commits disables uploads and preserves stored
photos. Do not roll back to ImageSharp without resolving its licensing separately.

## Branch scope and local preparation

The original PR heads have not been rewritten or pushed. Local preparation
branches retain their original feature head as an ancestor, then merge the
prepared predecessor. This avoids force pushes and preserves review changes.

| Existing branch / PR | Local prepared branch | Change needed |
|---|---|---|
| Features 1–4 / #226–229 | None | No ImageSharp usage |
| `feat/directory-photos` / #230 | `prep/directory-photos` | Shared SkiaSharp processor, API uploads, native assets and regression tests |
| `feat/directory-browse-api` / #231 | `prep/directory-browse-api` | Inherit photo changes; browse behavior unchanged |
| `chore/directory-seed-data` / #232 | `prep/directory-seed-data` | Generate seed photos with the shared processor; verify rerun preservation |
| `feat/portal-directory-profile` / #233 | `prep/portal-directory-profile` | Restore upload control and handler; retain photo removal |
| `feat/portal-directory-browse` / #234 | `prep/portal-directory-browse` | Inherit changes; existing photo/initials rendering remains |
| `feat/console-directory-moderation` / #235 | `prep/console-directory-moderation` | Inherit changes and update handoff/fixture documentation |
| `codex/directory-opening-timezone` / #240 | `prep/directory-opening-timezone` | Merge prepared profile predecessor, preserving timezone fix |
| `codex/directory-unsaved-status` / #242 | `prep/directory-unsaved-status` | Merge prepared profile predecessor, preserving unsaved-status fix |
| `codex/directory-pagination-errors` / #244 | `prep/directory-pagination-errors` | Merge prepared browse predecessor, preserving pagination fix |

Main, staging, and unrelated branches have no directory ImageSharp dependency.
PR #226 is merged. Main commit `93d9725`, including the contracts relocation and
snake_case answer keys, is integrated into the prepared photo branch and descendants.

The preparation checkout is `directory-image-prep` beside `review-fixes`.
The `upstream/*` refs are snapshots of GitHub heads fetched on October 20.
Before any later push, fetch GitHub again and reconcile new review changes.
Apply prepared changes in stack order, preserving ancestry. Validate the final deployed upload flow before enabling the directory in staging.

No stored secrets were read or deleted. Only their source-code references and
build-secret mounts were removed. Existing secret-store entries can be retired
separately after deciding not to return to ImageSharp.

## Verification

- macOS Release processor/API regression tests cover all eight EXIF orientations,
  crop, alpha, first frame, invalid/truncated files, byte/dimension limits,
  metadata chunk inspection, cancellation, upload ownership and delayed attachment.
- Seed suite: 19 tests pass, including generated photo dimensions and rerun preservation.
- Portal production build passes. The local browser preview uses the real API,
  an isolated synthetic database, and private Azurite photo storage.
- Final Linux AMD64 API image builds; native decode and WebP encode smoke passes.
- The broader API run completed 976 tests with zero assertion failures before
  the test host was terminated by the 90-second inactivity watchdog. This is
  **not a completed full-suite pass**. Its TRX, sequence and dump are retained locally.
- The unchanged portal lockfile has three high-severity audit entries in the
  `@vercel/config` → `@vercel/routing-utils` → `path-to-regexp` chain
  (GHSA-9wv6-86v2-598j). This migration does not suppress or upgrade those dependencies.
