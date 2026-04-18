# Observability — OpenTelemetry, Prometheus, Grafana

Scaffold for wiring OpenTelemetry traces and metrics, exporting metrics to Prometheus, and sketching
starter Grafana queries for CritterBids. This skill is intentionally a minimal foundation — it
answers *"what do I register, what do I export, and where do I look for more?"* so the first real
pass at production observability doesn't start from zero. Deeper content (full dashboards, alerting
rules, CritterWatch integration, advanced tuning) is tracked in §10 as explicit extension points
against the JasperFx ai-skills source at `C:\Code\JasperFx\ai-skills\wolverine\observability\`.

> **Status: 🟡 Placeholder — useful stub exists, extend during the first pre-production observability
> pass.** Authored 2026-04-18 drawing on the ai-skills source files
> (`opentelemetry-setup.md`, `metrics-and-auditing.md`, `grafana-dashboard-templates.md`) and
> verified against `src/Wolverine/MetricsConstants.cs` in the Wolverine repo. Flip to ✅ after
> the first real deploy to Hetzner produces concrete telemetry needs and those are folded back in.

---

## 1. Scope

Load this skill when:
- Wiring OpenTelemetry traces or metrics into `CritterBids.Api` or a BC module
- Adding a Prometheus scrape endpoint for production
- Choosing which message types deserve `[Audit]` tags
- Deciding which endpoints should have telemetry suppressed (health checks, keep-alives)
- Writing the first Grafana dashboard or alerting rule

Out of scope for this scaffold — see §10 for pointers:
- Full Grafana dashboard JSON templates
- Prometheus alerting rule files
- Async daemon deep-tuning via OTEL (covered partially in `marten-event-sourcing.md` §Async Daemon Configuration)
- `InvokeTracingMode.Full` and mediator-mode tracing semantics
- CritterWatch integration when that tooling ships from JasperFx

---

## 2. What Aspire Already Gives Us

Local development does not need any of this skill. The Aspire dashboard at
`http://localhost:15237` already surfaces:

- Structured logs from every Aspire-managed resource
- OpenTelemetry traces across services (HTTP → Wolverine → Marten spans linked into one timeline)
- Per-resource health and environment variable snapshots

Aspire provides the OTLP endpoint to every registered project through the
`OTEL_EXPORTER_OTLP_ENDPOINT` environment variable — the API host doesn't have to configure an
exporter URL for local dev. This is the baseline `aspire.md` describes.

What Aspire does **not** give us and what this skill covers:

| Concern | Aspire dev | This skill |
|---|---|---|
| Traces during local dev | ✅ in dashboard | — |
| Metrics during local dev | ⚠️ not surfaced in dashboard meaningfully | `AddMeter` registration |
| Production trace destination | ❌ dashboard is dev-only | OTLP → collector (Tempo / Jaeger / Honeycomb) |
| Prometheus scrape endpoint | ❌ | `/metrics` via `AddPrometheusExporter` |
| Long-term metric storage | ❌ | Prometheus + Grafana on the Hetzner VPS |
| Business-context tags | ❌ | `[Audit]` and tagging middleware |

The production story on Hetzner is: Wolverine + Marten emit spans and counters → the API host
exports both via OTLP for traces and via Prometheus scrape for metrics → a collector (or direct
Tempo + Prometheus + Grafana stack) stores and renders them.

---

## 3. OTEL Registration

Wolverine's `ActivitySource` name is `"Wolverine"`. Marten's is `"Marten"`. Both are stable
constants — register them verbatim.

**The Meter name is different.** Wolverine's Meter name is `"Wolverine:{WolverineOptions.ServiceName}"`.
The service name defaults to the host's assembly name. For `CritterBids.Api` that means the Meter
name is `"Wolverine:CritterBids.Api"` — not `"Wolverine"`. Marten's Meter name is just `"Marten"`.

This is the #1 footgun. A misspelled Meter name produces no error; you just get zero metric data.
**Verify against the Wolverine startup console output** — it prints the effective Meter name. If
`WolverineOptions.ServiceName` is set explicitly in `Program.cs`, the suffix follows that value.

Minimal registration for the API host:

```csharp
// In CritterBids.Api/Program.cs (or an extension method called from it)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("Wolverine")
            .AddSource("Marten")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Wolverine:CritterBids.Api")   // ⚠️ Meter name — includes service name suffix
            .AddMeter("Marten")                      // ⚠️ No suffix on Marten's Meter
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    })
    .UseOtlpExporter();   // Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT; set explicitly in production
```

Marten also exposes configuration on `StoreOptions.OpenTelemetry`:

```csharp
services.ConfigureMarten(opts =>
{
    opts.OpenTelemetry.TrackConnections = TrackLevel.Normal;   // connection spans
    opts.OpenTelemetry.TrackEventCounters();                   // emit marten.event.append counter
});
```

`TrackLevel.Verbose` adds write-operation tags to connection spans — useful for diagnosing a
specific slow handler but noisy enough to leave off by default.

---

## 4. Wolverine Metrics Reference

Verified against `src/Wolverine/MetricsConstants.cs` in the Wolverine repo. All of these are
emitted under the `"Wolverine:{ServiceName}"` Meter.

| Metric | Type | Description |
|---|---|---|
| `wolverine-messages-sent` | Counter | Messages sent out (local or via transport) |
| `wolverine-messages-received` | Counter | Messages received at the host |
| `wolverine-messages-succeeded` | Counter | Messages successfully processed |
| `wolverine-execution-failure` | Counter | Failed executions; tagged with `exception.type` |
| `wolverine-dead-letter-queue` | Counter | Messages moved to DLQ |
| `wolverine-execution-time` | Histogram | Handler execution time (ms) |
| `wolverine-effective-time` | Histogram | End-to-end time from send to handler completion (ms) |
| `wolverine-inbox-count` | Gauge | Persisted incoming envelopes awaiting processing |
| `wolverine-outbox-count` | Gauge | Persisted outgoing envelopes awaiting relay |
| `wolverine-scheduled-count` | Gauge | Persisted scheduled envelopes |

> **Note:** `wolverine-messages-received` is present in `MetricsConstants` but not listed in the
> ai-skills `opentelemetry-setup.md` reference at time of writing. Include it in dashboards — it
> and `wolverine-messages-sent` bracket the transport layer.

Durability gauges are on by default and refresh periodically:

```csharp
opts.Durability.DurabilityMetricsEnabled = true;      // default: true
opts.Durability.UpdateMetricsPeriod = 10.Seconds();   // default: 5s
```

Standard tag keys applied by Wolverine: `message.type`, `message.destination`, `tenant.id`,
`exception.type`, `source`.

### Trace-only events (no metric)

These are OTEL spans only, not counters. They fire on operational state transitions:

| Span | Trigger |
|---|---|
| `wolverine.envelope.discarded` | Message discarded by error policy |
| `wolverine.error.queued` | Message moved to dead letter |
| `wolverine.no.handler` | No handler found |
| `wolverine.envelope.requeued` | Requeued after failure |
| `wolverine.envelope.retried` | Inline retry |
| `wolverine.envelope.rescheduled` | Scheduled future retry |
| `wolverine.circuit.breaker.triggered` | Circuit breaker tripped |
| `wolverine.paused.listener` / `.starting.listener` / `.stopping.listener` | Listener lifecycle |
| `wolverine.sending.pausing` / `.sending.resumed` | Sending agent lifecycle |

If a dashboard needs counts of these, treat them as trace-derived metrics (Tempo → TraceQL, or
a span metrics processor in the OTEL collector). They do not appear in Prometheus directly.

---

## 5. Marten Metrics Reference

Emitted under the `"Marten"` Meter once `TrackEventCounters()` is called.

| Metric | Type | Description |
|---|---|---|
| `marten.event.append` | Counter | Events appended; tagged by `event_type` and `tenant.id` |
| `marten.{projection}.all.processed` | Counter | Events processed by a projection shard |
| `marten.{projection}.all.gap` | Histogram | Projection lag behind the event store |
| `marten.{projection}.all.skipped` | Counter | Events skipped by a projection |

Async daemon spans (emitted per shard):

| Span | Description |
|---|---|
| `marten.{name}.all.execution` | Processing a page of events |
| `marten.{name}.all.loading` | Loading a page |
| `marten.{name}.all.grouping` | Grouping step (multi-stream projections) |
| `marten.daemon.highwatermark` | High-water-mark calculation |

The `{projection}` placeholder resolves to the projection's configured name — so for
`ListingReadProjection` the gap metric would be `marten.listing_read.all.gap` (or similar — verify
against startup output). Dashboards using templated Grafana variables should use a label selector
rather than hard-coding the projection name.

---

## 6. Correlation Across Boundaries

Wolverine automatically propagates `Activity.Current.RootId` as the `CorrelationId` on every
outgoing envelope. Within a handler, cascading messages inherit the incoming envelope's correlation
ID, and ASP.NET requests that trigger Wolverine handlers tie into the same trace.

Envelope properties that matter for correlation:

| Property | Meaning |
|---|---|
| `Id` | Unique message ID |
| `CorrelationId` | Root activity ID — ties the full chain to its originating request |
| `ConversationId` | ID of the immediate parent message |
| `SagaId` | Saga context identifier |
| `TenantId` | Tenant context (CritterBids is single-tenant today, so this is unused) |

For CritterBids this means a single HTTP `PlaceBid` request should trace through the Auctions BC
bid-processing chain, any resulting Selling BC notifications, Settlement-BC commission entries, and
the SignalR broadcast — as one distributed trace. Ops-dashboard UX during the conference demo is
downstream of getting this wiring right.

---

## 7. Prometheus Export

```bash
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore
```

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Wolverine:CritterBids.Api")
            .AddMeter("Marten")
            .AddPrometheusExporter();
    });

var app = builder.Build();
app.MapPrometheusScrapingEndpoint();   // exposes /metrics on the API host
```

The scrape endpoint should be excluded from request-level telemetry (see §11). In production on
Hetzner, restrict `/metrics` to the Prometheus scraper's network path — it leaks operational
cardinality and should not be publicly reachable.

In Aspire local dev, Prometheus is not wired by default. Adding a Prometheus resource to
`CritterBids.AppHost` is a future extension — see §10.

---

## 8. Starter Grafana Queries

A minimal first dashboard for CritterBids on Hetzner. Not a full spec — just the queries that
answer the questions the conference demo is likely to raise ("is the system healthy? why is
this slow?"). Extend with per-BC breakdowns and alert thresholds when real traffic shape is known.

```promql
# Handler throughput (messages/sec)
rate(wolverine_messages_succeeded_total[5m])

# Handler failure rate
rate(wolverine_execution_failure_total[5m])

# DLQ rate — any sustained value above zero is investigatable
rate(wolverine_dead_letter_queue_total[5m])

# P95 handler execution time (ms)
histogram_quantile(0.95, rate(wolverine_execution_time_bucket[5m]))

# P95 end-to-end latency (send → handler completion)
histogram_quantile(0.95, rate(wolverine_effective_time_bucket[5m]))

# Durability backlogs
wolverine_inbox_count
wolverine_outbox_count

# Events appended per second (global)
rate(marten_event_append_total[5m])

# Per-event-type append rate — useful when BidPlaced dominates traffic
sum by (event_type) (rate(marten_event_append_total[5m]))

# Projection lag (replace {projection} with the actual shard name)
marten_listing_read_all_gap
```

Alerting rules are out of scope for this scaffold — see the ai-skills
`grafana-dashboard-templates.md` for a template set covering DLQ, execution time, inbox depth,
projection staleness, and failure rate.

---

## 9. Custom Audit Tags

`[Audit]` on a message property adds it as a tag on OTEL spans and to structured log entries for
that message. Use for **identifiers that make a span searchable from a user report** — bid IDs,
listing IDs, auction IDs — not for free-text fields or PII.

```csharp
// src/CritterBids.Auctions/PlaceBid.cs
public sealed record PlaceBid(
    [property: Audit] Guid AuctionId,
    [property: Audit] Guid ListingId,
    [property: Audit] Guid BidderParticipantId,
    decimal Amount);
```

Fluent equivalent, for messages that can't be annotated (e.g., integration contracts from
`CritterBids.Contracts`):

```csharp
opts.Policies.ForMessagesOfType<ContractListingPublished>()
    .Audit(x => x.ListingId, x => x.SellerId);
```

Wolverine 5.5+ automatically audits saga identity properties and Marten aggregate stream IDs —
so `AuctionCloseSaga.Id` and the stream ID on any `[WriteAggregate]` handler are tagged without
opt-in. This matters for the auction-close saga specifically: the saga ID already threads through
spans, so we don't need to re-tag it on every incoming command.

### CritterBids audit candidates (first pass)

| BC | Type | Fields to `[Audit]` |
|---|---|---|
| Auctions | `PlaceBid` | `AuctionId`, `ListingId`, `BidderParticipantId` |
| Auctions | `CloseAuction` | `AuctionId`, `ListingId` |
| Selling | `SubmitListing` | `ListingId`, `SellerId` |
| Selling | `ApproveListing` / `RejectListing` | `ListingId` |
| Settlement | any commission command | `AuctionId`, `SettlementId`, `ParticipantId` |
| Relay | outbound webhook commands | `SubscriberId`, correlation ID |

Not all of these exist yet. Add the attribute when the message type is authored, not in a sweep
later.

---

## 10. What This Scaffold Does Not Cover (Extension Points)

Intentional gaps. When one becomes relevant, the source material is already on disk and should
be the starting point rather than a clean-sheet draft.

| Topic | Source on disk | Relevant when |
|---|---|---|
| Full Grafana dashboard JSON | `C:\Code\JasperFx\ai-skills\wolverine\observability\grafana-dashboard-templates.md` | First real Grafana setup on the Hetzner VPS |
| Prometheus alerting rules | same file (`alerting rules` section) | After initial dashboard is live and on-call expectations exist |
| Custom tagging middleware | ai-skills `metrics-and-auditing.md` (`Custom metric tags via middleware`) | When an audit dimension crosses message types — e.g., tagging every Settlement-BC message with a fee-schedule version |
| Audit trail writes to Marten | ai-skills `metrics-and-auditing.md` (`Writing audit events to Marten`) | When regulatory or support needs a persistent message ledger beyond what the event store already provides |
| `InvokeTracingMode.Full` | ai-skills `opentelemetry-setup.md` (`Enable full tracing for InvokeAsync`) | When a hot-path is using `bus.InvokeAsync` and the default lightweight tracking isn't enough |
| `TrackLevel.Verbose` for Marten connections | ai-skills `opentelemetry-setup.md` (`Marten trace configuration`) | Diagnosing a specific slow handler's SQL profile |
| Node-assignment health-check noise tuning | ai-skills `opentelemetry-setup.md` (`Disable node assignment health check traces`) | When the trace volume from periodic health checks drowns out useful spans |
| Aspire `AddPrometheus` resource wiring | not yet in ai-skills | If local dev starts needing PromQL parity with prod |
| CritterWatch integration | not yet shipped by JasperFx | When CritterWatch is released and CritterBids becomes a testbed |
| Command-line diagnostics (`codegen-preview`, `describe-routing`, `describe-resiliency`) | partially covered in `wolverine-message-handlers.md` + `critter-stack-testing-patterns.md`; full reference at `C:\Code\JasperFx\ai-skills\wolverine\observability\command-line-diagnostics.md` | When a dedicated diagnostics skill is warranted |

When extending, prefer **pulling the relevant section in and CritterBids-izing it** (BC names,
actual message types, the Hetzner deploy posture) over copy-pasting the ai-skills reference
verbatim. The gap analysis at `docs/skills/jasper-fx-ai-skills-gap-analysis.md` §3.2 should be
updated to match whatever is absorbed.

---

## 11. Noise Reduction (CritterBids Posture)

Endpoints and message types that produce high-volume, low-information telemetry should have
telemetry disabled at the source. Cardinality is cheaper to not emit than to filter later.

```csharp
// Health check endpoints
[WolverineLogging(telemetryEnabled: false)]
[WolverineGet("/health")]
public static string Health() => "OK";

// Kubernetes/Docker readiness
[WolverineLogging(telemetryEnabled: false)]
[WolverineGet("/ready")]
public static string Ready() => "OK";
```

Per-endpoint suppression for listeners (in the host Wolverine config):

```csharp
opts.ListenToRabbitQueue("heartbeat").TelemetryEnabled(false);
```

Node-assignment health-check tracing is on by default and can dominate the trace volume during
quiet periods:

```csharp
opts.Durability.NodeAssignmentHealthCheckTracingEnabled = false;
// Or sample periodically instead of disabling fully
opts.Durability.NodeAssignmentHealthCheckTraceSamplingPeriod = TimeSpan.FromMinutes(10);
```

SignalR keep-alive ping messages (if they flow through Wolverine in the future) should also be
suppressed — not currently an issue because CritterBids' SignalR integration uses Wolverine only
for the broadcast side, not for client pings.

---

## 12. Open Questions

*Placeholder — none tracked yet.* When a specific OTEL or Prometheus behavior in the Critter
Stack is ambiguous or the ai-skills material contradicts the source, log it in
`docs/jasperfx-open-questions.md` and link it here, following the pattern from the DCB open
question (gap analysis §5).

---

## Related Skills

- `aspire.md` — local dev trace surfacing via the Aspire dashboard; environment injection of `OTEL_EXPORTER_OTLP_ENDPOINT`
- `wolverine-message-handlers.md` — middleware lifecycle (`Before`/`After`/`Finally`) that custom audit middleware hooks into; `[WolverineLogging]` for per-type telemetry control
- `marten-event-sourcing.md` — async daemon configuration; `EventAppendMode.Quick`; the event append counter
- `integration-messaging.md` — per-queue telemetry suppression; RabbitMQ transport configuration
- `wolverine-sagas.md` — saga identity automatic tagging (Wolverine 5.5+); auction-close saga as the primary candidate for custom audit tags
- `critter-stack-testing-patterns.md` — CLI diagnostics commands (`codegen-preview`, `describe-routing`) that overlap with the observability story

---

## External References

- Wolverine: [Logging and OpenTelemetry](https://wolverine.netlify.app/guide/logging.html)
- Marten: [OpenTelemetry](https://martendb.io/otel.html)
- ai-skills source: `C:\Code\JasperFx\ai-skills\wolverine\observability\` (five files; three absorbed into this scaffold)
- Gap analysis row: `docs/skills/jasper-fx-ai-skills-gap-analysis.md` §3.2 — shift from 🔴 to 🟡 on the next wave review
