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
