import { useSession } from "@/session/SessionContext";

export function HomePage() {
  const { participantId, status } = useSession();

  return (
    <div className="mx-auto max-w-lg space-y-4 py-8">
      <h2 className="text-lg font-semibold text-foreground">
        Seller Console — Scaffold Proof
      </h2>
      <p className="text-muted-foreground text-sm">
        This is the M9-S1 scaffold. The BiddingHub connection indicator in the
        header shows the live SignalR connection state. Seller UI surfaces
        (listing management, auction observation, obligation tracking) land in
        M9-S4 through M9-S6.
      </p>
      <dl className="rounded-lg border border-border bg-card p-4 text-sm">
        <div className="flex justify-between py-1">
          <dt className="text-muted-foreground">Session</dt>
          <dd className="font-mono text-foreground">{status}</dd>
        </div>
        {participantId && (
          <div className="flex justify-between py-1">
            <dt className="text-muted-foreground">Participant ID</dt>
            <dd className="font-mono text-xs text-foreground">
              {participantId}
            </dd>
          </div>
        )}
      </dl>
    </div>
  );
}
