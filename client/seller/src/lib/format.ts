const usd = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
});

export function formatUsd(amount: number): string {
  return usd.format(amount);
}

export function sellerStatusVariant(
  status: string,
): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case "Published":
      return "secondary";
    case "Rejected":
    case "Withdrawn":
      return "destructive";
    case "Draft":
    case "Submitted":
    default:
      return "outline";
  }
}
