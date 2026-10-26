"use client";

import { useEffect, useState } from "react";

const pad = (n: number) => String(n).padStart(2, "0");

/**
 * Days, hours, minutes and seconds until the doors open, pinned bottom right.
 *
 * It also balances the page: everything you interact with sits in the left
 * column, and this gives the right side something to hold besides the vault.
 *
 * Renders dashes on the server and fills in after mount, so the markup the
 * server sent and the first client render agree.
 */
export function Countdown({ startIso }: { startIso: string }) {
  const [now, setNow] = useState<number | null>(null);

  useEffect(() => {
    setNow(Date.now());
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);

  const left = now === null ? null : Math.max(0, new Date(startIso).getTime() - now);
  const parts =
    left === null
      ? ["--", "--", "--", "--"]
      : [Math.floor(left / 86_400_000), Math.floor(left / 3_600_000) % 24, Math.floor(left / 60_000) % 60, Math.floor(left / 1000) % 60].map(pad);
  const labels = ["Days", "Hrs", "Min", "Sec"];
  const done = left === 0;

  return (
    <aside className="heist-countdown" aria-label="Time until the event starts">
      <span className="heist-countdown__label">{done ? "// The job is on" : "// Doors open in"}</span>
      <span className="heist-countdown__clock" aria-live="off">
        {parts.map((value, i) => (
          <span key={labels[i]} className="heist-countdown__unit">
            <b>{value}</b>
            <small>{labels[i]}</small>
          </span>
        ))}
      </span>
    </aside>
  );
}
