import {
  createContext,
  use,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";

export type SessionStatus = "establishing" | "established" | "error";

export interface SessionState {
  participantId: string | null;
  status: SessionStatus;
}

const SessionStateContext = createContext<SessionState | null>(null);

const STORAGE_KEY = "critterbids.seller.participantId";

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

  const startedRef = useRef(false);

  useEffect(() => {
    if (state.status === "established" || startedRef.current) return;
    startedRef.current = true;

    void (async () => {
      try {
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
