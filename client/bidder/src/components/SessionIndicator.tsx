import { useSession } from "@/session/SessionContext";

// Renders the anonymous session as ESTABLISHED — no generated display-name header this slice
// (deferred; see SessionContext + the M8-S2 prompt Findings). The name lands when a read path exists.
const LABEL: Record<string, string> = {
  establishing: "Starting session…",
  established: "Anonymous bidder",
  error: "Session unavailable",
};

export function SessionIndicator() {
  const { status } = useSession();

  return (
    <span className="text-muted-foreground text-xs" aria-live="polite">
      {LABEL[status]}
    </span>
  );
}
