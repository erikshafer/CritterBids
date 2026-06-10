import { BoardState } from "@/operations/BoardState";
import {
  BoardTable,
  BoardTableBody,
  BoardTableHead,
  StatusBadge,
  Td,
  Th,
} from "@/operations/Table";
import { formatMoney, formatTimestamp, shortId } from "@/operations/format";
import { useParticipants, useSessions } from "@/operations/queries";

// Sessions & participants — the two activity boards on one route (the S5 nav's single entry):
// the Flash-session lineup (GET /api/operations/sessions, newest created first) and the
// participant-session activity board (GET /api/operations/participants, newest started first).
// Both push-covered (SessionCreated/SessionStarted, ParticipantSessionStarted/
// SellerRegistrationCompleted carry listingId: null — board-level invalidations).

export function SessionsBoard() {
  const sessions = useSessions();
  const participants = useParticipants();

  return (
    <div className="space-y-10">
      <section className="space-y-6">
        <h1 className="text-3xl font-semibold tracking-tight">Sessions</h1>
        <BoardState
          query={sessions}
          emptyMessage="No Flash sessions yet — the lineup fills as sessions are created."
        >
          {(rows) => (
            <BoardTable>
              <BoardTableHead>
                <Th>Session</Th>
                <Th>Status</Th>
                <Th className="text-right">Duration</Th>
                <Th className="text-right">Listings</Th>
                <Th>Created</Th>
                <Th>Started</Th>
              </BoardTableHead>
              <BoardTableBody>
                {rows.map((row) => (
                  <tr key={row.sessionId}>
                    <Td className="font-medium">
                      {row.title ?? (
                        <span
                          className="text-muted-foreground font-mono"
                          title={row.sessionId}
                        >
                          {shortId(row.sessionId)}
                        </span>
                      )}
                    </Td>
                    <Td>
                      <StatusBadge
                        tone={row.status === "Started" ? "positive" : "default"}
                      >
                        {row.status}
                      </StatusBadge>
                    </Td>
                    <Td className="text-right tabular-nums">
                      {row.durationMinutes} min
                    </Td>
                    <Td className="text-right tabular-nums">
                      {row.attachedListingIds.length}
                    </Td>
                    <Td className="text-muted-foreground tabular-nums">
                      {formatTimestamp(row.createdAt)}
                    </Td>
                    <Td className="text-muted-foreground tabular-nums">
                      {formatTimestamp(row.startedAt)}
                    </Td>
                  </tr>
                ))}
              </BoardTableBody>
            </BoardTable>
          )}
        </BoardState>
      </section>

      <section className="space-y-6">
        <h2 className="text-2xl font-semibold tracking-tight">Participants</h2>
        <BoardState
          query={participants}
          emptyMessage="No participant sessions yet — rows appear as attendees scan in."
        >
          {(rows) => (
            <BoardTable>
              <BoardTableHead>
                <Th>Display name</Th>
                <Th>Bidder</Th>
                <Th className="text-right">Credit ceiling</Th>
                <Th>Started</Th>
              </BoardTableHead>
              <BoardTableBody>
                {rows.map((row) => (
                  <tr key={row.participantId}>
                    <Td className="font-medium">
                      {row.displayName ?? (
                        <span
                          className="text-muted-foreground font-mono"
                          title={row.participantId}
                        >
                          {shortId(row.participantId)}
                        </span>
                      )}
                    </Td>
                    <Td className="font-mono">{row.bidderId ?? "—"}</Td>
                    <Td className="text-right tabular-nums">
                      {formatMoney(row.creditCeiling)}
                    </Td>
                    <Td className="text-muted-foreground tabular-nums">
                      {formatTimestamp(row.startedAt)}
                    </Td>
                  </tr>
                ))}
              </BoardTableBody>
            </BoardTable>
          )}
        </BoardState>
      </section>
    </div>
  );
}
