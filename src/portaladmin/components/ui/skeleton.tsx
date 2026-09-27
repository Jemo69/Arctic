import type { CSSProperties, ReactNode } from "react";
import styles from "./skeleton.module.css";

export function Skeleton({ width = "100%", height = 12, radius = 999, className = "" }: {
  width?: CSSProperties["width"];
  height?: CSSProperties["height"];
  radius?: CSSProperties["borderRadius"];
  className?: string;
}) {
  return <span aria-hidden="true" className={`${styles.shape} ${className}`} style={{ width, height, borderRadius: radius }} />;
}

export function SkeletonRegion({ label, children, className = "" }: {
  label: string; children: ReactNode; className?: string;
}) {
  return <div className={`${styles.region} ${className}`} aria-busy="true" aria-label={label}>
    <span className={styles.srOnly} role="status">{label}</span>
    <div aria-hidden="true">{children}</div>
  </div>;
}
