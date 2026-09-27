const dateFormat = new Intl.DateTimeFormat("en-US", {
  month: "short", day: "numeric", year: "numeric", timeZone: "UTC",
});
const timeFormat = new Intl.DateTimeFormat("en-US", {
  hour: "numeric", minute: "2-digit", timeZone: "UTC", timeZoneName: "short",
});

export function ApplicantTime({ value }: { value: string }) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return <span>—</span>;
  return <time dateTime={value}>
    <span>{dateFormat.format(date)}</span>{" · "}<span>{timeFormat.format(date)}</span>
  </time>;
}
