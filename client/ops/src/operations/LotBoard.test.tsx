import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { StaffAuthProvider } from "@/auth/StaffAuthContext";
import { LotBoard } from "@/operations/LotBoard";
import { listingKey, operationsKeys } from "@/operations/keys";
import type { LotBoardRow } from "@/operations/schema";

// Board render tests with a SEEDED query cache (staleTime: Infinity, no refetch) — no fetch
// fires, so neither a fetch stub nor jsdom networking is involved. The query functions
// themselves are covered in queries.test.ts; the live smoke covers the real wire.

const openRow: LotBoardRow = {
  listingId: "11111111-0000-0000-0000-000000000001",
  sellerId: "22222222-0000-0000-0000-000000000002",
  title: "Boxed vintage synthesizer",
  format: "Flash",
  startingBid: 10,
  reservePrice: null,
  buyItNow: null,
  feePercentage: 0.1,
  scheduledCloseAt: "2026-06-10T16:00:00Z",
  currentBid: 42,
  bidCount: 3,
  hammerPrice: null,
  winnerId: null,
  passReason: null,
  withdrawnBy: null,
  withdrawalReason: null,
  status: "Open",
  lastUpdatedAt: "2026-06-10T15:04:05Z",
};

// A pre-publish row: the view's own Title is null until ListingPublished folds in — the
// render-time join supplies the display title (M8-S6 prompt finding 3).
const untitledRow: LotBoardRow = {
  ...openRow,
  listingId: "33333333-0000-0000-0000-000000000003",
  title: null,
  status: "Draft",
  currentBid: null,
  bidCount: 0,
};

function renderLotBoard(rows: LotBoardRow[]) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { staleTime: Infinity, retry: false } },
  });
  queryClient.setQueryData(operationsKeys.lotBoard, rows);
  // Seed the title-join entry the untitled row resolves through.
  queryClient.setQueryData<string | null>(
    listingKey(untitledRow.listingId),
    "Hand-thrown ceramic mug",
  );

  render(
    <QueryClientProvider client={queryClient}>
      <StaffAuthProvider>
        <LotBoard />
      </StaffAuthProvider>
    </QueryClientProvider>,
  );
}

describe("LotBoard", () => {
  it("renders rows: own title, status badge, current bid, and the title-join fallback for a null Title", () => {
    renderLotBoard([openRow, untitledRow]);

    expect(
      screen.getByRole("heading", { name: "Lot board" }),
    ).toBeInTheDocument();
    // The view's own Title renders directly.
    expect(screen.getByText("Boxed vintage synthesizer")).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
    expect(screen.getByText("$42.00")).toBeInTheDocument();
    // The null-Title row renders through the ["listing", id] join.
    expect(screen.getByText("Hand-thrown ceramic mug")).toBeInTheDocument();
    expect(screen.getByText("Draft")).toBeInTheDocument();
  });

  it("renders the designed empty state from [] (never an error)", () => {
    renderLotBoard([]);

    expect(
      screen.getByText(
        "No listings are on the board yet — the lot board fills as listings publish.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
