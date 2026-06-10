import type { ReactNode, ThHTMLAttributes, TdHTMLAttributes } from "react";

import { cn } from "@/lib/utils";

// Minimal projector-legible table primitives shared by the boards: generous row height,
// high-contrast header, no zebra noise. shadcn's table component isn't copied in (components
// are copied as surfaces need them — milestone §3); these few wrappers are all six boards need.

export function BoardTable({ children }: { children: ReactNode }) {
  return (
    <div className="border-border/60 overflow-x-auto rounded-lg border">
      <table className="w-full text-left text-base">{children}</table>
    </div>
  );
}

export function BoardTableHead({ children }: { children: ReactNode }) {
  return (
    <thead className="border-border/60 text-muted-foreground border-b text-sm uppercase tracking-wide">
      <tr>{children}</tr>
    </thead>
  );
}

export function Th({
  className,
  ...props
}: ThHTMLAttributes<HTMLTableCellElement>) {
  return <th className={cn("px-4 py-3 font-medium", className)} {...props} />;
}

export function BoardTableBody({ children }: { children: ReactNode }) {
  return <tbody className="divide-border/40 divide-y">{children}</tbody>;
}

export function Td({
  className,
  ...props
}: TdHTMLAttributes<HTMLTableCellElement>) {
  return <td className={cn("px-4 py-3 align-top", className)} {...props} />;
}

/** A status chip; tone classes come from the board (e.g. failed = destructive). */
export function StatusBadge({
  tone = "default",
  children,
}: {
  tone?: "default" | "positive" | "attention" | "destructive" | "muted";
  children: ReactNode;
}) {
  const tones = {
    default: "bg-accent text-accent-foreground",
    positive: "bg-emerald-950 text-emerald-300 border border-emerald-700",
    attention: "bg-amber-950 text-amber-300 border border-amber-700",
    destructive: "bg-red-950 text-red-300 border border-red-700",
    muted: "bg-muted text-muted-foreground",
  } as const;
  return (
    <span
      className={cn(
        "inline-block rounded-full px-2.5 py-0.5 text-sm font-medium whitespace-nowrap",
        tones[tone],
      )}
    >
      {children}
    </span>
  );
}
