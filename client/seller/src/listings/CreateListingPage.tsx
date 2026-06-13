import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useNavigate } from "@tanstack/react-router";

import { useSession } from "@/session/SessionContext";
import { useCreateDraft } from "@/listings/mutations";
import {
  createDraftSchema,
  DURATION_PRESETS,
  EXTENDED_BIDDING_PRESETS,
  type CreateDraftFormValues,
} from "@/listings/formSchemas";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

const defaultValues: CreateDraftFormValues = {
  title: "",
  format: "Flash",
  startingBid: "",
  reservePrice: "",
  buyItNowPrice: "",
  duration: "",
  extendedBiddingEnabled: false,
  extendedBiddingTriggerWindow: "",
  extendedBiddingExtension: "",
};

const resolver = zodResolver(createDraftSchema);

export function CreateListingPage() {
  const { participantId } = useSession();
  const navigate = useNavigate();
  const mutation = useCreateDraft(participantId ?? "");

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<CreateDraftFormValues>({
    resolver,
    defaultValues,
    shouldUnregister: true,
  });

  const format = watch("format");
  const extendedBiddingEnabled = watch("extendedBiddingEnabled");

  const onSubmit = handleSubmit((values) => {
    mutation.mutate(values, {
      onSuccess: () => void navigate({ to: "/listings" }),
    });
  });

  return (
    <section className="mx-auto max-w-2xl">
      <h1 className="mb-6 text-xl font-semibold tracking-tight">
        Create New Listing
      </h1>

      <Card>
        <CardHeader>
          <CardTitle>Listing Details</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-5" aria-label="Create listing">
            <div className="space-y-1.5">
              <Label htmlFor="title">Title</Label>
              <Input
                id="title"
                {...register("title")}
                placeholder="e.g. Vintage Mechanical Keyboard"
              />
              {errors.title && (
                <p className="text-destructive text-xs">{errors.title.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="format">Auction Format</Label>
              <Select id="format" {...register("format")}>
                <option value="Flash">Flash</option>
                <option value="Timed">Timed</option>
              </Select>
              {errors.format && (
                <p className="text-destructive text-xs">{errors.format.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="startingBid">Starting Bid ($)</Label>
              <Input
                id="startingBid"
                type="number"
                inputMode="decimal"
                min="0"
                step="0.01"
                {...register("startingBid")}
              />
              {errors.startingBid && (
                <p className="text-destructive text-xs">{errors.startingBid.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="reservePrice">Reserve Price ($) — optional</Label>
              <Input
                id="reservePrice"
                type="number"
                inputMode="decimal"
                min="0"
                step="0.01"
                {...register("reservePrice")}
              />
              {errors.reservePrice && (
                <p className="text-destructive text-xs">{errors.reservePrice.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="buyItNowPrice">Buy It Now Price ($) — optional</Label>
              <Input
                id="buyItNowPrice"
                type="number"
                inputMode="decimal"
                min="0"
                step="0.01"
                {...register("buyItNowPrice")}
              />
              {errors.buyItNowPrice && (
                <p className="text-destructive text-xs">{errors.buyItNowPrice.message}</p>
              )}
            </div>

            {format === "Timed" && (
              <div className="space-y-1.5">
                <Label htmlFor="duration">Duration</Label>
                <Select id="duration" {...register("duration")}>
                  <option value="">Select duration...</option>
                  {DURATION_PRESETS.map((preset) => (
                    <option key={preset.value} value={preset.value}>
                      {preset.label}
                    </option>
                  ))}
                </Select>
                {errors.duration && (
                  <p className="text-destructive text-xs">{errors.duration.message}</p>
                )}
              </div>
            )}

            <div className="flex items-center gap-2">
              <Checkbox
                id="extendedBiddingEnabled"
                {...register("extendedBiddingEnabled")}
              />
              <Label htmlFor="extendedBiddingEnabled">
                Enable extended bidding
              </Label>
            </div>

            {extendedBiddingEnabled && (
              <>
                <div className="space-y-1.5">
                  <Label htmlFor="extendedBiddingTriggerWindow">
                    Trigger Window
                  </Label>
                  <Select
                    id="extendedBiddingTriggerWindow"
                    {...register("extendedBiddingTriggerWindow")}
                  >
                    <option value="">Select trigger window...</option>
                    {EXTENDED_BIDDING_PRESETS.map((preset) => (
                      <option key={preset.value} value={preset.value}>
                        {preset.label}
                      </option>
                    ))}
                  </Select>
                  {errors.extendedBiddingTriggerWindow && (
                    <p className="text-destructive text-xs">
                      {errors.extendedBiddingTriggerWindow.message}
                    </p>
                  )}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="extendedBiddingExtension">Extension</Label>
                  <Select
                    id="extendedBiddingExtension"
                    {...register("extendedBiddingExtension")}
                  >
                    <option value="">Select extension...</option>
                    {EXTENDED_BIDDING_PRESETS.map((preset) => (
                      <option key={preset.value} value={preset.value}>
                        {preset.label}
                      </option>
                    ))}
                  </Select>
                  {errors.extendedBiddingExtension && (
                    <p className="text-destructive text-xs">
                      {errors.extendedBiddingExtension.message}
                    </p>
                  )}
                </div>
              </>
            )}

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
                {mutation.isPending ? "Creating..." : "Create Draft"}
              </Button>
              <Button
                type="button"
                variant="outline"
                onClick={() => void navigate({ to: "/listings" })}
              >
                Cancel
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </section>
  );
}
