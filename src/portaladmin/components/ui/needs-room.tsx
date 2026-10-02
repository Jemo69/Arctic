"use client";

/*
 * A narrow-window notice for the console's four authoring tools: the form
 * builder, the two template editors, and the campaign composer. The other
 * thirteen routes are a record and a decision — approve this person, read
 * this answer — and stay useful on a phone. These four assume a laptop: a
 * preview pane beside an editing pane, drag handles, multi-column settings.
 * That is a fact about the page, so the page opts in by wrapping its own
 * return in <NeedsRoom> rather than a shared list elsewhere deciding for it.
 *
 * Whether to hide the tool is a CSS media query, not a user-agent check and
 * not a JavaScript width read. A laptop window narrowed to a third of the
 * screen has exactly the same layout problem as a phone, and only a media
 * query is true of the window itself rather than of what a string claims
 * about the device. The breakpoint is --bp-lg (48rem / 768px) from
 * libs/ui/tokens.css: a tablet, or a laptop window given up to half the
 * screen, which is around where these editors stop having room for a
 * preview beside the fields. The custom property itself cannot appear in
 * the media query — @media does not resolve custom properties — so the
 * stylesheet repeats the literal and names the token next to it.
 *
 * The whole wrapper is a client component, not only the dismiss button,
 * because that button is the only reason any part of this needs to run on
 * the client, and splitting one small file in two would not shrink the
 * boundary, only rename it. The children stay outside that boundary in
 * substance: the page that calls <NeedsRoom> is a server component, so the
 * builder or composer it passes in is already-rendered server output
 * flowing through as a prop, not code that now runs on the client because it
 * is sitting inside this element.
 */

import Link from "next/link";
import { useState, type ReactNode } from "react";
import styles from "./needs-room.module.css";

export function NeedsRoom({ back, backLabel, children }: {
  /** Where the list this tool was opened from lives. */
  back: string;
  /** What that list calls itself, e.g. "Forms" or "Templates". */
  backLabel: string;
  children: ReactNode;
}) {
  const [dismissed, setDismissed] = useState(false);

  return (
    <div className={styles.wrapper} data-dismissed={dismissed || undefined}>
      <div className={styles.notice} role="status">
        <p>This editor is built for a wider window than this one.</p>
        <div className={styles.actions}>
          <Link href={back} className={styles.back}>&larr; {backLabel}</Link>
          <button type="button" className={styles.proceed} onClick={() => setDismissed(true)}>
            Use it anyway
          </button>
        </div>
      </div>
      <div className={styles.content}>{children}</div>
    </div>
  );
}
