// Render-time formatting helpers shared by the boards. Wire values stay strings/numbers until
// the moment of display (the schemas don't coerce — ADR 013 keeps the boundary a shape check).

const money = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
});

export function formatMoney(amount: number | null | undefined): string {
  return amount == null ? "—" : money.format(amount);
}

/** Projector-legible timestamp: time-of-day for today's events, date + time otherwise. */
export function formatTimestamp(iso: string | null | undefined): string {
  if (iso == null) return "—";
  const value = new Date(iso);
  if (Number.isNaN(value.getTime())) return iso;
  const now = new Date();
  const sameDay =
    value.getFullYear() === now.getFullYear() &&
    value.getMonth() === now.getMonth() &&
    value.getDate() === now.getDate();
  return sameDay
    ? value.toLocaleTimeString(undefined, { hour12: false })
    : value.toLocaleString(undefined, { hour12: false });
}

/** Shortened Guid for identity columns no human reads in full (and the title-join 404 fallback). */
export function shortId(id: string): string {
  return `${id.slice(0, 8)}…`;
}
