import type { ReactNode } from "react";
import { loadApplicantAnalytics, loadBestEmails, loadEmailAnalytics } from "./home-actions";
import { HomeOverview } from "./home-overview";

export async function HomeInsights({ canViewEmail, greeting }: { canViewEmail: boolean; greeting: ReactNode }) {
  const [result, email, bestEmails] = await Promise.all([
    loadApplicantAnalytics(),
    canViewEmail ? loadEmailAnalytics() : Promise.resolve(undefined),
    canViewEmail ? loadBestEmails() : Promise.resolve(undefined),
  ]);
  return <HomeOverview key={JSON.stringify(result)} initial={result}
    initialEmail={email} initialBest={bestEmails} canViewEmail={canViewEmail} greeting={greeting} />;
}
