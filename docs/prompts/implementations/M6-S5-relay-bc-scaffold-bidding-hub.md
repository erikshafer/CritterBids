# M6-S5: Relay BC Scaffold + `BiddingHub` Core Routes

**Milestone:** M6 ([Obligations BC + Relay BC](../../milestones/M6-obligations-relay-bc.md))
**Slice:** S5 of 7 (Relay BC scaffold + first reactive surface)
**Narrative:** [`docs/narratives/001-bidder-wins-flash-auction.md`](../../narratives/001-bidder-wins-flash-auction.md) (Moments 5–6 are the Relay `BiddingHub` pushes, currently carried under the `defer` disposition; S5 lands lived code for the `BidPlaced` / `ListingSold` / `SettlementCompleted` subset of those pushes)
**Agent:** @PSA
**Estimated scope:** one PR; new `CritterBids.Relay` + `CritterBids.Relay.Tests` projects, three notification handlers, two hubs, plus `Program.cs` / `CritterBids.slnx` / sibling-fixture edits

---

## Goal

Stand up the `CritterBids.Relay` and `CritterBids.Relay.Tests` projects, register them in the solution / Api host / Wolverine configuration, and land **Relay's first reactive surface**: the `BiddingHub` and `OperationsHub` SignalR hubs plus the three participant-facing notification handlers (`BidPlaced`, `ListingSold`, `SettlementCompleted`) that consume the `relay-auctions-events` and `relay-settlement-events` queues and push to the correct hub group. This is CritterBids' first lived SignalR transport — the slice that converts narrative 001's Moments 5–6 from forward-spec (`defer`) into running, integration-tested code for the BidPlaced/ListingSold/SettlementCompleted subset.

Relay is a **pure consumer**: its handlers return `void` / `Task`, never `OutgoingMessages`, never call `IMessageBus`, and never publish integration events. Its only output is a SignalR push. The remaining inbound consumers (`relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, `relay-listings-events`), the full `OperationsHub` push handler set, and the `NotificationHistoryView` Marten projection are deliberately held for S6; the end-to-end journey test and the full route-topology audit are held for S7.

If a load-bearing design decision surfaces mid-session — in particular either of the two open SignalR questions named below — stop and flag rather than pivoting in-session.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M6-obligations-relay-bc.md` | Milestone scope — §2 "Relay BC — core components", §2 "Relay integrations — events consumed per hub", §5 RabbitMQ routing summary, §6 "Relay never publishes integration events" + "BC discovery isolation in test fixtures", §7 slice table (S5 row, and S6/S7 rows for what is deferred) |
| `docs/narratives/001-bidder-wins-flash-auction.md` | Joint-authoritative narrative; Moments 3, 5, 6 carry the `BiddingHub` open-broadcast and bid-feed pushes S5 begins implementing (currently `defer`) |
| `docs/skills/wolverine-signalr.md` | Hub design, server configuration, marker interfaces, group management, the SignalR client transport for integration testing — **and the source-review note (§ top) pointing at the two open design questions** |
| `docs/vision/live-queries-and-streaming.md` | "Open Design Questions for SignalR Implementation" — automatic group targeting and `WolverineHub` vs plain `Hub`; read before wiring the first hub |
| `docs/skills/adding-bc-module.md` | `AddXyzModule()` extension shape; `services.ConfigureMarten()` ownership; no `AddMarten()` inside the module |
| `docs/skills/critter-stack-testing-patterns.md` | §Cross-BC Handler Isolation — the `{TargetBc}BcDiscoveryExclusion` fixture pattern; `DisableAllExternalWolverineTransports()`; `tracked.Sent` vs `tracked.NoRoutes` learning |
| `src/CritterBids.Settlement/` (`SettlementModule.cs`) and the M6-S2 Obligations scaffold (`src/CritterBids.Obligations/ObligationsModule.cs`, `docs/prompts/implementations/M6-S2-obligations-scaffold.md`) | Direct structural template for a new BC scaffold, module-extension shape, solution/host wiring, and the sibling-fixture exclusion work |

## In scope

1. **`src/CritterBids.Relay` class library** — `WolverineFx.SignalR` package reference plus the package set matching sibling Marten BCs; `<ProjectReference>` to `CritterBids.Contracts` (handlers consume `CritterBids.Contracts.Auctions.*` and `CritterBids.Contracts.Settlement.*`); `AssemblyInfo.cs` with `InternalsVisibleTo("CritterBids.Relay.Tests")`. Added to `CritterBids.slnx` under `/src/`, alphabetical (after `CritterBids.Participants`).
2. **`BiddingHub`** — participant-facing hub at its M6-S1-decided route; group enrolment per the M6-S1 group-naming convention (participant/listing groups keyed as decided in S1). Hub class + server registration only; no React client.
3. **`OperationsHub`** — staff-facing hub at its route; **set up and `MapHub`-registered** in S5 so host wiring is done once, but its per-event push handlers are S6. Registering the hub with no S5 handlers is intentional.
4. **Three notification handlers** pushing to `BiddingHub` participant groups: `BidPlacedHandler` (`CritterBids.Contracts.Auctions.BidPlaced`), `ListingSoldHandler` (`ListingSold`), and `SettlementCompletedHandler` (the settlement-confirmation push). Each returns `void` / `Task` and targets the correct group per the skill's group-management section.
5. **`AddRelayModule()`** — registers the SignalR services / hubs, contributes Relay's Wolverine handler discovery, and follows the `adding-bc-module` shape. No `AddMarten()` call; **no Relay Marten document is registered in S5** (the `NotificationHistoryView` projection is S6 — note this explicitly in the module rather than stubbing an empty projection).
6. **`Program.cs` wiring** — `using CritterBids.Relay;`, `Discovery.IncludeAssembly`, `AddRelayModule()`, `app.MapHub<BiddingHub>()` and `app.MapHub<OperationsHub>()`, the inbound `relay-auctions-events` and `relay-settlement-events` `ListenToRabbitQueue()` calls, and the publish-side route additions needed to land `BidPlaced` / `ListingSold` / `SettlementCompleted` on those queues (routing-only additions to existing message types — **no Auctions or Settlement BC code changes**). `<ProjectReference>` from `CritterBids.Api.csproj`.
7. **Cross-BC discovery exclusions** — add a `RelayBcDiscoveryExclusion` to the sibling test fixtures whose BCs also handle these shared events (at minimum Auctions, Listings, Settlement, Obligations; plus any inline exclusion the sibling suites use) so Relay's handlers do not leak into their in-process hosts.
8. **`CritterBids.Relay.Tests`** — fixture that excludes the foreign BCs and applies `DisableAllExternalWolverineTransports()`; a boots-clean test; and one integration test per handler asserting the SignalR push lands on the expected group, driven via the skill's SignalR client transport (no real-clock waits; deterministic in-process driving).

## Explicitly out of scope

- **The remaining inbound consumers** — `relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, `relay-listings-events` and their handlers; **S6**.
- **The full `OperationsHub` push handler set** (staff-facing duplicates of the auctions feed, dispute/escalation alerts, session/lot-board pushes); **S6**. S5 registers the hub but wires no `OperationsHub` push handlers.
- **`NotificationHistoryView` Marten projection** and any Relay-owned document / `ConfigureMarten` document registration; **S6**.
- **End-to-end journey test** (`SettlementCompleted` → Obligations start → Relay push) and the full `Program.cs` route-topology audit / test-count baseline update; **S7**.
- **`relay-settlement-events` `ListenTo` final confirmation as a topology audit item** — S5 adds the `ListenTo` it needs to consume `SettlementCompleted`; the milestone's S7 audit confirms the complete settlement-route wiring.
- **Email / SMS / push delivery seams, Relay HTTP endpoints, React SPA / `@microsoft/signalr` client** — all post-S5 / M8 per the milestone non-goals. Relay is handler-only.
- **Auctions / Settlement BC code changes** — Relay needs only publish-route additions in `Program.cs`; do not modify Auctions or Settlement source.
- **Editing OpenSpec-managed files** under `.github/prompts/` or `.github/skills/`, and **skill-file edits** (record any owed skill update — e.g. the first-lived-example note for `wolverine-signalr.md` — in the retro per AUTHORING.md rule 4).

## Conventions to pin or follow

- **Relay never publishes integration events** (milestone §6): every Relay handler returns `void` / `Task`. No `OutgoingMessages`, no `IMessageBus`, no event publication. A non-`void`/`Task` Relay handler signature is a bug.
- **Hub + group management** per `docs/skills/wolverine-signalr.md`; group-naming per the M6-S1 decision (`BiddingHub` participant/listing groups; `OperationsHub` broadcast).
- **Module shape** per `docs/skills/adding-bc-module.md`: `AddRelayModule()` extension; no `AddMarten()` inside the module.
- **Test isolation** per `critter-stack-testing-patterns.md`: `RelayBcDiscoveryExclusion` on sibling fixtures; Relay.Tests applies `DisableAllExternalWolverineTransports()`; assert pushes via `tracked.Sent`-equivalent SignalR client observation, not `NoRoutes`.
- `sealed record` for any new types; no "Event" suffix on internal event names; no "paddle"; `[AllowAnonymous]` posture holds through M6 (Relay registers no HTTP endpoints this slice).

## Spec delta

Per ADR 020. S5's spec consequence is anchored to **narrative 001**: Moments 5–6 (and the `BiddingHub` open-broadcast in Moment 3) currently sit under the `defer` disposition because Relay had no lived code. S5 lands lived, integration-tested code for the `BidPlaced` / `ListingSold` / `SettlementCompleted` subset of those pushes, so narrative 001 gains a **Document History row** recording that the `BiddingHub` BidPlaced/ListingSold/SettlementCompleted pushes move from `defer` to lived (partial — the Outbid push, the open-broadcast remainder, and the full `OperationsHub` feed remain `defer` until S6). The `wolverine-signalr.md` skill gains its first lived example; that skill update is **owed to the retro**, not made in-session. Whether Relay opts into the OpenSpec workspace at its opening session (per ADR 021) is an open question below; absent a PO decision to adopt, S5 carries **no OpenSpec change** and the spec delta is narrative-anchored only. The retro's `## Spec delta — landed?` paragraph confirms the three pushes are lived and covered, and records the narrative-row and skill-note dispositions.

## Acceptance criteria

- [ ] `src/CritterBids.Relay/CritterBids.Relay.csproj` exists; `WolverineFx.SignalR` referenced; `<ProjectReference>` to `CritterBids.Contracts` present; `AssemblyInfo.cs` exposes internals to the test project; both new project nodes are in `CritterBids.slnx`.
- [ ] `BiddingHub` and `OperationsHub` exist and are `MapHub`-registered in `Program.cs`.
- [ ] `BidPlacedHandler`, `ListingSoldHandler`, and `SettlementCompletedHandler` each consume their contract event, return `void` / `Task`, and push to the correct `BiddingHub` group.
- [ ] `AddRelayModule()` registers SignalR + hubs + Relay handler discovery per the `adding-bc-module` shape; no `AddMarten()` call inside the module; no Relay Marten document registered (projection deferred to S6).
- [ ] `Program.cs` has the `using`, `IncludeAssembly`, `AddRelayModule()`, both `MapHub` calls, the `relay-auctions-events` and `relay-settlement-events` `ListenToRabbitQueue()` calls, and the publish-route additions for `BidPlaced` / `ListingSold` / `SettlementCompleted`; `CritterBids.Api.csproj` references the project.
- [ ] No Auctions or Settlement BC source files are modified (Program.cs routing only).
- [ ] Each sibling fixture that needs it (Auctions / Listings / Settlement / Obligations, plus any inline exclusion) registers a `RelayBcDiscoveryExclusion`; Relay.Tests applies `DisableAllExternalWolverineTransports()`.
- [ ] `CritterBids.Relay.Tests` contains a boots-clean test and one passing integration test per handler asserting the SignalR push reaches the expected group via the skill's client transport — all green, no real-clock waits.
- [ ] `dotnet build` passes (0 errors); full `dotnet test CritterBids.slnx` green with no regressions across any BC (Docker running for Testcontainers PostgreSQL).
- [ ] `docs/narratives/001-bidder-wins-flash-auction.md` gains a Document History row for the partial Moment 5–6 lived-code landing (BidPlaced/ListingSold/SettlementCompleted subset).
- [ ] `docs/retrospectives/M6-S5-relay-bc-scaffold-bidding-hub-retrospective.md` written, including `## Spec delta — landed?`, the owed `wolverine-signalr.md` first-lived-example note, the resolution of each open question below, and the Relay-OpenSpec-adoption decision.
- [ ] No commit to `main`; no `Co-Authored-By` trailer.

## Open questions

- **The two open SignalR design questions** (per the `wolverine-signalr.md` source-review note and `docs/vision/live-queries-and-streaming.md`): automatic group targeting, and `WolverineHub` vs plain `Hub`. S5 is the first reactive surface, so a choice must be made to wire the first hub. If the choice is load-bearing across future hubs, **stop and flag** — it may warrant an ADR (next unreserved: `023-<slug>.md`). Record the decision and its rationale in the retro regardless.
- **Which queue carries `SettlementCompleted` to Relay.** The §2 routing table lists `relay-settlement-events` as carrying `SellerPayoutIssued`, while the §1 exit criteria and the S5 slice row name `SettlementCompleted` as a `BiddingHub` push. Confirm against Settlement's existing publish topology whether `SettlementCompleted` rides `relay-settlement-events` or needs an added publish route; add publish-route wiring in `Program.cs` only — no Settlement BC code change. Flag the resolution in the retro.
- **Relay OpenSpec opt-in (ADR 021).** Relay's opening session may opt the BC into the OpenSpec workspace. Default for S5 is **not** to adopt (the milestone doc does not require it and Relay is a pure-consumer BC); if the PO wants Relay under OpenSpec, that is a decision to surface, not to make unilaterally. Record the disposition in the retro.
- **SignalR integration-test transport in the Testcontainers fixture.** Confirm the `WolverineFx.SignalR` client transport (skill §9) drives deterministically inside the Relay.Tests host; if it cannot observe pushes without a real connection, flag the limitation and the chosen assertion shape in the retro rather than introducing real-clock waits.
