// ADR 024 credential transport, HTTP half: every staff-gated request carries the staff token in
// the X-Staff-Token header — never a query-string parameter on HTTP paths. (The hub half — the
// access_token query string on the WebSocket upgrade — lives in src/signalr/SignalRProvider.tsx.)

/** Mirrors `StaffAuthConstants.StaffTokenHeader` (src/CritterBids.Api/Auth/StaffAuthConstants.cs). */
export const STAFF_TOKEN_HEADER = "X-Staff-Token";

/**
 * The probe endpoint the auth gate validates a candidate token against. Any of the seven
 * staff-gated GET endpoints would do; the lot board is the dashboard's landing view. It returns
 * 200 + [] even when no rows exist (never 404), so a 2xx is purely an auth signal.
 */
export const STAFF_PROBE_PATH = "/api/operations/lot-board";

export type StaffFetch = (
  input: string | URL,
  init?: RequestInit,
) => Promise<Response>;

export interface StaffFetchOptions {
  /** Reads the currently held staff token; called per request so rotation needs no rebinding. */
  getToken: () => string | null;
  /** Invoked on any 401 response — the ADR 024 contract: clear the stored token, re-show the gate. */
  onUnauthorized: () => void;
  /** Test seam; production uses the global fetch. */
  fetchImpl?: typeof fetch;
}

/**
 * A fetch wrapper for the staff-gated `/api/operations/*` surface: attaches the X-Staff-Token
 * header and funnels every 401 into `onUnauthorized`. M8-S6's query functions go through this;
 * in S5 it backs the auth-gate lifecycle and its tests.
 */
export function createStaffFetch({
  getToken,
  onUnauthorized,
  fetchImpl = fetch,
}: StaffFetchOptions): StaffFetch {
  return async (input, init) => {
    const headers = new Headers(init?.headers);
    const token = getToken();
    if (token !== null) {
      headers.set(STAFF_TOKEN_HEADER, token);
    }
    const response = await fetchImpl(input, { ...init, headers });
    if (response.status === 401) {
      onUnauthorized();
    }
    return response;
  };
}

export type TokenValidation = "valid" | "invalid" | "unreachable";

/**
 * Validates a candidate token before it is stored: one GET against the probe endpoint with the
 * X-Staff-Token header. 2xx → valid; 401 → invalid (ADR 024: absence/mismatch both 401; 403 is
 * structurally unreachable under the single shared secret); anything else (5xx, network failure)
 * → unreachable, so a down API host is reported distinctly from a wrong token.
 */
export async function validateStaffToken(
  candidate: string,
  fetchImpl: typeof fetch = fetch,
): Promise<TokenValidation> {
  try {
    const response = await fetchImpl(STAFF_PROBE_PATH, {
      headers: { [STAFF_TOKEN_HEADER]: candidate, Accept: "application/json" },
    });
    if (response.ok) return "valid";
    if (response.status === 401) return "invalid";
    return "unreachable";
  } catch {
    return "unreachable";
  }
}
