import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import { useSession } from "@/session/SessionContext";
import { useProvideTracking } from "@/obligations/mutations";
import {
  trackingFormSchema,
  type TrackingFormValues,
} from "@/obligations/formSchemas";
import type { ObligationStatusView } from "@/obligations/schema";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formatUsd } from "@/lib/format";

const resolver = zodResolver(trackingFormSchema);

export function ProvideTrackingDialog({
  obligation,
  onClose,
}: {
  obligation: ObligationStatusView;
  onClose: () => void;
}) {
  const { participantId } = useSession();
  const mutation = useProvideTracking(participantId ?? "");

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<TrackingFormValues>({
    resolver,
    defaultValues: { trackingNumber: "" },
  });

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const onSubmit = handleSubmit((values) => {
    mutation.mutate(
      { obligationId: obligation.id, values },
      { onSuccess: onClose },
    );
  });

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
      role="dialog"
      aria-modal="true"
      aria-label="Provide tracking information"
    >
      <div className="mx-4 w-full max-w-md rounded-lg border border-border bg-background p-6 shadow-lg">
        <h2 className="mb-1 text-lg font-semibold">Provide Tracking</h2>
        <p className="text-muted-foreground mb-4 text-sm">
          {formatUsd(obligation.hammerPrice)} sale — obligation{" "}
          {obligation.id.slice(0, 8)}…
        </p>
        <form
          onSubmit={onSubmit}
          className="space-y-4"
          aria-label="Provide tracking"
        >
          <div className="space-y-1.5">
            <Label htmlFor="trackingNumber">Tracking Number</Label>
            <Input
              id="trackingNumber"
              {...register("trackingNumber")}
              placeholder="e.g. 1Z999AA10123456784"
              autoFocus
            />
            {errors.trackingNumber && (
              <p className="text-destructive text-xs">
                {errors.trackingNumber.message}
              </p>
            )}
          </div>

          {mutation.isError && (
            <p className="text-destructive text-xs" role="alert">
              {mutation.error.message}
            </p>
          )}

          <div className="flex gap-3 pt-2">
            <Button
              type="submit"
              disabled={isSubmitting || mutation.isPending}
            >
              {mutation.isPending ? "Submitting..." : "Submit Tracking"}
            </Button>
            <Button type="button" variant="outline" onClick={onClose}>
              Cancel
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
