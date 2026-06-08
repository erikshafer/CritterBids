// Display formatters for the bidder surfaces. Money arrives as a JSON number (a backend decimal).
const usd = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
});

export function formatUsd(amount: number): string {
  return usd.format(amount);
}

// Map a listing Status to a shadcn Badge variant. Status is a backend string, not an enum
// (CatalogListingView.cs): Published → Open → Closed → Sold → Settled, with Passed / Withdrawn
// terminal branches.
export function statusVariant(
  status: string,
): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case "Sold":
    case "Settled":
      return "default";
    case "Open":
      return "secondary";
    case "Passed":
    case "Withdrawn":
      return "destructive";
    default:
      return "outline";
  }
}
