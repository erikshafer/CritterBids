import {
  createContext,
  use,
  useCallback,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

import { createStaffFetch, type StaffFetch } from "@/auth/staffApi";

// The operator-entered staff token (ADR 024's single shared secret) held in sessionStorage —
// the same storage pattern as the bidder's participantId (M8-S2). sessionStorage over
// localStorage is the M8-S5 open-question resolution: the operator re-enters the token per
// projector session; a clean session model beats cross-tab persistence for a shared secret.
const STORAGE_KEY = "critterbids.staffToken";

export interface StaffAuthValue {
  /** The held staff token, or null when the auth gate must show. */
  token: string | null;
  /**
   * Why the gate is showing after a clear — e.g. "the API rejected the stored token (401)".
   * Null on first entry. Cleared on the next successful setToken.
   */
  authError: string | null;
  /** Store a validated token; the gate unmounts and the dashboard (incl. the hub) mounts. */
  setToken: (token: string) => void;
  /** Drop the token (401 contract: clear + re-show the gate). `reason` surfaces on the gate. */
  clearToken: (reason?: string) => void;
  /**
   * The staff-gated fetch: attaches X-Staff-Token, clears the token on any 401. Stable identity
   * for the provider's lifetime — safe in effect/query dependencies.
   */
  staffFetch: StaffFetch;
}

const StaffAuthContext = createContext<StaffAuthValue | null>(null);

export function StaffAuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(() =>
    sessionStorage.getItem(STORAGE_KEY),
  );
  const [authError, setAuthError] = useState<string | null>(null);

  // staffFetch reads through this ref so its identity never changes when the token does.
  const tokenRef = useRef(token);
  tokenRef.current = token;

  const setToken = useCallback((next: string) => {
    sessionStorage.setItem(STORAGE_KEY, next);
    setAuthError(null);
    setTokenState(next);
  }, []);

  const clearToken = useCallback((reason?: string) => {
    sessionStorage.removeItem(STORAGE_KEY);
    setAuthError(reason ?? null);
    setTokenState(null);
  }, []);

  const staffFetch = useMemo(
    () =>
      createStaffFetch({
        getToken: () => tokenRef.current,
        onUnauthorized: () =>
          clearToken("The API rejected the staff token (401). Enter a valid token."),
      }),
    [clearToken],
  );

  const value = useMemo<StaffAuthValue>(
    () => ({ token, authError, setToken, clearToken, staffFetch }),
    [token, authError, setToken, clearToken, staffFetch],
  );

  return <StaffAuthContext value={value}>{children}</StaffAuthContext>;
}

export function useStaffAuth(): StaffAuthValue {
  const context = use(StaffAuthContext);
  if (context === null) {
    throw new Error("useStaffAuth must be used within a StaffAuthProvider.");
  }
  return context;
}
