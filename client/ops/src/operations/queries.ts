import { queryOptions, useQuery } from "@tanstack/react-query";
import type { ZodType } from "zod";

import { useStaffAuth } from "@/auth/StaffAuthContext";
import type { StaffFetch } from "@/auth/staffApi";
import { listingKey, operationsKeys } from "@/operations/keys";
import {
  bidActivitySchema,
  listingTitleSchema,
  lotBoardSchema,
  obligationsSchema,
  participantsSchema,
  sessionsSchema,
  settlementQueueSchema,
} from "@/operations/schema";

// The board query layer: every staff read goes through staffFetch (X-Staff-Token attached,
// every 401 funnelled into clear-token + re-gate — ADR 024), is Zod-parsed at the wire boundary
// (ADR 013), and lives under the ["operations", …] key family the cache bridge invalidates
// (ADR 026). Same-origin relative paths — the Vite dev proxy (ADR 025) owns dev reachability.
//
// The query-options factories take staffFetch explicitly so they are unit-testable with a fake;
// the use* hooks bind them to the app's StaffAuthContext instance (stable identity for the
// provider's lifetime — safe as a queryFn dependency).

async function fetchBoard<T>(
  staffFetch: StaffFetch,
  url: string,
  schema: ZodType<T>,
): Promise<T> {
  const response = await staffFetch(url, {
    headers: { Accept: "application/json" },
  });
  if (!response.ok) {
    throw new Error(`Request to ${url} failed with ${response.status}.`);
  }
  return schema.parse(await response.json());
}

// Every board is push-fed (M8-S6b completed the ops feed: each Operations-consumed integration
// event has an OperationsFeedNotification, enforced by the backend topology test) — no query
// here polls. Reconnection recovery is the SignalRProvider's onreconnected one-shot
// ["operations"]-family invalidation, the same push-equals-re-query authority rule (ADR 026).

export function lotBoardQueryOptions(staffFetch: StaffFetch) {
  return queryOptions({
    queryKey: operationsKeys.lotBoard,
    queryFn: () =>
      fetchBoard(staffFetch, "/api/operations/lot-board", lotBoardSchema),
  });
}

export function bidActivityQueryOptions(staffFetch: StaffFetch) {
  return queryOptions({
    queryKey: operationsKeys.bidActivity,
    queryFn: () =>
      fetchBoard(
        staffFetch,
        "/api/operations/bid-activity",
        bidActivitySchema,
      ),
  });
}

export function settlementQueueQueryOptions(staffFetch: StaffFetch) {
  return queryOptions({
    queryKey: operationsKeys.settlementQueue,
    queryFn: () =>
      fetchBoard(
        staffFetch,
        "/api/operations/settlement-queue",
        settlementQueueSchema,
      ),
  });
}

export function escalationsQueryOptions(staffFetch: StaffFetch) {
  return queryOptions({
    queryKey: operationsKeys.escalations,
    queryFn: () =>
      fetchBoard(
        staffFetch,
        "/api/operations/obligations/escalations",
        obligationsSchema,
      ),
  });
}

export function disputesQueryOptions(staffFetch: StaffFetch) {
  return queryOptions({
    queryKey: operationsKeys.disputes,
    queryFn: () =>
      fetchBoard(
        staffFetch,
        "/api/operations/obligations/disputes",
        obligationsSchema,
      ),
  });
}

export function sessionsQueryOptions(staffFetch: StaffFetch) {
  return queryOptions({
    queryKey: operationsKeys.sessions,
    queryFn: () =>
      fetchBoard(staffFetch, "/api/operations/sessions", sessionsSchema),
  });
}

export function participantsQueryOptions(staffFetch: StaffFetch) {
  return queryOptions({
    queryKey: operationsKeys.participants,
    queryFn: () =>
      fetchBoard(
        staffFetch,
        "/api/operations/participants",
        participantsSchema,
      ),
  });
}

export function useLotBoard() {
  const { staffFetch } = useStaffAuth();
  return useQuery(lotBoardQueryOptions(staffFetch));
}

export function useBidActivity() {
  const { staffFetch } = useStaffAuth();
  return useQuery(bidActivityQueryOptions(staffFetch));
}

export function useSettlementQueue() {
  const { staffFetch } = useStaffAuth();
  return useQuery(settlementQueueQueryOptions(staffFetch));
}

export function useEscalations() {
  const { staffFetch } = useStaffAuth();
  return useQuery(escalationsQueryOptions(staffFetch));
}

export function useDisputes() {
  const { staffFetch } = useStaffAuth();
  return useQuery(disputesQueryOptions(staffFetch));
}

export function useSessions() {
  const { staffFetch } = useStaffAuth();
  return useQuery(sessionsQueryOptions(staffFetch));
}

export function useParticipants() {
  const { staffFetch } = useStaffAuth();
  return useQuery(participantsQueryOptions(staffFetch));
}

/**
 * The render-time Title join (milestone §2 carry-over from M7): the bid-activity, settlement,
 * and obligations records carry ListingId only; the lot board's own Title is nullable until a
 * ListingPublished is folded. Titles resolve from `GET /api/listings/{id}` — an [AllowAnonymous]
 * read, fetched bare (no staff header needed) — cached per ["listing", id] (the key the cache
 * bridge invalidates on ListingRevised) and deduplicated across rows by TanStack Query.
 *
 * Resolves to `null` on a 404 — a missing listing is a stable answer rendered as an id
 * fallback, never retried (wolverine-http-frontend-contract §5).
 */
export function listingTitleQueryOptions(
  listingId: string,
  fetchImpl: typeof fetch = fetch,
) {
  return queryOptions({
    queryKey: listingKey(listingId),
    queryFn: async (): Promise<string | null> => {
      const response = await fetchImpl(`/api/listings/${listingId}`, {
        headers: { Accept: "application/json" },
      });
      if (response.status === 404) {
        return null;
      }
      if (!response.ok) {
        throw new Error(
          `Title lookup for listing ${listingId} failed with ${response.status}.`,
        );
      }
      return listingTitleSchema.parse(await response.json()).title;
    },
    staleTime: 5 * 60_000, // titles change rarely; ListingRevised invalidates explicitly
  });
}

export function useListingTitle(listingId: string) {
  return useQuery(listingTitleQueryOptions(listingId));
}
