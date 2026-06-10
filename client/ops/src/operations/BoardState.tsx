import type { ReactNode } from "react";
import type { UseQueryResult } from "@tanstack/react-query";

import { Card, CardContent } from "@/components/ui/card";

// The three non-data states every board shares. An empty array is a DESIGNED state — the
// []-never-404 read contract makes "nothing here" a normal answer, not an error
// (wolverine-http-frontend-contract §5).

export interface BoardStateProps<T> {
  query: UseQueryResult<readonly T[]>;
  /** Board-specific empty-state copy, e.g. "No disputes are open." */
  emptyMessage: string;
  /** Renders the board once rows exist. */
  children: (rows: readonly T[]) => ReactNode;
}

export function BoardState<T>({
  query,
  emptyMessage,
  children,
}: BoardStateProps<T>) {
  if (query.isPending) {
    return (
      <Card>
        <CardContent>
          <p className="text-muted-foreground py-4 text-base">Loading…</p>
        </CardContent>
      </Card>
    );
  }

  if (query.isError) {
    return (
      <Card>
        <CardContent>
          <p className="text-destructive py-4 text-base" role="alert">
            Could not load this board: {query.error.message}
          </p>
        </CardContent>
      </Card>
    );
  }

  if (query.data.length === 0) {
    return (
      <Card>
        <CardContent>
          <p className="text-muted-foreground py-4 text-base">
            {emptyMessage}
          </p>
        </CardContent>
      </Card>
    );
  }

  return <>{children(query.data)}</>;
}
