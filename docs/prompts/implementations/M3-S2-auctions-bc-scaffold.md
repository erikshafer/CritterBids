# M3-S2: Auctions BC Scaffold

**Milestone:** M3 — Auctions BC
**Session:** S2 of 7
**Prompt file:** `docs/prompts/implementations/M3-S2-auctions-bc-scaffold.md`
**Baseline:** 44 tests passing · `dotnet build` 0 errors, 0 warnings · M3-S1 complete

---

## Goal

Stand up the `CritterBids.Auctions` and `CritterBids.Auctions.Tests` projects and wire them into the solution, the Api host, and the Wolverine/Marten configuration — nothing more. The scaffold must compile, register cleanly, boot cleanly, and contribute nothing to behavior. No handlers, no DCB, no saga, no RabbitMQ queues, no event type registrations. S3 adds the first consumer; S4 lands the DCB; S5 lands the saga. All S2 is doing is creating the shelf that the next three sessions fill.

S1 locked the Auctions integration vocabulary (nine contract stubs under `CritterBids.Contracts.Auctions`, Gate 4 deferred, W002-7 and W002-9 resolved). S2 should walk in with zero vocabulary ambiguity. If any surfaces, stop and flag — do not pivot in-session.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M3-auctions-bc.md` | Milestone scope — S2 deliverables are §4 (solution layout), §5 (infrastructure), §9 (session breakdown) |
| `docs/retrospectives/M3-S1-auctions-foundation-decisions-retrospective.md` | "What M3-S2 should know" section — decisions S2 walks in with |
| `docs/skills/adding-bc-module.md` | Canonical BC scaffold pattern — ToC, Marten BC registration, host-level settings, Program.cs wiring, checklist. Updated in S1 to reflect ADR 011. |
| `src/CritterBids.Selling/SellingModule.cs` | Most recent Marten BC module precedent — shape and placement of `ConfigureMarten` callback |
| `src/CritterBids.Api/Program.cs` | Edit site — module registration block and Wolverine assembly discovery |
| `src/CritterBids.Contracts/Auctions/` | Nine contract stubs from S1 — referenced only by assembly presence in S2; no event type registration yet |

---

## In scope

- **Create `src/CritterBids.Auctions/` class library project.** Target framework and package references match the other BC projects (`CritterBids.Selling`, `CritterBids.Listings`). Add to `CritterBids.slnx` under the `/src/` folder node. The project has no reference to `CritterBids.Contracts` yet — S3 adds it when the `ListingPublished` consumer needs `CritterBids.Contracts.Selling.ListingPublished` and produces `CritterBids.Contracts.Auctions.BiddingOpened`.

- **Create `tests/CritterBids.Auctions.Tests/` project.** Sibling to the production project per Layout 2 (pinned M1-S1). xUnit + Shouldly; Testcontainers/Alba references match the most recent BC test project. Add to `CritterBids.slnx` under the `/tests/` folder node. The project references `CritterBids.Auctions`.

- **Author `AuctionsModule.cs` in `CritterBids.Auctions`.** Single `public static class AuctionsModule` with `AddAuctionsModule(this IServiceCollection services) : IServiceCollection`. Internal shape mirrors `SellingModule.AddSellingModule` exactly:

  ```csharp
  services.ConfigureMarten(opts =>
  {
      opts.Schema.For<Listing>().DatabaseSchemaName("auctions");
      opts.Projections.LiveStreamAggregation<Listing>();
  });

  return services;
  ```

  **No `opts.Events.AddEventType<T>()` calls.** Event type registrations land with their first use — `BiddingOpened` in S3's consumer, bid-and-friends in S4's DCB handler. The M2 key learning about silent `AggregateStreamAsync<T>` null returns from registering event types ahead of their `Apply()` methods applies here and is the reason for the deferral.

- **Author `Listing` aggregate empty shell in `CritterBids.Auctions/Listing.cs`.** Namespace `CritterBids.Auctions`. Class body carries only `public Guid Id { get; set; }` plus an `// S4 adds bidding state fields (CurrentHighBid, BidderId, BidCount, ReserveStatus, BuyItNowAvailable, ScheduledCloseAt) and Apply() methods per the DCB boundary model.` comment — nothing else. No constructor, no behavior, no `Apply()` methods. The aggregate exists solely so `opts.Projections.LiveStreamAggregation<Listing>()` has a type to bind to.

- **Wire Auctions into `src/CritterBids.Api/Program.cs`.** Three concrete edits:
  1. Add `using CritterBids.Auctions;` at the top.
  2. In the `UseWolverine` block, add `opts.Discovery.IncludeAssembly(typeof(Listing).Assembly);` after the existing `IncludeAssembly` lines for Participants/Selling/Listings.
  3. In the postgres-guarded modules block, add `builder.Services.AddAuctionsModule();` after `AddListingsModule();`.

- **Add `<ProjectReference>` from `CritterBids.Api.csproj` to `CritterBids.Auctions.csproj`.** Required for the `typeof(Listing).Assembly` reference in `Program.cs` to resolve per the M2-S7 discovery documented in `adding-bc-module.md`. Not optional.

- **Smoke test in `CritterBids.Auctions.Tests`.** One test minimum, following the test-fixture pattern established in the other BC test projects (Alba + Testcontainers if the BC test projects already use them by M3; otherwise a plain DI/ServiceCollection test). The test verifies: (a) `AddAuctionsModule()` registers cleanly on a service collection alongside the other BC modules and `AddMarten()`, (b) the Api boots green with the Auctions module in place. If an Alba-based fixture is the pattern, a single "Api responds 200 on a health-like route with Auctions registered" test is sufficient. The goal is a green guard that fails fast if a future edit breaks scaffold wiring, not exhaustive coverage.

- **Session retrospective** at `docs/retrospectives/M3-S2-auctions-bc-scaffold-retrospective.md`.

---

## Explicitly out of scope

- **Any event type registrations for Auctions events.** `BiddingOpened`, `BidPlaced`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`, `BiddingClosed`, `ListingSold`, `ListingPassed` — all nine stay unregistered until their first-use session (S3 for `BiddingOpened`; S4 for the bid-and-friends batch; S5 for the closing-outcome batch).
- **Any handler, command, or endpoint.** No `PlaceBid`, no `BuyNow`, no `ListingPublishedHandler`, no HTTP endpoints.
- **Any DCB artifact.** No `BidConsistencyState`, no `[BoundaryModel]`, no `EventTagQuery`. S4 lands all of it.
- **Any saga artifact.** No `AuctionClosingSaga`, no `[SagaIdentity]`, no scheduled messages. S5 lands all of it.
- **`Listing` aggregate behavior.** No `Apply()` methods, no invariants, no state fields beyond `Id`. Every bidding-state field belongs to S4.
- **RabbitMQ queue wiring.** `auctions-selling-events` is wired in S3; `listings-auctions-events` is wired in S5 or S6. `Program.cs` `opts.PublishMessage` / `opts.ListenToRabbitQueue` blocks do not gain Auctions entries in this session.
- **Contracts project edits.** The nine stubs from S1 stand as-authored. No reference addition from `CritterBids.Auctions` to `CritterBids.Contracts` yet — S3 adds it.
- **Listings BC edits.** `CatalogListingView` gains auction-status fields in S6, not here.
- **ADR, skill, or workshop doc edits.** S1 closed the docs queue for scaffold-adjacent decisions. If S2 surfaces a skill gap (e.g. `adding-bc-module.md` missing a step that would have helped), note it in the retrospective — do not edit in-session.
- **Gate 4 re-evaluation.** Event row IDs stay on Marten engine default per the S1 deferral; Gate 4 re-evaluation trigger is the M3-S4 prompt draft, not this session.

---

## Conventions to pin or follow

- **Layout 2 one-prod-one-test-sibling.** Both projects land in the same PR; the `.slnx` edit adds both nodes in the same commit as the `.csproj` files.
- **Module shape per `adding-bc-module.md`.** `services.ConfigureMarten()` inside a static `AddXyzModule` extension returning `IServiceCollection`; no `AddMarten()` call inside the module (the primary store is owned by `Program.cs` per ADR 009).
- **Schema name `auctions`.** Per `opts.Schema.For<Listing>().DatabaseSchemaName("auctions")` inside the `ConfigureMarten` callback, not via top-level `opts.DatabaseSchemaName`. The primary store's `DatabaseSchemaName = "public"` remains the default; per-type schema overrides are the isolation mechanism.
- **`Program.cs` edit order matches module-add order.** Auctions registrations come after Listings and before the ASP.NET / Wolverine HTTP block, preserving the existing Participants → Selling → Listings → Auctions sequence for both `IncludeAssembly` and `Add*Module` calls.
- **UUID v7 for any stream ID created in the smoke test.** `Guid.CreateVersion7()`, not `Guid.NewGuid()`. Consistent with every Marten BC per ADR 007 stream-ID section. (The smoke test most likely does not create a stream at all, but this is the applicable convention if it does.)
- **No auth changes.** `[AllowAnonymous]` stance is unchanged; no Auctions endpoints exist in S2 to attach `[Authorize]` to.

---

## Acceptance criteria

- [ ] `src/CritterBids.Auctions/CritterBids.Auctions.csproj` exists; target framework and package references match sibling BC projects.
- [ ] `tests/CritterBids.Auctions.Tests/CritterBids.Auctions.Tests.csproj` exists; references `CritterBids.Auctions`; test framework references match sibling BC test projects.
- [ ] `CritterBids.slnx` contains `<Project>` entries for both new projects under the `/src/` and `/tests/` folder nodes respectively.
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` defines `AddAuctionsModule(this IServiceCollection services) : IServiceCollection` that calls `services.ConfigureMarten(...)` with the `auctions` schema mapping for `Listing` and the `LiveStreamAggregation<Listing>()` projection registration, and nothing else.
- [ ] `src/CritterBids.Auctions/Listing.cs` contains a class with only `public Guid Id { get; set; }` plus one forward-looking comment marking what S4 adds. No other members.
- [ ] `AuctionsModule.cs` contains zero `opts.Events.AddEventType<T>()` calls.
- [ ] `src/CritterBids.Api/CritterBids.Api.csproj` has a `<ProjectReference>` to `CritterBids.Auctions.csproj`.
- [ ] `src/CritterBids.Api/Program.cs` — `using CritterBids.Auctions;` present; `opts.Discovery.IncludeAssembly(typeof(Listing).Assembly);` present after the existing `IncludeAssembly` lines; `builder.Services.AddAuctionsModule();` present after `AddListingsModule();`.
- [ ] `Program.cs` `opts.PublishMessage` / `opts.ListenToRabbitQueue` blocks contain zero Auctions-related lines.
- [ ] At least one test in `CritterBids.Auctions.Tests` that asserts the Auctions module registers cleanly alongside the other BC modules and the Api boots green.
- [ ] `dotnet build` — 0 errors, 0 warnings.
- [ ] `dotnet test` — all green; baseline 44 tests still pass; new smoke test(s) pass.
- [ ] `docs/retrospectives/M3-S2-auctions-bc-scaffold-retrospective.md` exists; records the solution layout delta, the Program.cs edit diff (three lines added), the smoke-test shape chosen, any skill gap discovered, and a one-paragraph "what M3-S3 should know" note.

---

## Open questions

- **Smoke-test shape.** If the other BC test projects use Alba + Testcontainers by M3, follow the same pattern — one boot-green test is sufficient. If they don't, a plain `ServiceCollection`-based registration test is the scaffold-appropriate choice. Do not invent a third pattern; match the existing one.
- **Projection registration on an empty aggregate.** `opts.Projections.LiveStreamAggregation<Listing>()` binds to an aggregate type that has no `Apply()` methods yet. Marten tolerates this (live aggregation against zero events returns a default-constructed aggregate). If a Marten 8 change surfaces an error on this shape, it is a genuine blocker — stop and flag rather than dropping the projection line. S4 needs the projection registered and will not want to discover a hole here.

---

## Commit sequence

Three commits, in this order:

1. `feat(auctions): scaffold CritterBids.Auctions project with AuctionsModule and empty Listing aggregate`
2. `feat(auctions): scaffold CritterBids.Auctions.Tests with boot-green smoke test; wire Auctions into Api Program.cs and .slnx`
3. `docs: write M3-S2 retrospective`

The scaffold and wiring are split across commits 1 and 2 so that a reviewer reading commit 1 sees a self-contained new project and commit 2 sees the integration diff (Program.cs + .csproj + .slnx + test). The smoke test lives in commit 2 because it only makes sense once Program.cs references the module. Commit 3 is the retrospective and lands after the green build.
