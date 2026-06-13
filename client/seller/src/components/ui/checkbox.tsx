import { forwardRef, type ComponentProps } from "react";

import { cn } from "@/lib/utils";

const Checkbox = forwardRef<HTMLInputElement, ComponentProps<"input">>(
  ({ className, ...props }, ref) => (
    <input
      type="checkbox"
      ref={ref}
      data-slot="checkbox"
      className={cn(
        "h-4 w-4 shrink-0 rounded border border-input shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      {...props}
    />
  ),
);
Checkbox.displayName = "Checkbox";

export { Checkbox };
