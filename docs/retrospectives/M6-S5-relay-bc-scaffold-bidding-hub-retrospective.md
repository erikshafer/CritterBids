# M6-S5: Relay BC Scaffold + `BiddingHub` Core Routes - Retrospective

**Date:** 2026-05-29
**Milestone:** M6 - Obligations BC + Relay BC
**Slice:** S5 - Relay BC scaffold + first reactive surface
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M6-S5-relay-bc-scaffold-bidding-hub.md`

## Baseline

- Solution had 7 test projects green pre-slice; no `CritterBids.Relay` project existed. No SignalR / WebSocket code anywhere in the repo — S5 is the first reactive surface.
- `Directory.Packages.props` carried no `WolverineFx.SignalR` or `Microsoft.AspNetCore.SignalR.Client` pins.
- `dotnet build` emitted 14 pre-existing Marten 8.35.0 `NU1904` critical-vuln warnings (baseline, not introduced by this slice).
- Full-suite test count before S5: Contracts 1, Api 1, Participants 6, Listings 20, Selling 36, Settlement 25, Obligations 13, Auctions 65 = 167.
- ADR ledger next unreserved number: `023`.

## Items completed

| Item | Description |
|------|-------------|
| S5a | `CritterBids.Relay` class library — csproj (`WolverineFx.SignalR` + Contracts ref), `AssemblyInfo.cs` (`InternalsVisibleTo`), slnx node |
| S5b | `BiddingHub` (plain `Hub`, `/hub/bidding`) — group enrolment via `OnConnectedAsync` query-string + race-free `JoinListingGroup`/`JoinBidderGroup` |
| S5c | `OperationsHub` (plain `Hub`, `/hub/operations`) — registered + `MapHub`'d, no S5 push handlers |
| S5d | Three notification handlers — `BidPlacedHandler`, `ListingSoldHandler`, `SettlementCompletedHandler` — each `static Task Handle(...)`, pure consumer |
| S5e | `AddRelayModule()` — `AddSignalR()` only; no `AddMarten()`; `NotificationHistoryView` explicitly noted as S6, not stubbed |
| S5f | `Program.cs` wiring — `using`, `IncludeAssembly`, unconditional `AddRelayModule()` + 2× `MapHub().DisableAntiforgery()`, in-guard publish routes + 2× `ListenToRabbitQueue` |
| S5g | `RelayBcDiscoveryExclusion` added to 7 sibling fixtures |
| S5h | `CritterBids.Relay.Tests` — boots-clean fixture + minimal-Kestrel hub fixture; 3 boots-clean tests + 3 SignalR push tests |
| S5i | ADR 023 (load-bearing SignalR architecture decision) + ledger row |
| S5j | Narrative 001 Document History v0.2 row |

## S5b/S5d: Plain `Hub` + direct `IHubContext` push (ADR 023, path b)

**Why this approach — and why path (a) was rejected.** The `wolverine-signalr.md` skill and `WolverineFx.SignalR` document a transport pattern (path a): `opts.UseSignalR()` + `MessagesImplementing<IBiddingHubMessage>().ToSignalR()`, handler *returns* the notification, transport broadcasts it CloudEvents-wrapped. While wiring the first hub we confirmed the finding already recorded in `docs/vision/live-queries-and-streaming.md`: the transport resolves `IHubContext<WolverineHub>` (its group helpers are generic `where THub : WolverineHub`), so it pushes to the framework's own `WolverineHub` — **not** to an application hub mapped with `app.MapHub<BiddingHub>("/hub/bidding")`. A client connected to `/hub/bidding` is never in the context the transport pushes to; path (a) does not connect end-to-end. This is load-bearing across every future Relay hub, so per the prompt's stop-and-flag instruction it was escalated to **ADR 023** rather than pivoted silently.

**Handler shape after (representative — `BidPlacedHandler`):**

```csharp
public static class BidPlacedHandler
{
    public static Task Handle(
        BidPlaced message,
        IHubContext<BiddingHub> hub,
        CancellationToken cancellationToken)
    {
        var notification = new BidPlacedNotification(
            message.ListingId, message.BidId, message.BidderId,
            message.Amount, message.BidCount, message.PlacedAt);

        return hub.Clients
            .Group($"listing:{message.ListingId}")
            .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken);
    }
}
```

| Metric | Value |
|--------|-------|
| Handler return type | `Task` (never `OutgoingMessages`) |
| `IMessageBus` injections across Relay handlers | 0 |
| `OutgoingMessages` returns across Relay handlers | 0 |
| `session.Store()` / `IDocumentSession` usage in Relay | 0 |
| Group targeting | explicit per-handler `Clients.Group(key)` |
| `ReceiveMessage` payload | raw notification record (no CloudEvents envelope) |

**Group targets** (per M6-S1 naming convention): `BidPlaced` → `listing:{ListingId}`; `ListingSold` → `listing:{ListingId}`; `SettlementCompleted` → `bidder:{WinnerId}`. The winner-confirmation push targets the bidder group because it is a private "you won and were charged" message, not a feed broadcast.

## S5h: SignalR integration testing required two fixtures + a discovery fix

**Why two fixtures.** Alba / `TestServer` cannot host SignalR (no real socket). The boots-clean assertions use an Alba `RelayTestFixture` (Testcontainers Postgres + `RunWolverineInSoloMode` + `DisableAllExternalWolverineTransports`); the push assertions use a separate `RelayHubTestFixture` that stands up real Kestrel on an ephemeral port (`builder.WebHost.UseUrls("http://127.0.0.1:0")`, port read back from `_app.Urls.Single()`) so a real `HubConnection` can connect.

**Discovery / resolution — transitive handler leak.** First push-test run: 4 passed, 2 timed out (`BidPlaced`/`ListingSold`). Verbose logs showed the root cause:

```
Wolverine.Persistence.Sagas.UnknownSagaException: ... saga of type ... could not be found
```

Because `CritterBids.Relay.Tests` references `CritterBids.Api` (which references every BC), Wolverine's conventional discovery scanned **all** BC assemblies in the minimal Kestrel host. Auctions' own `BidPlacedHandler` (a saga handler) co-consumed `BidPlaced` and faulted before Relay's push fired. `SettlementCompleted` had no competing consumer in the host, so its test passed — which is why only 2 of 3 failed.

`opts.DisableConventionalDiscovery()` does **not** exist on `WolverineOptions` in this Wolverine version (compile error). Resolution: register the six `…BcDiscoveryExclusion` extensions inside the hub fixture's `UseWolverine` block — the same `IWolverineExtension` exclusion pattern the sibling suites already use:

```csharp
internal sealed class AuctionsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x =>
            x.Excludes.WithCondition("Auctions BC handlers are out of scope for Relay tests",
                t => t.Namespace?.StartsWith("CritterBids.Auctions") == true));
}
```

After excluding Selling / Auctions / Listings / Settlement / Obligations / Participants, all 6 Relay tests passed. The push tests await an explicit `JoinListingGroup`/`JoinBidderGroup` client invocation before publishing (race-free enrolment), then `await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10))` — a failsafe timeout only, not a real-clock wait; the TCS completes the instant the push lands.

## Test results

| Phase | Relay Tests | Result |
|-------|-------------|--------|
| After handlers + first push run | 4 / 6 | 2 timeouts (transitive Auctions saga handler) |
| After discovery-exclusion fix in hub fixture | 6 / 6 | green |
| Full `dotnet test CritterBids.slnx` | 173 / 173 | green, no regressions |

Full-suite breakdown at close: Contracts 1, Api 1, **Relay 6**, Participants 6, Listings 20, Selling 36, Settlement 25, Obligations 13, Auctions 65 = **173** (+6 from baseline; the 7 sibling suites unchanged after their `RelayBcDiscoveryExclusion` additions).

## Build state at session close

- `dotnet build`: 0 errors.
- Warnings: 14 Marten `NU1904` — **unchanged** from baseline; no new warnings introduced.
- Relay BC negative-space assertions: `IMessageBus` usages = 0; `OutgoingMessages` returns = 0; `AddMarten` calls in `RelayModule` = 0; `ConfigureMarten` calls = 0; non-`Task`/`void` handler signatures = 0.
- Auctions / Settlement BC source files modified = 0 (publish routing added in `Program.cs` only).
- `MapHub` registrations added = 2 (`BiddingHub`, `OperationsHub`); `ListenToRabbitQueue` added = 2 (`relay-auctions-events`, `relay-settlement-events`).

## Key learnings

1. **The Wolverine SignalR transport targets `WolverineHub`, not mapped application hubs.** Any BC that maps its own `Hub` and wants Wolverine handlers to push to it must inject `IHubContext<THub>` directly (ADR 023). The `ToSignalR()` routing sugar is only end-to-end if you also use `WolverineHub` + `opts.UseSignalR<THub>()`.
2. **A test project that references the Api host inherits every BC's handler discovery.** In an in-process Wolverine host this means foreign saga handlers will co-consume shared integration events and can fault the message before your handler runs. The `{TargetBc}BcDiscoveryExclusion` `IWolverineExtension` pattern is mandatory in any minimal-host fixture that references `CritterBids.Api`, not just the Alba boots-clean fixture.
3. **`opts.DisableConventionalDiscovery()` is not available** on `WolverineOptions` in this version — exclusion extensions are the supported lever.
4. **SignalR pushes are deterministically testable without real-clock waits**: real Kestrel on port 0 + `Microsoft.AspNetCore.SignalR.Client` + an awaited explicit group-join invocation + a `TaskCompletionSource` the `connection.On<T>` handler completes. The only timeout is a failsafe.
5. **One green test in a partial failure is a signal, not noise.** `SettlementCompleted` passing while `BidPlaced`/`ListingSold` timed out pinpointed the cause: only the latter two had a competing in-host consumer.

## Findings against narrative

S5 is anchored to narrative 001 (Moments 5–6, `BiddingHub` pushes). No drift surfaced: the narrated `BidPlaced`/`ListingSold`/`SettlementCompleted` pushes match the lived handlers' shape and group targeting. One observation routed `document-as-intentional`: the narrative's deferred-section trigger text reads "trigger: M4 Tier 4 ship", but the Relay BC was re-scoped to M6 (Finding 006). This is a stale trigger label, not a domain disagreement; recorded in the v0.2 Document History row rather than filed as a new finding. No `code-update` or `workshop-update` findings.

## Spec delta - landed?

**Landed as written.** The prompt's `## Spec delta` declared exactly one spec consequence: narrative 001 gains a Document History row recording that the `BiddingHub` `BidPlaced`/`ListingSold`/`SettlementCompleted` pushes move from `defer` to lived (partial). That row landed as **v0.2** (2026-05-29) in `docs/narratives/001-bidder-wins-flash-auction.md` § Document History, naming the three lived handlers, their group targets, the ADR-023 path, and the residue still deferred to S6/S7 (Outbid targeted push, `ReserveMet`/`ExtendedBiddingTriggered`, the open-broadcast remainder, the per-listing high-bidder projection / `NotificationHistoryView`, the remaining inbound consumers, and the full `OperationsHub` feed). No workshop amendment was required (Relay has no workshop yet). No OpenSpec change was authored (see open-question disposition below), so the delta is narrative-anchored only, as the prompt specified.

**Owed skill note — `wolverine-signalr.md` first lived example.** Per AUTHORING.md rule 4 the skill edit is owed to the retro, not made in-session. The skill should gain its first lived CritterBids example citing Relay's M6-S5 handlers, and its "Lessons Learned" caution against injecting `IHubContext` directly should be **superseded for CritterBids Relay** by ADR 023: because the Wolverine transport pushes to `WolverineHub` rather than mapped hubs, direct `IHubContext<THub>` injection is the *endorsed* Relay pattern, not an anti-pattern. This is a documentation follow-up for whoever next edits the skill file.

## Open-question resolutions

1. **`WolverineHub` vs plain `Hub`** → **plain `Hub`** (ADR 023). The transport-coupled `WolverineHub` path does not reach mapped hubs; plain `Hub` + `IHubContext` is the working path and keeps handlers pure consumers. Load-bearing → escalated to ADR 023 as the prompt required.
2. **Automatic group targeting** → **no; explicit per-handler** `Clients.Group(key)`. Marker interfaces (`IBiddingHubMessage`/`IOperationsHubMessage`) are retained as documentation / future-door affordances but carry no routing behaviour under path (b).
3. **Which queue carries `SettlementCompleted` to Relay** → **`relay-settlement-events`, via an added publish route** in `Program.cs`. The §2 routing table only listed `SellerPayoutIssued` on that queue; confirmed against Settlement's publish topology that `SettlementCompleted` needed an explicit added route (no Settlement BC code change). Accepted consequence recorded below.
4. **Relay OpenSpec opt-in (ADR 021)** → **not adopting for S5** (the prompt's default). Relay is a pure-consumer BC; the milestone doc does not require it; adoption is a PO decision to surface, not make unilaterally. No `openspec/` change folder created. Surfaced here for a PO call at Relay's continued M6 work.
5. **SignalR integration-test transport feasibility** → the skill's `WolverineFx.SignalR` *client transport* (skill §9) is a path-(a) affordance and is **N/A** under path (b). Chosen assertion shape: real Kestrel + `Microsoft.AspNetCore.SignalR.Client` + TCS, deterministic with a failsafe timeout only. No real-clock waits introduced.

## Accepted consequence — `relay-settlement-events` queue sharing

`relay-settlement-events` now carries both `SettlementCompleted` (consumed by Relay in S5) and `SellerPayoutIssued` (publish-only, no Relay handler until S6). In live RabbitMQ, Relay will receive `SellerPayoutIssued` with no handler — Wolverine logs / dead-letters it without crashing. Harmless in tests (external transports disabled). The `SellerPayoutIssued` handler is deliberately deferred to S6 with the rest of the inbound-consumer set.

## Verification checklist

- [x] `CritterBids.Relay.csproj` exists; `WolverineFx.SignalR` referenced; `<ProjectReference>` to `CritterBids.Contracts`; `AssemblyInfo.cs` exposes internals; both project nodes in `CritterBids.slnx`.
- [x] `BiddingHub` and `OperationsHub` exist and are `MapHub`-registered in `Program.cs`.
- [x] `BidPlacedHandler`, `ListingSoldHandler`, `SettlementCompletedHandler` each consume their contract event, return `Task`, and push to the correct `BiddingHub` group.
- [x] `AddRelayModule()` registers SignalR + hubs + Relay handler discovery; no `AddMarten()`; no Relay Marten document registered (projection deferred to S6, noted in module).
- [x] `Program.cs` has `using`, `IncludeAssembly`, `AddRelayModule()`, both `MapHub` calls, both `ListenToRabbitQueue()` calls, and the `BidPlaced`/`ListingSold`/`SettlementCompleted` publish-route additions; `CritterBids.Api.csproj` references the project.
- [x] No Auctions or Settlement BC source files modified (Program.cs routing only).
- [x] Sibling fixtures (Auctions / Listings / Settlement / Obligations + Selling / Participants) register `RelayBcDiscoveryExclusion`; Relay.Tests applies `DisableAllExternalWolverineTransports()`.
- [x] Relay.Tests has a boots-clean test and one passing integration test per handler asserting the SignalR push reaches the expected group via a real client — all green, no real-clock waits.
- [x] `dotnet build` passes (0 errors); full `dotnet test CritterBids.slnx` green (173) with no regressions.
- [x] Narrative 001 gained a Document History row (v0.2) for the partial Moment 5–6 landing.
- [x] This retrospective written with `## Spec delta — landed?`, the owed `wolverine-signalr.md` note, all open-question resolutions, and the Relay-OpenSpec disposition.
- [x] No commit to `main`; no `Co-Authored-By` trailer.

## What remains / next session should verify

**In scope for M6, deferred to S6:**
- Remaining inbound consumers: `relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, `relay-listings-events` + handlers.
- Full `OperationsHub` push handler set (staff feed duplicates, dispute/escalation alerts, session/lot-board pushes); `SellerPayoutIssued` handler.
- `NotificationHistoryView` Marten projection + Relay-owned document / `ConfigureMarten` registration. **Note:** S6 will be the first slice to call `AddMarten`-side registration for Relay — the boots-clean fixture already provisions Postgres so the projection has a home.

**In scope for M6, deferred to S7:**
- End-to-end journey test (`SettlementCompleted` → Obligations start → Relay push) and the full `Program.cs` route-topology audit + test-count baseline update.

**Out of scope, tracked elsewhere:**
- React SPA / `@microsoft/signalr` client, Relay HTTP endpoints, email/SMS/push delivery seams — post-S5 / M8 per milestone non-goals.
- `wolverine-signalr.md` skill edit (owed; documentation follow-up per the Spec-delta section).
- Relay OpenSpec adoption — open PO decision (defaulted to not-adopting for S5).
