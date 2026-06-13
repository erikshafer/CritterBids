import { z } from "zod";

export const trackingFormSchema = z.object({
  trackingNumber: z.string().min(1, "Tracking number is required"),
});

export type TrackingFormValues = z.infer<typeof trackingFormSchema>;
