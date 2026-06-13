import { useState } from "react";

import { useSession } from "@/session/SessionContext";
import { useSellerObligations } from "@/obligations/queries";
import type { ObligationStatusView } from "@/obligations/schema";
import { ProvideTrackingDialog } from "@/obligations/ProvideTrackingDialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState, ErrorState } from "@/components/States";
import { formatUsd } from "@/lib/format";

const ACTIONABLE_STATUSES = new Set(["AwaitingShipment", "Escalated"]);

export function ObligationsPage() {
  const { participantId } = useSession();
  const { data, isPending, isError, error, refetch } = useSellerObligations(
    participantId ?? "",
  );

  if (isPending) {
    return <ObligationsSkeleton />;
  }

  if (isError) {
    return (
      <section>
        <h1 className="mb-4 text-xl font-semibold tracking-tight">
          Obligations
        </h1>
        <ErrorState
          message={error.message}
          onRetry={() => void refetch()}
        />
      </section>
    );
  }

  return (
    <section>
      <h1 className="mb-4 text-xl font-semibold tracking-tight">
        Obligations
      </h1>
      {data.length === 0 ? (
        <EmptyState message="No post-sale obligations yet." />
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {data.map((obligation) => (
            <li key={obligation.id}>
              <ObligationCard obligation={obligation} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

export function obligationStatusVariant(
  status: string,
): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case "Fulfilled":
      return "secondary";
    case "Escalated":
    case "Disputed":
      return "destructive";
    case "Shipped":
      return "default";
    case "AwaitingShipment":
    default:
      return "outline";
  }
}

function statusLabel(status: string): string {
  switch (status) {
    case "AwaitingShipment":
      return "Awaiting Shipment";
    case "Shipped":
      return "Shipped";
    case "Escalated":
      return "Overdue";
    case "Fulfilled":
      return "Fulfilled";
    case "Disputed":
      return "Disputed";
    default:
      return status;
  }
}

function formatRelativeDeadline(deadline: string): string {
  const deadlineDate = new Date(deadline);
  const now = new Date();
  const diffMs = deadlineDate.getTime() - now.getTime();

  if (diffMs <= 0) {
    return "Overdue";
  }

  const diffMinutes = Math.floor(diffMs / 60_000);
  if (diffMinutes < 1) return "less than a minute";
  if (diffMinutes < 60) return `in ${diffMinutes} minute${diffMinutes === 1 ? "" : "s"}`;
  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `in ${diffHours} hour${diffHours === 1 ? "" : "s"}`;
  const diffDays = Math.floor(diffHours / 24);
  return `in ${diffDays} day${diffDays === 1 ? "" : "s"}`;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function ObligationCard({ obligation }: { obligation: ObligationStatusView }) {
  const [trackingOpen, setTrackingOpen] = useState(false);
  const canProvideTracking = ACTIONABLE_STATUSES.has(obligation.status);

  return (
    <>
      <Card className="h-full">
        <CardHeader>
          <div className="flex items-start justify-between gap-2">
            <CardTitle className="text-base">
              {formatUsd(obligation.hammerPrice)} sale
            </CardTitle>
            <Badge variant={obligationStatusVariant(obligation.status)}>
              {statusLabel(obligation.status)}
            </Badge>
          </div>
          <p className="text-muted-foreground text-xs">
            Obligation {obligation.id.slice(0, 8)}…
          </p>
        </CardHeader>
        <CardContent className="space-y-2">
          <ObligationStatusDetail obligation={obligation} />

          {canProvideTracking && (
            <div className="pt-2">
              <Button size="sm" onClick={() => setTrackingOpen(true)}>
                Provide Tracking
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
      {trackingOpen && (
        <ProvideTrackingDialog
          obligation={obligation}
          onClose={() => setTrackingOpen(false)}
        />
      )}
    </>
  );
}

function ObligationStatusDetail({
  obligation,
}: {
  obligation: ObligationStatusView;
}) {
  switch (obligation.status) {
    case "AwaitingShipment":
      return (
        <div className="space-y-1.5">
          <p className="text-sm">
            Ship your item by{" "}
            <span className="font-medium">
              {formatRelativeDeadline(obligation.shipByDeadline)}
            </span>
          </p>
          {obligation.reminderSentAt && (
            <p className="text-sm font-medium text-amber-600">
              Reminder sent — ship before your deadline.
            </p>
          )}
        </div>
      );

    case "Escalated":
      return (
        <div className="space-y-1.5">
          <p className="text-destructive text-sm font-medium">
            Overdue — your deadline passed; this sale is under review.
          </p>
          {obligation.escalatedAt && (
            <p className="text-muted-foreground text-xs">
              Escalated {formatTimestamp(obligation.escalatedAt)}
            </p>
          )}
        </div>
      );

    case "Shipped":
      return (
        <div className="space-y-1.5">
          <p className="text-sm">
            Shipped — tracking #{obligation.trackingNumber}; delivery
            confirmation pending.
          </p>
          {obligation.trackingProvidedAt && (
            <p className="text-muted-foreground text-xs">
              Shipped {formatTimestamp(obligation.trackingProvidedAt)}
            </p>
          )}
        </div>
      );

    case "Fulfilled":
      return (
        <div className="space-y-1.5">
          <p className="text-sm font-medium text-green-600">Completed.</p>
          {obligation.fulfilledAt && (
            <p className="text-muted-foreground text-xs">
              Fulfilled {formatTimestamp(obligation.fulfilledAt)}
            </p>
          )}
        </div>
      );

    case "Disputed":
      return (
        <div className="space-y-1.5">
          {obligation.disputeResolution ? (
            <p className="text-sm">
              Dispute resolved ({obligation.disputeResolution}).
            </p>
          ) : (
            <p className="text-destructive text-sm font-medium">
              Dispute open ({obligation.disputeReason}).
            </p>
          )}
          {obligation.disputeOpenedAt && (
            <p className="text-muted-foreground text-xs">
              Opened {formatTimestamp(obligation.disputeOpenedAt)}
            </p>
          )}
          {obligation.disputeResolvedAt && (
            <p className="text-muted-foreground text-xs">
              Resolved {formatTimestamp(obligation.disputeResolvedAt)}
            </p>
          )}
        </div>
      );

    default:
      return null;
  }
}

export function useActionableObligationCount(): number {
  const { participantId } = useSession();
  const { data } = useSellerObligations(participantId ?? "");
  if (!data) return 0;
  return data.filter((o) => ACTIONABLE_STATUSES.has(o.status)).length;
}

function ObligationsSkeleton() {
  return (
    <section>
      <Skeleton className="mb-4 h-7 w-36" />
      <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 3 }).map((_, index) => (
          <li key={index}>
            <Card className="h-full">
              <CardHeader>
                <Skeleton className="h-5 w-3/4" />
              </CardHeader>
              <CardContent className="space-y-2">
                <Skeleton className="h-4 w-48" />
                <Skeleton className="h-4 w-32" />
              </CardContent>
            </Card>
          </li>
        ))}
      </ul>
    </section>
  );
}
