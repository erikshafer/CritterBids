---
name: observability
description: "Observability in CritterBids: pre-production OTEL posture, audit tags, Grafana starting points, and telemetry suppression. Use when wiring traces, metrics, dashboards, or audit tags."
cluster: observability
tags: [observability, opentelemetry, metrics, grafana, auditing]
status: placeholder
---

# Observability

> 🟡 Placeholder scaffold for pre-production observability work.
> Generic OpenTelemetry, metrics, auditing, and dashboard mechanics live in ai-skills `wolverine-observability-*`;
> **this skill documents only the CritterBids-specific posture to start from.**

Extend this after the first real Hetzner/pre-production deployment produces concrete telemetry needs. Until then, keep it thin.

## When to apply this skill

Use this skill when:

- Wiring OpenTelemetry traces or metrics into `CritterBids.Api`.
- Adding a Prometheus scrape endpoint or first Grafana dashboard.
- Choosing CritterBids message properties for `[Audit]` tags.
- Suppressing noisy health-check, readiness, heartbeat, or keep-alive telemetry.

Do NOT use this skill for command-line diagnostics (`wolverine-message-handlers` and `critter-stack-testing-patterns` cover the in-context commands) or Marten async-daemon tuning (`marten-event-sourcing`).

## Read upstream first

Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of the generic observability mechanics:

1. `wolverine-observability-opentelemetry-setup` — Wolverine/Marten tracing and metrics registration.
2. `wolverine-observability-metrics-and-auditing` — metric names, `[Audit]`, custom tags, and audit trails.
3. `wolverine-observability-grafana-dashboard-templates` — starter Prometheus/Grafana dashboards and alerting ideas.

This skill picks up at CritterBids' service name, deployment posture, audit candidates, and noise-reduction rules.

## CritterBids posture

| Concern | Current answer |
|---|---|
| Local dev traces | Aspire dashboard already surfaces OTEL spans |
| Production target | Hetzner VPS; traces via OTLP, metrics via Prometheus/Grafana |
| API service name | `CritterBids.Api` unless `WolverineOptions.ServiceName` is set explicitly |
| Wolverine meter | `Wolverine:CritterBids.Api` by default; verify startup output |
| Marten meter | `Marten` |
| Health/ready/metrics noise | Suppress at source where possible |
| Full dashboard/alerts | Placeholder until real deployment data exists |

Aspire local development does not need extra OTEL plumbing just to see traces. Production needs explicit collection/storage: Wolverine + Marten emit spans/counters, the API exports them, and Prometheus/Grafana (plus a trace backend such as Tempo/Jaeger/Honeycomb) stores and renders them.

## Minimal registration posture

The upstream setup skill owns the full mechanics. For CritterBids, keep these project-specific names in mind:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Wolverine")
        .AddSource("Marten")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("Wolverine:CritterBids.Api")
        .AddMeter("Marten")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .UseOtlpExporter();
```

If `WolverineOptions.ServiceName` changes, the Wolverine meter suffix changes too. A misspelled meter gives zero metric data without an obvious error.

For Marten event counters, add the store option when production metrics are actually being wired:

```csharp
services.ConfigureMarten(opts =>
{
    opts.OpenTelemetry.TrackConnections = TrackLevel.Normal;
    opts.OpenTelemetry.TrackEventCounters();
});
```

Leave verbose SQL/span tracking off by default; enable it only for targeted diagnosis.

## Audit-tag posture

Use `[Audit]` for stable identifiers that make a support report or demo incident searchable. Do not tag PII, free text, or high-cardinality noise that is not operationally useful.

First-pass candidates:

| BC | Message type | Candidate tags |
|---|---|---|
| Auctions | `PlaceBid` | `AuctionId`, `ListingId`, `BidderParticipantId` |
| Auctions | `CloseAuction` | `AuctionId`, `ListingId` |
| Selling | listing submission/approval/rejection commands | `ListingId`, `SellerId` where present |
| Settlement | commission/settlement commands | `AuctionId`, `SettlementId`, `ParticipantId` |
| Relay | outbound webhook commands | `SubscriberId`, correlation ID |

Add attributes as messages are authored or touched; do not run a blind sweep.

## Telemetry noise suppression

Suppress high-volume, low-information telemetry at the source:

```csharp
[WolverineLogging(telemetryEnabled: false)]
[WolverineGet("/health")]
public static string Health() => "OK";

[WolverineLogging(telemetryEnabled: false)]
[WolverineGet("/ready")]
public static string Ready() => "OK";
```

For listeners that are operational heartbeats rather than domain work:

```csharp
opts.ListenToRabbitQueue("heartbeat").TelemetryEnabled(false);
```

If node-assignment health checks drown out useful traces during quiet periods, prefer sampling or disabling that specific source rather than filtering later in Grafana.

## Starter dashboard questions

Before building full dashboards, answer these questions with the upstream Grafana templates:

- Are Wolverine handlers succeeding, failing, or hitting DLQ?
- Are inbox/outbox/scheduled envelope backlogs growing?
- Is `PlaceBid` or auction-close handling slow at p95/p99?
- Are Marten appends and projection lag healthy during bidding bursts?
- Can a support report for a listing, bidder, or settlement ID be found by tags?

Prometheus scrape endpoint `/metrics` must not be publicly reachable on the VPS; restrict it to the scraper network path.

## Common pitfalls

- **Meter name typo.** `Wolverine` is the ActivitySource; metrics use `Wolverine:{ServiceName}`.
- **Premature dashboard completeness.** Keep this placeholder lean until production traffic shows what matters.
- **Over-tagging.** Audit tags are for searchable identifiers, not payload dumping.
- **Health-check trace spam.** Suppress or sample periodic checks at source.
- **Public `/metrics`.** Treat metric cardinality as operational data, not a public endpoint.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-observability-opentelemetry-setup` — OTEL registration and tracing modes.
- `wolverine-observability-metrics-and-auditing` — Wolverine metrics, `[Audit]`, custom tags, audit trails.
- `wolverine-observability-grafana-dashboard-templates` — dashboards and alert starter material.

**Prerequisites:**

- `aspire` — local dashboard posture and OTLP environment injection.
- `wolverine-message-handlers` — handler diagnostics and `[WolverineLogging]` context.

**Downstream:**

- `integration-messaging` — RabbitMQ endpoint posture and listener suppression.
- `wolverine-sagas` — saga IDs and auction-close observability candidates.
- `marten-event-sourcing` — Marten append counters and async daemon considerations.

**External:**

- ADR 011 in [`docs/decisions/`](../../decisions/) — all-Marten storage posture.
- [`CLAUDE.md`](../../../CLAUDE.md) § Quick Start and Canonical Bootstrap Sequence.
