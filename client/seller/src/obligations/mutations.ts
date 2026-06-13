import { useMutation, useQueryClient } from "@tanstack/react-query";

import type { TrackingFormValues } from "@/obligations/formSchemas";

export function useProvideTracking(sellerId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      obligationId,
      values,
    }: {
      obligationId: string;
      values: TrackingFormValues;
    }) => {
      const response = await fetch("/api/obligations/tracking", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
        },
        body: JSON.stringify({
          obligationId,
          trackingNumber: values.trackingNumber,
        }),
      });
      if (!response.ok) {
        const text = await response.text().catch(() => "");
        throw new Error(
          text || `Provide tracking failed with ${response.status}.`,
        );
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ["sellerObligations", sellerId],
      });
    },
  });
}
