"use client";

import { useRef, useState, type ReactNode } from "react";
import styles from "./person-detail.module.css";

const tabs = [
  { id: "permissions", label: "Permissions" },
  { id: "teams", label: "Teams & grants" },
  { id: "account", label: "Account" },
] as const;

type Tab = typeof tabs[number]["id"];

export function PersonTabs({ permissions, teams, account, permissionCount }: {
  permissions: ReactNode; teams: ReactNode; account: ReactNode; permissionCount: number;
}) {
  const [active, setActive] = useState<Tab>("permissions");
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const content = { permissions, teams, account };

  return <>
    <div className={styles.tabs} role="tablist" aria-label="Person details">
      {tabs.map((tab, index) => <button key={tab.id} ref={node => { buttons.current[index] = node; }}
        id={`person-tab-${tab.id}`} type="button" role="tab" aria-selected={active === tab.id}
        aria-controls={`person-panel-${tab.id}`} tabIndex={active === tab.id ? 0 : -1}
        onClick={() => setActive(tab.id)} onKeyDown={event => {
          const next = event.key === "ArrowRight" ? (index + 1) % tabs.length
            : event.key === "ArrowLeft" ? (index + tabs.length - 1) % tabs.length
              : event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : null;
          if (next === null) return;
          event.preventDefault();
          setActive(tabs[next].id);
          buttons.current[next]?.focus();
        }}>
        {tab.label}{tab.id === "permissions" ? <span className={styles.count}>{permissionCount}</span> : null}
      </button>)}
    </div>
    {tabs.map(tab => <div key={tab.id} id={`person-panel-${tab.id}`} role="tabpanel"
      aria-labelledby={`person-tab-${tab.id}`} hidden={active !== tab.id} className={styles.tabPanel}>
      {content[tab.id]}
    </div>)}
  </>;
}
