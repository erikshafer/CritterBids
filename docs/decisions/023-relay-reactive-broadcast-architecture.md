# ADR 023: Relay Reactive-Broadcast Architecture — Plain Hub + Direct `IHubContext`

**Status:** Accepted
**Date:** 2026-05-29

---

## Context

The Relay BC lands its first reactive surface in M6-S5: Wolverine handlers consume integration
events (`BidPlaced`, `ListingSold`, `SettlementCompleted`) and push them to SignalR clients
watching a live auction. This is the first WebSocket/SignalR code in CritterBids, so the wiring
pattern every future Relay hub and handler will copy is being set now — it needs deciding before
the hub handlers are written.

`docs/skills/wolverine-signalr.md` and the `WolverineFx.SignalR` package document a transport-based
pattern (**path (a)**): register `opts.UseSignalR()`, mark notification records with a marker
interface, route them with `MessagesImplementing<IBiddingHubMessage>().ToSignalR()`, and let
Wolverine deliver each cascaded/published message to connected clients. Under this pattern a handler
*returns* the notification and Wolverine's transport broadcasts it, wrapped in a CloudEvents
envelope, on the `ReceiveMessage` client method.

**The blocker.** While wiring the first hub we confirmed the finding already recorded in
`docs/vision/live-queries-and-streaming.md` (§"SignalR transport vs. mapped hub"): the
`WolverineFx.SignalR` transport resolves `IHubContext<WolverineHub>` — it sends to the framework's
own `WolverineHub`, not to an application hub mapped with `app.MapHub<BiddingHub>(...)`. The
transport's group-targeting helpers are generic over `where THub : WolverineHub`. A connection made
to `/hub/bidding` (a plain `Hub`) is therefore never in the same hub context the transport pushes
to, so path (a) does **not** connect end-to-end to the hubs CritterBids maps. The vision doc proves
this and (step 5) mandates an ADR for the chosen alternative.

Relay also has a hard architectural constraint from the milestone and the S5 prompt: **Relay never
publishes.** Every Relay handler returns `void`/`Task`; its only output is a SignalR push. It emits
no integration events, uses no `OutgoingMessages`, and touches no `IMessageBus`.

## Decision

**Adopt path (b): plain `Hub` subclasses driven by Wolverine handlers that inject
`IHubContext<THub>` and push explicitly.**

- `BiddingHub` and `OperationsHub` are plain `Microsoft.AspNetCore.SignalR.Hub` subclasses, mapped
  the standard ASP.NET Core way: `app.MapHub<BiddingHub>("/hub/bidding")`. SignalR services come
  from a standard `services.AddSignalR()` inside `AddRelayModule()` — **not** `opts.UseSignalR()`.
- Each Relay notification handler is a pure consumer:
  ```csharp
  public static Task Handle(BidPlaced message, IHubContext<BiddingHub> hub, CancellationToken ct)
      => hub.Clients.Group($"listing:{message.ListingId}")
             .SendAsync("ReceiveMessage", notification, ct);
  ```
  It returns `Task`, never a cascaded message, satisfying the Relay-never-publishes constraint.
- **Group targeting is explicit in the handler**, not derived automatically from a marker interface.
  The handler chooses the group key (`listing:{id}`, `bidder:{id}`, `ops:staff`).
- The raw notification record is the `ReceiveMessage` payload (no CloudEvents envelope — that
  wrapper is a path-(a) transport behaviour). Clients deserialize the record directly.
- `WolverineFx.SignalR` remains the Relay BC's referenced package. It is the canonical Relay
  dependency and keeps the `WolverineHub`-transport door open for a future slice, but its
  transport registration (`opts.UseSignalR()` / `ToSignalR()`) is **not** used.

## Options Considered

**Option A — `WolverineFx.SignalR` transport (`opts.UseSignalR()` + `ToSignalR()`).**
The documented pattern. Rejected: it pushes to `IHubContext<WolverineHub>`, not to the mapped
application hubs, so it does not deliver to clients connected at `/hub/bidding`. Proven non-working
in the vision doc. It also models the push as a *published message*, which sits awkwardly with the
Relay-never-publishes constraint.

**Option B — plain `Hub` + direct `IHubContext` push (chosen).** Works end-to-end with mapped hubs,
gives explicit per-message group control, and makes every handler a pure `Task`-returning consumer
that publishes nothing. Trade-off: bypasses the marker-interface routing sugar and the CloudEvents
envelope, and contradicts the skill's "Lessons Learned" caution against injecting `IHubContext`
directly — a caution this ADR supersedes for CritterBids (see below).

**Option C — subclass `WolverineHub` and use `opts.UseSignalR<BiddingHub>()`.** Would let the
transport target the mapped hub. Rejected for S5: it couples Relay to the framework hub base, still
models pushes as published messages (Relay-never-publishes friction), and is heavier than needed for
a one-directional broadcast. The `WolverineFx.SignalR` reference is retained so this remains a
future option if bidirectional or transport-routed hubs are wanted later.

## Consequences

- **Positive.** End-to-end working pushes to the hubs CritterBids actually maps; explicit, readable
  group targeting; handlers are trivially pure consumers; deterministic to integration-test with a
  real SignalR client (see M6-S5 tests). No envelope-parsing burden on the React clients.
- **Negative / accepted.** We forgo the marker-interface routing and CloudEvents envelope. The
  marker interfaces (`IBiddingHubMessage`, `IOperationsHubMessage`) are retained as documentation /
  a future-door affordance but carry no routing behaviour under path (b).
- **Skill revision.** This ADR supersedes the `IHubContext`-injection caution in
  `docs/skills/wolverine-signalr.md` § Lessons Learned *for CritterBids Relay*: direct
  `IHubContext<THub>` injection in a Wolverine handler is the **endorsed** Relay pattern, precisely
  because the transport does not reach mapped hubs. The skill's first lived example should cite
  Relay's M6-S5 handlers.
- **Scope.** Applies to every Relay hub and push handler. Future hubs follow the same shape unless a
  bidirectional or transport-routed requirement reopens Option C.

## Revisit Trigger

A future requirement for **bidirectional** hub messaging (client→server commands routed through
Wolverine) or for the transport's automatic fan-out across a multi-node backplane. Either would
justify revisiting Option C (`WolverineHub` subclass + `opts.UseSignalR<THub>()`); the retained
`WolverineFx.SignalR` reference keeps that path open without a new dependency.
