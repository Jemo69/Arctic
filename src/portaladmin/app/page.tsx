import { redirect } from "next/navigation";
import { Suspense } from "react";
import { UserGroupIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { currentPerson } from "@/lib/api";
import { displayName } from "@/lib/person-profile";
import { HomeFooter } from "./home-footer";
import { HomeGreeting } from "./home-greeting";
import { HomeHeader } from "./home-header";
import { HomeInsights } from "./home-insights";
import { HomeEmail } from "./home-email-panel";
import { HomeEmailView } from "./home-email";
import { HomeOverview } from "./home-overview";
import { Shell } from "./shell";
import styles from "./home.module.css";

export default async function Home() {
  const person = await currentPerson();
  if (!person) redirect("/sign-in");
  const canViewEmail = person.permissions.has("email.view_stats");
  const email = canViewEmail
    ? <Suspense fallback={<HomeEmailView />}><HomeEmail /></Suspense> : null;
  const greeting = <HomeGreeting name={displayName(person.fullName, person.email)} />;

  return (
    <Shell personId={person.personId}>
      <div className={styles.home}>
        <HomeHeader teams={person.teams} />
        {person.permissions.has("applications.view") ? (
          <Suspense fallback={<HomeOverview canViewEmail={canViewEmail} greeting={greeting} />}>
            <HomeInsights canViewEmail={canViewEmail} greeting={greeting} />
          </Suspense>
        ) : <section className={styles.empty}>
          <Icon icon={UserGroupIcon} size={28} />
          <h1>Your organizer workspace</h1>
          <p>Ask your team admin for application access to see applicant analytics here.</p>
        </section>}
        {!person.permissions.has("applications.view") ? email : null}
      </div>
      <HomeFooter />
    </Shell>
  );
}
