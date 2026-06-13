import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import { useSession } from "@/session/SessionContext";
import { useEditDraft } from "@/listings/mutations";
import { editDraftSchema, type EditDraftFormValues } from "@/listings/formSchemas";
import type { SellerListingSummary } from "@/listings/schema";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

const resolver = zodResolver(editDraftSchema);

export function EditDraftDialog({
  listing,
  onClose,
}: {
  listing: SellerListingSummary;
  onClose: () => void;
}) {
  const { participantId } = useSession();
  const mutation = useEditDraft(participantId ?? "");

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<EditDraftFormValues>({
    resolver,
    defaultValues: {
      listingId: listing.id,
      title: listing.title,
      reservePrice: listing.reservePrice != null ? String(listing.reservePrice) : "",
      buyItNowPrice: listing.buyItNowPrice != null ? String(listing.buyItNowPrice) : "",
    },
  });

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const onSubmit = handleSubmit((values) => {
    mutation.mutate(values, { onSuccess: onClose });
  });

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
      role="dialog"
      aria-modal="true"
      aria-label={`Edit ${listing.title}`}
    >
      <div className="mx-4 w-full max-w-md rounded-lg border border-border bg-background p-6 shadow-lg">
        <h2 className="mb-4 text-lg font-semibold">Edit Draft</h2>
        <form onSubmit={onSubmit} className="space-y-4" aria-label="Edit draft listing">
          <div className="space-y-1.5">
            <Label htmlFor="edit-title">Title</Label>
            <Input id="edit-title" {...register("title")} />
            {errors.title && (
              <p className="text-destructive text-xs">{errors.title.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="edit-reservePrice">Reserve Price ($)</Label>
            <Input
              id="edit-reservePrice"
              type="number"
              inputMode="decimal"
              min="0"
              step="0.01"
              {...register("reservePrice")}
            />
            {errors.reservePrice && (
              <p className="text-destructive text-xs">
                {errors.reservePrice.message}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="edit-buyItNowPrice">Buy It Now Price ($)</Label>
            <Input
              id="edit-buyItNowPrice"
              type="number"
              inputMode="decimal"
              min="0"
              step="0.01"
              {...register("buyItNowPrice")}
            />
            {errors.buyItNowPrice && (
              <p className="text-destructive text-xs">
                {errors.buyItNowPrice.message}
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
              {mutation.isPending ? "Saving..." : "Save Changes"}
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
