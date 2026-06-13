import { queryOptions, useQuery } from "@tanstack/react-query";

import {
  obligationStatusListSchema,
  type ObligationStatusView,
} from "@/obligations/schema";

export function sellerObligationsQueryOptions(sellerId: string) {
  return queryOptions({
    queryKey: ["sellerObligations", sellerId],
    queryFn: async (): Promise<ObligationStatusView[]> => {
      const response = await fetch(
        `/api/obligations/status?sellerId=${encodeURIComponent(sellerId)}`,
        { headers: { Accept: "application/json" } },
      );
      if (!response.ok) {
        throw new Error(
          `Obligations request failed with ${response.status}.`,
        );
      }
      return obligationStatusListSchema.parse(await response.json());
    },
    enabled: sellerId.length > 0,
  });
}

export function useSellerObligations(sellerId: string) {
  return useQuery(sellerObligationsQueryOptions(sellerId));
}
