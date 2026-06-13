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

export function auctionStatusVariant(
  status: string,
): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case "Open":
    case "Extended":
      return "default";
    case "Sold":
    case "Settled":
      return "secondary";
    case "Passed":
    case "Withdrawn":
    case "Closed":
      return "destructive";
    default:
      return "outline";
  }
}
