import type { ComponentProps } from "react";

import { cn } from "@/lib/utils";

// shadcn/ui Skeleton primitive (copied in for M8-S2 — catalog/detail loading states).
function Skeleton({ className, ...props }: ComponentProps<"div">) {
  return (
    <div
      data-slot="skeleton"
      className={cn("bg-accent animate-pulse rounded-md", className)}
      {...props}
    />
  );
}

export { Skeleton };
