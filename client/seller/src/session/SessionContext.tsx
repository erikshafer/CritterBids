import {
  createContext,
  use,
  useCallback,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";

export type SessionStatus = "establishing" | "established" | "error";

export interface SessionState {
  participantId: string | null;
  status: SessionStatus;
  isRegisteredSeller: boolean;
  registerAsSeller: () => Promise<void>;
  isRegistering: boolean;
  registrationError: string | null;
}

const SessionStateContext = createContext<SessionState | null>(null);

const STORAGE_KEY = "critterbids.seller.participantId";
const SELLER_KEY = "critterbids.seller.isRegisteredSeller";

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
  const [sessionState, setSessionState] = useState<{
    participantId: string | null;
    status: SessionStatus;
  }>(() => {
    const existing = sessionStorage.getItem(STORAGE_KEY);
    return existing
      ? { participantId: existing, status: "established" }
      : { participantId: null, status: "establishing" };
  });

  const [isRegisteredSeller, setIsRegisteredSeller] = useState(() => {
    return sessionStorage.getItem(SELLER_KEY) === "true";
  });
  const [isRegistering, setIsRegistering] = useState(false);
  const [registrationError, setRegistrationError] = useState<string | null>(
    null,
  );

  const startedRef = useRef(false);

  useEffect(() => {
    if (sessionState.status === "established" || startedRef.current) return;
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
          setSessionState({ participantId: null, status: "error" });
          return;
        }
        sessionStorage.setItem(STORAGE_KEY, id);
        setSessionState({ participantId: id, status: "established" });
      } catch {
        setSessionState({ participantId: null, status: "error" });
      }
    })();
  }, [sessionState.status]);

  const registerAsSeller = useCallback(async () => {
    if (!sessionState.participantId || isRegisteredSeller) return;
    setIsRegistering(true);
    setRegistrationError(null);

    try {
      const response = await fetch(
        `/api/participants/${sessionState.participantId}/register-seller`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            Accept: "application/json",
          },
          body: JSON.stringify({
            participantId: sessionState.participantId,
          }),
        },
      );

      if (response.ok || response.status === 409) {
        sessionStorage.setItem(SELLER_KEY, "true");
        setIsRegisteredSeller(true);
      } else {
        throw new Error(
          `Registration failed with ${response.status}.`,
        );
      }
    } catch (err) {
      setRegistrationError(
        err instanceof Error ? err.message : "Registration failed.",
      );
    } finally {
      setIsRegistering(false);
    }
  }, [sessionState.participantId, isRegisteredSeller]);

  const state: SessionState = {
    participantId: sessionState.participantId,
    status: sessionState.status,
    isRegisteredSeller,
    registerAsSeller,
    isRegistering,
    registrationError,
  };

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
