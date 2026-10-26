"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { signOut } from "../actions";
import { TAB_ICONS } from "../../../components/heist/icons";
import { MarkerLoop } from "../../../components/heist/marker";

const tabs = [
  { href: "/portal", label: "Status" },
  // COPY — "Announcements" needs sign-off.
  { href: "/portal/announcements", label: "Announcements" },
  { href: "/portal/profile", label: "Profile" },
  // Beside Profile rather than inside it. The two lock on different rules —
  // the profile closes when a decision lands and the resume does not — and one
  // tab holding two lock states is a screen nobody can explain.
  { href: "/portal/resume", label: "Resume" },
  { href: "/portal/check-in", label: "Check in" },
  { href: "/portal/messages", label: "Emails" },
] as const;

/**
 * The five screens, and the way out.
 *
 * Check in is shown to everybody rather than only to people who have a code.
 * A tab that appeared the day somebody confirmed would tell them that day what
 * they were supposed to be told by an email, and the screen behind it explains
 * itself for anyone who is not there yet.
 *
 * Announcements sits second, next to the screen everybody lands on, because it
 * is the only tab here whose contents change while somebody is standing in the
 * building. The other three are answers to questions asked once. It is shown
 * to everybody for the same reason check in is: a tab that appeared the first
 * time something was posted would be a notification, and a notification is
 * exactly the thing this feature is not.
 *
 * A client component only because the current tab has to be marked, and
 * `usePathname` is the only way to know which one that is. `aria-current` does
 * the marking; the underline is the visual echo of it, not the other way
 * round, so a screen reader gets the same answer as a sighted reader.
 */
export function Tabs({directoryEligible=false,icons=false}:{directoryEligible?:boolean;icons?:boolean}) {
  const pathname = usePathname();
  // The Heist theme draws a kit icon before each label. Decorative: the label
  // already says where the tab goes.
  const icon = (href: string) =>
    icons ? (
      <svg width="17" height="17" viewBox="0 0 48 48" fill="none" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <path d={TAB_ICONS[href]} />
      </svg>
    ) : null;
  // Keyed by the tab, so switching tabs mounts a fresh loop and it draws itself in.
  const marker = (href: string, active: boolean) => (icons && active ? <MarkerLoop key={href} id={href} /> : null);

  return (
    <nav className="portal__tabs" aria-label="Your application">
      {directoryEligible?<Link href="/portal/directory" aria-current={pathname.startsWith("/portal/directory")?"page":undefined}>{icon("/portal/directory")}Directory{marker("/portal/directory",pathname.startsWith("/portal/directory"))}</Link>:null}
      {tabs.map(({ href, label }) => (
        <Link
          key={href}
          href={href}
          // Exact, because /portal is a prefix of every other tab and a
          // startsWith test would light up "Status" on all three.
          aria-current={pathname === href ? "page" : undefined}
        >
          {icon(href)}
          {label}
          {marker(href, pathname === href)}
        </Link>
      ))}

      {/*
        A form rather than a link, because signing out is a change and a GET
        that changes something is a GET a link prefetcher can make for you.
      */}
      <form action={signOut}>
        <button type="submit" className="link">
          Sign out
        </button>
      </form>
    </nav>
  );
}
