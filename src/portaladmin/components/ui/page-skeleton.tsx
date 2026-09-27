import { Skeleton, SkeletonRegion } from "./skeleton";
import styles from "./page-skeleton.module.css";

type ListKind = "campaigns" | "templates" | "people" | "applicants" | "responses" | "audit" | "permissions";
export type PageSkeletonKind = Exclude<ListKind, "permissions"> | "forms" | "events" | "person" | "campaign" | "template" | "form" | "event" | "applicant" | "dashboard";

function Lines({ wide = "72%", narrow = "42%" }: { wide?: string; narrow?: string }) {
  return <div className={styles.lines}><Skeleton width={wide} height={13} /><Skeleton width={narrow} height={9} /></div>;
}

function Toolbar({ tabs = false, compact = false }: { tabs?: boolean; compact?: boolean }) {
  return <div className={styles.toolbar}>
    <Skeleton width={tabs ? 115 : 280} height={38} />
    {!compact ? <><Skeleton width={tabs ? 90 : 130} height={38} /><Skeleton width={tabs ? 95 : 130} height={38} /></> : null}
    <Skeleton className={styles.toolbarEnd} width={100} height={38} />
  </div>;
}

function Rows({ kind = "campaigns", rows = 5, header = true }: { kind?: ListKind; rows?: number; header?: boolean }) {
  const thumbnail = kind === "campaigns" || kind === "templates";
  const avatar = kind === "people" || kind === "applicants" || kind === "responses";
  const permissions = kind === "permissions";
  return <div className={styles.table} data-kind={kind}>
    {header ? <div className={styles.tableHead}><Skeleton width={80} height={9} /><Skeleton width={65} height={9} /><Skeleton width={permissions ? 45 : 80} height={9} />{!permissions ? <Skeleton width={45} height={9} /> : null}</div> : null}
    {Array.from({ length: rows }, (_, index) => <div key={index} className={styles.row}>
      <div className={styles.identity}>
        {thumbnail || avatar ? <Skeleton width={thumbnail ? 54 : 36} height={thumbnail ? 64 : 36} radius={avatar ? "50%" : 9} /> : null}
        <Lines wide={index % 2 ? "62%" : "78%"} narrow={index % 2 ? "36%" : "48%"} />
      </div>
      <Skeleton width="70%" height={25} />
      {permissions ? <Skeleton width={60} height={10} /> : <><div className={styles.metrics}><Skeleton width="65%" height={12} /><Skeleton width="48%" height={9} /></div><Skeleton width={65} height={30} /></>}
    </div>)}
  </div>;
}

export function ListBodySkeleton({ kind = "campaigns", rows = 5, header = true, label = "Loading results…" }: {
  kind?: ListKind; rows?: number; header?: boolean; label?: string;
}) {
  return <SkeletonRegion label={label}><Rows kind={kind} rows={rows} header={header} /></SkeletonRegion>;
}

function PreviewCards({ template = false }: { template?: boolean }) {
  return <div className={styles.cards}>{Array.from({ length: 4 }, (_, index) => <div className={styles.card} data-template={template || undefined} key={index}>
    <div className={styles.cardCanvas}><div className={styles.cardPaper}>
      <Lines wide="68%" narrow="88%" />
      <Skeleton height={template ? 75 : 24} radius={6} /><Lines wide="76%" narrow="60%" />
    </div></div>
    <div className={styles.cardCopy}><Skeleton width="76%" height={14} /><div className={styles.between}><Skeleton width="45%" height={10} /><Skeleton width={55} height={23} /></div></div>
  </div>)}</div>;
}

export function FormsBodySkeleton({ view = "grid" }: { view?: "grid" | "list" }) {
  return <SkeletonRegion label="Loading forms…">{view === "grid" ? <PreviewCards /> : <Rows />}</SkeletonRegion>;
}

function Fields({ count = 4 }: { count?: number }) {
  return <div className={styles.fields}>{Array.from({ length: count }, (_, index) => <div className={styles.field} key={index}>
    <Skeleton width={index % 2 ? 110 : 135} height={12} /><Skeleton height={46} radius={14} />
  </div>)}</div>;
}

function Tabs({ count = 3 }: { count?: number }) {
  return <div className={styles.tabs}>{Array.from({ length: count }, (_, index) => <Skeleton key={index} width={index % 2 ? 120 : 96} height={15} />)}</div>;
}

function DetailHeader({ profile = false, stats = false }: { profile?: boolean; stats?: boolean }) {
  return <>
    <div className={styles.breadcrumb}><Skeleton width={150} height={12} /></div>
    <div className={styles.detailHeader}>
      <div className={styles.identity}>{profile ? <Skeleton width={58} height={58} radius="50%" /> : null}
        <div className={styles.titleLines}><Skeleton width="82%" height={25} /><Skeleton width="48%" height={12} /></div>
      </div>
      {stats ? <div className={styles.headerStats}>{[0, 1].map(index => <div key={index}><Skeleton width={85} height={10} /><Skeleton width={22} height={23} /></div>)}</div> : <Skeleton width={150} height={40} />}
    </div>
  </>;
}

function Inbox() {
  return <div className={styles.preview}>
    <div className={styles.between}><Skeleton width={120} height={15} /><Skeleton width={55} height={10} /></div>
    <div className={styles.inboxRow}><Skeleton width={38} height={38} radius="50%" /><Lines wide="84%" narrow="66%" /></div>
    <div className={styles.inboxRow}><Skeleton width={38} height={38} radius="50%" /><Lines wide="56%" narrow="80%" /></div>
    <div className={styles.inboxRow}><Skeleton width={38} height={38} radius="50%" /><Lines wide="68%" narrow="58%" /></div>
  </div>;
}

export function EditorWorkspaceSkeleton() {
  return <SkeletonRegion label="Opening email editor…"><div className={styles.editorWorkspace}>
    <div className={styles.editorPane}><Toolbar tabs /><div className={styles.codeLines}>{[72, 84, 57, 91, 66, 80, 48, 75, 60, 85].map((width, index) => <Skeleton key={index} width={`${width}%`} height={12} />)}</div></div>
    <div className={styles.previewPane}><div className={styles.message}><Lines /><Skeleton height={170} radius={12} /><Lines wide="90%" narrow="76%" /><Lines wide="85%" narrow="62%" /><Skeleton width={130} height={36} /></div></div>
  </div></SkeletonRegion>;
}

export function EmailPreviewSkeleton() {
  return <SkeletonRegion label="Rendering email preview…"><div className={styles.message}>
    <Lines wide="76%" narrow="54%" /><Skeleton height={220} radius={12} />
    <Lines wide="94%" narrow="84%" /><Lines wide="87%" narrow="70%" /><Skeleton width={140} height={36} />
  </div></SkeletonRegion>;
}

function Metrics() {
  return <div className={styles.statGrid}>{Array.from({ length: 3 }, (_, index) => <div className={styles.stat} key={index}>
    <Skeleton width={110} height={12} /><Skeleton width={65} height={33} /><Skeleton width="72%" height={10} />
  </div>)}</div>;
}

export function BarsSkeleton({ label = "Loading chart…", rows = 5 }: { label?: string; rows?: number }) {
  return <SkeletonRegion label={label}><div className={styles.bars}>
    {Array.from({ length: rows }, (_, index) => <div key={index}><div className={styles.between}><Skeleton width={`${40 + index % 3 * 10}%`} height={11} /><Skeleton width={35} height={11} /></div><Skeleton width={`${90 - index * 11}%`} height={7} /></div>)}
  </div></SkeletonRegion>;
}

export function ChartSkeleton() {
  return <SkeletonRegion label="Loading response activity…"><div className={styles.chart}>
    <div className={styles.chartGrid}><span /><span /><span /></div>
    <div className={styles.chartBars}>{[28, 42, 36, 60, 49, 72, 62, 83, 65, 76, 57, 88, 74, 94].map((height, index) => <Skeleton key={index} width="100%" height={`${height}%`} radius="5px 5px 0 0" />)}</div>
  </div><div className={styles.between}><Skeleton width={65} height={9} /><Skeleton width={65} height={9} /></div></SkeletonRegion>;
}

export function EmailMetricsSkeleton() {
  return <SkeletonRegion label="Loading email performance…"><div className={styles.emailMetrics}><Metrics /><div><Skeleton width={150} height={14} /><BarsSkeleton rows={3} /></div></div></SkeletonRegion>;
}

export function ResponseDetailSkeleton() {
  return <SkeletonRegion label="Loading response…"><div className={styles.responseDetail}>
    <div className={styles.identity}><Skeleton width={40} height={40} radius="50%" /><Lines /></div>
    {Array.from({ length: 4 }, (_, index) => <div key={index} className={styles.answer}><Skeleton width="38%" height={12} /><Lines wide="94%" narrow="75%" /></div>)}
  </div></SkeletonRegion>;
}

const titles: Partial<Record<PageSkeletonKind, string>> = {
  campaigns: "Email Campaign", templates: "Templates", people: "People", applicants: "Applicants", forms: "Forms", events: "Events", audit: "Audit log",
};

export function PageSkeleton({ kind }: { kind: PageSkeletonKind }) {
  const title = titles[kind];
  return <SkeletonRegion label={`Loading ${title?.toLowerCase() ?? kind}…`} className={styles.page}>
    {title ? <header className={styles.pageHeader}><div><h1>{title}</h1><Skeleton width={310} height={11} /></div><Skeleton width={130} height={40} /></header> : null}
    {title && kind !== "audit" ? <Toolbar /> : null}
    {kind === "forms" || kind === "templates" ? <><Tabs count={4} /><PreviewCards template={kind === "templates"} /></> : null}
    {kind === "events" ? <div className={styles.eventList}>{[0, 1, 2].map(index => <div className={styles.eventCard} key={index}>
      <div className={styles.identity}><Skeleton width={56} height={62} radius={12} /><Lines wide="45%" narrow="62%" /><Skeleton width={110} height={30} /></div><div className={styles.cardFooter}><Skeleton width={150} height={11} /><Skeleton width={95} height={11} /></div>
    </div>)}</div> : null}
    {kind === "audit" ? <><Toolbar /><Rows kind="audit" rows={7} /></> : null}
    {["campaigns", "people", "applicants"].includes(kind) ? <Rows kind={kind as ListKind} /> : null}
    {kind === "person" ? <><DetailHeader profile stats /><Tabs /><div className={styles.permissionLayout}>
      <div className={styles.areaRail}><Skeleton width={100} height={11} />{Array.from({ length: 9 }, (_, index) => <Skeleton key={index} width={index % 2 ? "72%" : "94%"} height={15} />)}</div>
      <div><Lines wide="30%" narrow="55%" /><Toolbar compact /><Rows kind="permissions" rows={7} /></div>
    </div></> : null}
    {kind === "campaign" || kind === "template" ? <><DetailHeader /><Tabs count={kind === "campaign" ? 4 : 3} /><div className={styles.columns}>
      <div><Lines wide="30%" narrow="60%" /><Fields /><div className={styles.senderFields}><Fields count={1} /><Fields count={1} /></div></div>
      <div><Lines wide="35%" narrow="67%" /><Inbox /><div className={styles.preview}><Skeleton width={140} height={14} /><Fields count={1} /></div></div>
    </div></> : null}
    {kind === "responses" ? <><DetailHeader /><Metrics /><Toolbar /><Rows kind="responses" rows={6} /></> : null}
    {kind === "event" || kind === "applicant" ? <><DetailHeader profile={kind === "applicant"} /><Tabs /><div className={styles.detailColumns}>
      <div className={styles.detailPanel}><Lines /><Fields count={3} /></div><div className={styles.detailPanel}><Lines /><Fields count={2} /></div>
    </div></> : null}
    {kind === "form" ? <><DetailHeader /><Tabs /><div className={styles.builder}>
      <div className={styles.formQuestions}><Skeleton width={115} height={16} />
        {[0, 1, 2].map(index => <div key={index} className={styles.detailPanel}><Lines /><Fields count={1} /></div>)}
      </div>
    </div></> : null}
    {kind === "dashboard" ? <><div className={styles.breadcrumb}><Skeleton width={200} height={12} /></div><div className={styles.pageHeader}><Lines wide="80%" narrow="60%" /><Skeleton width={220} height={38} /></div><Tabs /><Metrics />
      <div className={styles.detailColumns}><div className={styles.detailPanel}><Lines /><ChartSkeleton /></div><div className={styles.detailPanel}><Lines /><BarsSkeleton /></div></div>
    </> : null}
  </SkeletonRegion>;
}
