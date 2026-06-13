import { z } from "zod";

const DURATION_PRESETS = [
  { label: "1 hour", value: "01:00:00" },
  { label: "3 hours", value: "03:00:00" },
  { label: "12 hours", value: "12:00:00" },
  { label: "1 day", value: "1.00:00:00" },
  { label: "3 days", value: "3.00:00:00" },
  { label: "7 days", value: "7.00:00:00" },
] as const;

const EXTENDED_BIDDING_PRESETS = [
  { label: "15 seconds", value: "00:00:15" },
  { label: "30 seconds", value: "00:00:30" },
  { label: "1 minute", value: "00:01:00" },
  { label: "2 minutes", value: "00:02:00" },
  { label: "5 minutes", value: "00:05:00" },
] as const;

export { DURATION_PRESETS, EXTENDED_BIDDING_PRESETS };

export const createDraftSchema = z
  .object({
    title: z.string().min(1, "Title is required"),
    format: z.enum(["Flash", "Timed"]),
    startingBid: z.string().min(1, "Starting bid is required"),
    reservePrice: z.string(),
    buyItNowPrice: z.string(),
    duration: z.string(),
    extendedBiddingEnabled: z.boolean(),
    extendedBiddingTriggerWindow: z.string(),
    extendedBiddingExtension: z.string(),
  })
  .superRefine((data, ctx) => {
    const startingBid = Number(data.startingBid);
    if (!Number.isFinite(startingBid) || startingBid <= 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Starting bid must be a positive number",
        path: ["startingBid"],
      });
    }

    if (data.reservePrice !== "") {
      const reserve = Number(data.reservePrice);
      if (!Number.isFinite(reserve) || reserve <= 0) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Reserve price must be a positive number",
          path: ["reservePrice"],
        });
      }
    }

    if (data.buyItNowPrice !== "") {
      const bin = Number(data.buyItNowPrice);
      if (!Number.isFinite(bin) || bin <= 0) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Buy It Now price must be a positive number",
          path: ["buyItNowPrice"],
        });
      }
    }

    if (data.format === "Timed" && !data.duration) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Duration is required for Timed listings",
        path: ["duration"],
      });
    }

    const reserve = data.reservePrice ? Number(data.reservePrice) : undefined;
    const bin = data.buyItNowPrice ? Number(data.buyItNowPrice) : undefined;
    if (
      reserve !== undefined &&
      bin !== undefined &&
      Number.isFinite(reserve) &&
      Number.isFinite(bin) &&
      bin < reserve
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Buy It Now price must be at least the reserve price",
        path: ["buyItNowPrice"],
      });
    }

    if (data.extendedBiddingEnabled) {
      if (!data.extendedBiddingTriggerWindow) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Trigger window is required when extended bidding is enabled",
          path: ["extendedBiddingTriggerWindow"],
        });
      }
      if (!data.extendedBiddingExtension) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Extension is required when extended bidding is enabled",
          path: ["extendedBiddingExtension"],
        });
      }
    }
  });

export type CreateDraftFormValues = z.infer<typeof createDraftSchema>;

export const editDraftSchema = z
  .object({
    listingId: z.string(),
    title: z.string(),
    reservePrice: z.string(),
    buyItNowPrice: z.string(),
  })
  .superRefine((data, ctx) => {
    if (data.reservePrice !== "") {
      const reserve = Number(data.reservePrice);
      if (!Number.isFinite(reserve) || reserve <= 0) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Reserve price must be a positive number",
          path: ["reservePrice"],
        });
      }
    }

    if (data.buyItNowPrice !== "") {
      const bin = Number(data.buyItNowPrice);
      if (!Number.isFinite(bin) || bin <= 0) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Buy It Now price must be a positive number",
          path: ["buyItNowPrice"],
        });
      }
    }

    const reserve = data.reservePrice ? Number(data.reservePrice) : undefined;
    const bin = data.buyItNowPrice ? Number(data.buyItNowPrice) : undefined;
    if (
      reserve !== undefined &&
      bin !== undefined &&
      Number.isFinite(reserve) &&
      Number.isFinite(bin) &&
      bin < reserve
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Buy It Now price must be at least the reserve price",
        path: ["buyItNowPrice"],
      });
    }
  });

export type EditDraftFormValues = z.infer<typeof editDraftSchema>;
