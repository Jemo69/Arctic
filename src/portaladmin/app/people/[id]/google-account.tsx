import Image from "next/image";
import { Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Unlink } from "./controls";
import styles from "./person-detail.module.css";

export function GoogleAccount({ personId, email, linked, isSelf, revoked, canManage }: {
  personId: string;
  email: string;
  linked: boolean;
  isSelf: boolean;
  revoked: boolean;
  canManage: boolean;
}) {
  const localSession = process.env.NODE_ENV === "development" && isSelf && !linked;
  return <section className={styles.connections} aria-labelledby="connected-accounts-heading">
    <header className={styles.sectionHead}>
      <div><h2 id="connected-accounts-heading">Connected accounts</h2><p>{isSelf ? "Manage how you sign in to MorganHacks." : "The account this organizer uses to sign in."}</p></div>
    </header>
    <div className={styles.connectionCard}>
      <div className={styles.connectionHeader}>
        <div className={styles.connectionProvider}>
          <span className={styles.providerLogo}><Image src="/brands/google.svg" alt="" width={25} height={25} /></span>
          <h3>Google</h3>
        </div>
        <span className={styles.connectionStatus} data-connected={linked || undefined} data-local={localSession || undefined}>
          {linked ? <Icon icon={Tick02Icon} size={14} /> : <span aria-hidden="true" />}
          {linked ? "Connected" : localSession ? "Local session" : "Not connected"}
        </span>
      </div>
      <p className={styles.connectionDescription}>{revoked
          ? linked ? "Google is connected. Sign-in is paused until this organizer’s access is restored." : "Google can be connected after this organizer’s access is restored."
          : linked ? "This profile is linked to Google. Use the connected account to sign in to MorganHacks."
            : localSession ? "You’re signed in to the local preview. Google hasn’t been linked to this local profile."
            : isSelf ? "Connect Google to sign in with your organizer email."
              : "Google connects automatically when this organizer first signs in."}</p>
      {linked && canManage ? <Unlink personId={personId} email={email} isSelf={isSelf} />
        : !linked && isSelf && !revoked ? <div className={styles.connectionFooter}>
          <a className={styles.connectButton} href="/api/auth/google">Connect Google account</a>
        </div>
          : linked ? <p className={styles.connectionNote}>An admin can unlink this account if sign-in help is needed.</p> : null}
    </div>
  </section>;
}
