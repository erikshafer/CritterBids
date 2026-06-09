import {
  createContext,
  use,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";

// Anonymous participant session (narrative 003; narrative 001 Moment 1).
//
// M8-S2 deliberately renders the session as ESTABLISHED/ANONYMOUS with no generated display-name
// header. The backend mints a display name (e.g. "DaringOtter4271") into the ParticipantSessionStarted
// event, but `POST /api/participants/session` returns ONLY the participant id (CreationResponse<Guid>)
// and there is no anonymous read endpoint that surfaces the name. Rendering it would require a
// backend change M8's non-goals forbid, so the name header is a recorded carry-forward (see the
// M8-S2 prompt's Findings / Open Questions). We hold the id — the token a later bidding slice's
// bid placement will carry.
export type SessionStatus = "establishing" | "established" | "error";

export interface SessionState {
  participantId: string | null;
  status: SessionStatus;
}

const SessionStateContext = createContext<SessionState | null>(null);

const STORAGE_KEY = "critterbids.participantId";

// The CreationResponse<Guid> Location header is "/api/participants/{id}"; the id is its last segment.
// Reading the header is robust to body-serialization details; we fall back to the body's `value`.
function extractParticipantId(
  location: string | null,
  body: unknown,
): string | null {
  if (location) {
    const segment = location.split("/").filter(Boolean).pop();
    if (segment) return segment;
  }
  if (body && typeof body === "object" && "value" in body) {
    const value = (body as { value: unknown }).value;
    if (typeof value === "string") return value;
  }
  return null;
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<SessionState>(() => {
    const existing = sessionStorage.getItem(STORAGE_KEY);
    return existing
      ? { participantId: existing, status: "established" }
      : { participantId: null, status: "establishing" };
  });

  // React 19 StrictMode double-invokes effects in dev; this guard ensures one POST per real mount.
  //
  // Deliberately NO cancelled-flag cleanup here. With the startedRef guard, StrictMode's
  // setup→cleanup→setup sequence leaves exactly one in-flight fetch — started by the FIRST
  // invocation, whose cleanup has already run by the time the response arrives. A cancelled flag
  // therefore dropped the only result on the floor: the POST returned 201 but the state stayed
  // "establishing" forever on every fresh-storage dev visit (found in the M8 Bug #2 fix-up
  // walkthrough). The provider lives for the app's lifetime, so applying the result from the
  // torn-down effect instance is safe — and a setState after a real unmount is a no-op.
  const startedRef = useRef(false);

  useEffect(() => {
    if (state.status === "established" || startedRef.current) return;
    startedRef.current = true;

    void (async () => {
      try {
        // Wolverine.HTTP binds the StartParticipantSession command from the request body, so the
        // POST needs a JSON body even though the command is an empty record — an empty body 400s
        // ("Invalid JSON format"). "{}" satisfies the binder. (Verified live, M8-S2 smoke check.)
        const response = await fetch("/api/participants/session", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            Accept: "application/json",
          },
          body: "{}",
        });
        if (!response.ok) {
          throw new Error(`Session start failed with ${response.status}.`);
        }
        const body: unknown = await response.json().catch(() => null);
        const id = extractParticipantId(response.headers.get("Location"), body);
        if (!id) {
          setState({ participantId: null, status: "error" });
          return;
        }
        sessionStorage.setItem(STORAGE_KEY, id);
        setState({ participantId: id, status: "established" });
      } catch {
        // A failed session start must NOT block anonymous catalog reads: the catalog endpoints
        // are [AllowAnonymous] and need no participant id (prompt acceptance criteria / milestone).
        setState({ participantId: null, status: "error" });
      }
    })();
  }, [state.status]);

  return (
    <SessionStateContext value={state}>{children}</SessionStateContext>
  );
}

export function useSession(): SessionState {
  const context = use(SessionStateContext);
  if (context === null) {
    throw new Error("useSession must be used within a SessionProvider.");
  }
  return context;
}
