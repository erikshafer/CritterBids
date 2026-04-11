# M1 — Skeleton

**Status:** Planning
**Scope:** Tier 0 foundation — slices 0.1, 0.2, 0.3 (Participants BC only)
**Companion docs:** [`../workshops/001-flash-session-demo-day-journey.md`](../workshops/001-flash-session-demo-day-journey.md) Phase 4 · [`../workshops/001-scenarios.md`](../workshops/001-scenarios.md) slices 0.2, 0.3 · [`../skills/README.md`](../skills/README.md) · [`../workshops/PARKED-QUESTIONS.md`](../workshops/PARKED-QUESTIONS.md)

---

## 1. Goal & Exit Criteria

### Milestone/phase vocabulary (recorded as of M1)

CritterBids uses **milestones** as the unit of work governance. Each milestone is a numbered sequential unit with a planning doc and a retrospective doc, exits on concrete deliverables, and is sized to a knowable number of sessions. **Phases** are optional informal labels applied to a range of milestones, used in retrospect or with clear boundaries (e.g., "Pre-MVP phase = M1 through the first end-to-end bid"). Phases have no planning docs, no exit criteria, no retrospectives of their own — all rigor lives at the milestone level. No cycles, no sprints, no sub-milestones.

### Goal

Stand up the CritterBids solution skeleton with the Participants BC as the first real implementation. End state: a contributor can clone the repo, run `dotnet run --project src/CritterBids.AppHost` (or the Docker Compose fallback), hit `POST /api/participants/session` to start a session, and `POST /api/participants/{id}/register-seller` to become a seller. All 5 scenarios from `001-scenarios.md` slices 0.2 and 0.3 pass as integration tests.

### Exit criteria

- [ ] Solution builds clean with `dotnet build`
- [ ] `dotnet run --project src/CritterBids.AppHost` boots the API + Postgres + SQL Server + RabbitMQ via .NET Aspire, end-to-end verified
- [ ] `docker compose up -d && dotnet run --project src/CritterBids.Api` fallback path verified working
- [ ] Participants BC implemented: `StartParticipantSession` and `RegisterAsSeller` commands, events, HTTP endpoints, Polecat storage
- [ ] `SellerRegistrationCompleted` published via `OutgoingMessages` (no M1 consumer — Selling BC enters in M2 per W004-P1-2)
- [ ] All 5 integration tests from §7 pass against real Polecat + SQL Server via Testcontainers
- [ ] `docs/skills/aspire.md` authored (new skill, retrospectively from sessions 1–4)
- [ ] `docs/skills/polecat-event-sourcing.md` filled in from 🟡 → ✅ based on actual M1 implementation experience
- [ ] Aspire MCP discovery note committed (candidate servers tracked, no integration committed)
- [ ] `docs/decisions/0001-uuid-strategy.md` ADR authored as **Proposed** (v5 for stream IDs, v7 under consideration for event row IDs / high-write projections — gated on Marten 8 / Polecat 2 capability check and JasperFx team input)
- [ ] M1 retrospective doc written

---

## 2. In Scope

Three slices from Tier 0, Participants BC only:

| Slice | Name | Scenarios (from `001-scenarios.md`) |
|---|---|---|
| 0.1 | Project scaffolding | — (infrastructure) |
| 0.2 | Start anonymous session | Happy path; display name unique within active sessions |
| 0.3 | Register as seller | Happy path; reject no-session; reject already-registered |

Slice 0.3 emits `SellerRegistrationCompleted` via `OutgoingMessages` with **no consumer in M1**. The Selling BC enters in M2 and will own the `RegisteredSellers` projection per W004-P1-2.

---

## 3. Explicit Non-Goals

Hard line — if you catch yourself building any of these in M1, stop and flag it:

- Selling, Auctions, Settlement, Obligations, Relay, Operations, Listings BCs
- Any real-time / SignalR wiring
- Any frontend (`critterbids-web`, `critterbids-ops`)
- Real authentication scheme — M1 uses `[AllowAnonymous]` everywhere (see §6)
- Any slice beyond 0.1 / 0.2 / 0.3
- `tests/CritterBids.E2E.Tests` project — deferred until the first E2E scenario exists
- Marten configuration (Participants is Polecat; Marten arrives with Selling in M2)

---

## 4. Solution Layout

```
CritterBids/
├── CritterBids.sln
├── Directory.Packages.props              # central package management
├── docker-compose.yml                    # fallback for non-Aspire contributors
├── src/
│   ├── CritterBids.AppHost/              # .NET Aspire orchestration (primary)
│   ├── CritterBids.Api/                  # API host, Program.cs, AddXyzModule() calls
│   ├── CritterBids.Contracts/            # integration event types (cross-BC)
│   └── CritterBids.Participants/         # BC class library — first real BC
└── tests/
    ├── CritterBids.UnitTests/            # xUnit + Shouldly, no heavy deps
    │   └── Participants/
    └── CritterBids.IntegrationTests/     # Alba + Testcontainers + Polecat
        └── Participants/
```

**Test layout rationale (Layout 2 — tier-first, BC-as-folders):** Fast tests stay fast. Heavyweight dependencies (Testcontainers, Alba, Polecat) isolated by tier, not per-BC. Avoids project proliferation in a modular monolith. BC folders inside each test project provide the per-BC organization.

`Participants` is the only BC project in M1. `CritterBids.Contracts` is created but holds only `SellerRegistrationCompleted` (needed for M2 consumer).

---

## 5. Infrastructure Baseline

### Aspire AppHost resources (primary path)

`CritterBids.AppHost/Program.cs` provisions:

- **PostgreSQL** — for future Marten BCs (not used in M1, but wired so M2 doesn't need infra changes)
- **SQL Server** — for Polecat (Participants BC storage in M1)
- **RabbitMQ** — for Wolverine transport (no active subscribers in M1, but `SellerRegistrationCompleted` is published into it)
- **CritterBids.Api** — the API host, referencing all provisioned resources

### Docker Compose fallback

`docker-compose.yml` in repo root provides Postgres + SQL Server + RabbitMQ with ports/credentials matching the Aspire-provisioned defaults. Contributors who don't want Aspire run `docker compose up -d && dotnet run --project src/CritterBids.Api`. Both paths must work at M1 exit.

### Polecat configuration (Participants BC)

- SQL Server connection string resolved from Aspire (primary) or `appsettings.json` (fallback)
- `opts.Policies.AutoApplyTransactions()` — see §6 pinning note
- Event stream registered for `Participant` aggregate
- No projections in M1 (Participants has no views)

### Marten configuration

**None.** Participants is Polecat. Marten wiring arrives with Selling in M2.

### Wolverine configuration

- RabbitMQ transport configured
- `OutgoingMessages` pattern enabled for integration event publishing
- No active message handlers in M1 beyond the Participants command handlers (which return events, not messages)

---

## 6. Conventions Pinned

Conventions inherit from `CLAUDE.md` unless overridden below.

- **Stream IDs:** UUID v5 with BC-specific namespace constant. `ParticipantsNamespace = new Guid("TODO-GENERATE-AT-IMPLEMENTATION");` — resolved when session 2 lands. v5 stays the convention for stream IDs because determinism from business key is load-bearing for idempotent stream creation.
- **Event row IDs:** Not pinned in M1. See §8 and `docs/decisions/0001-uuid-strategy.md` (Proposed).
- **Integration events:** Published via `OutgoingMessages` collection returned from handlers. Never `IMessageBus` directly. `bus.ScheduleAsync()` is the only justified `IMessageBus` use in handlers — not needed in M1.
- **AutoApplyTransactions:** Required in the Participants BC's Polecat configuration. `CLAUDE.md` currently phrases this rule in Marten-specific terms; the Polecat equivalent is pinned here for M1 and CLAUDE.md will be cleaned up later as conventions stabilize.
- **`[AllowAnonymous]` — M1-ONLY OVERRIDE:** The global convention in `CLAUDE.md` is `[Authorize]` on all non-auth endpoints from first commit. **M1 overrides this** with `[AllowAnonymous]` on every endpoint. Real authentication scheme is deferred (§8). This override does not extend to M2.
- **Records:** `sealed record` for all commands, events, queries, read models — no exceptions.
- **Collections:** `IReadOnlyList<T>` not `List<T>`.
- **Event naming:** No "Event" suffix on domain event type names.
- **Paddle:** Banned. Participants are identified by `BidderId`.
- **Module registration:** `AddParticipantsModule()` extension on `IServiceCollection`, called from `CritterBids.Api/Program.cs`. Establishes the pattern for all future BCs.
- **Skills to load during implementation:** `polecat-event-sourcing.md` (🟡 — fill in as you go), `wolverine-message-handlers.md`, `critter-stack-testing-patterns.md`, `csharp-coding-standards.md`. See `docs/skills/README.md` for the full status ledger — do not duplicate skill content here.

---

## 7. Acceptance Tests

Direct mapping from `001-scenarios.md` slices 0.2 and 0.3 to test methods in `tests/CritterBids.IntegrationTests/Participants/`. Five tests total. All use Alba + Testcontainers + real Polecat.

### `StartParticipantSessionTests.cs`

| Scenario (from `001-scenarios.md` §0.2) | Test method |
|---|---|
| Happy path — new participant session | `StartingSession_FromEmptyStream_ProducesParticipantSessionStarted` |
| Display name is unique within active sessions | `StartingSecondSession_ProducesDifferentDisplayName_ThanActiveSessions` |

### `RegisterAsSellerTests.cs`

| Scenario (from `001-scenarios.md` §0.3) | Test method |
|---|---|
| Happy path — participant becomes a seller | `RegisterAsSeller_WithActiveSession_ProducesSellerRegistrationCompleted` |
| Reject — no active session | `RegisterAsSeller_WithoutActiveSession_IsRejected` |
| Reject — already registered | `RegisterAsSeller_WhenAlreadyRegistered_IsRejectedIdempotently` |

Each test asserts against both (a) the HTTP response shape and (b) the Polecat event stream contents using the patterns in `critter-stack-testing-patterns.md`. The happy-path `RegisterAsSeller` test additionally asserts that `SellerRegistrationCompleted` was enqueued on the Wolverine outbox (no downstream consumer verification — that's M2's job).

---

## 8. Open Questions / Decisions

| ID | Question | Disposition |
|---|---|---|
| M1-D1 | Real authentication scheme | **Deferred.** M1 uses `[AllowAnonymous]`. Revisit when frontend milestone (M6) plans the auth story. |
| M1-D3 | UUID v7 for event row IDs and high-write projection IDs | **Leaning adopt; ADR Proposed.** Authored in session 5 as `docs/decisions/0001-uuid-strategy.md`. Stream IDs stay v5 (determinism is load-bearing). v7 is a legitimate consideration for high-write tables — primarily Auctions bid events and Listings projections — because its Unix-ms prefix gives insert locality and composes well with Postgres range partitioning. Acceptance gates before moving from Proposed to Accepted: (a) verify Marten 8 / Polecat 2 expose event row ID generation at all; (b) verify v7 support or path to it; (c) JasperFx team input. Not blocking M1 — Participants is low-write and M1 doesn't touch Auctions. Re-surfaces naturally at M3 (Auctions BC). |
| M1-D4 | Polecat namespace GUID for Participants stream IDs | **TODO at session 2.** Generate once, pin as constant in code. |
| M1-D5 | CLAUDE.md cleanup: Marten-specific wording of `AutoApplyTransactions` rule | **Deferred cleanup.** Track, address when conventions stabilize. |

---

## 9. Session Breakdown

One session ≈ one PR ≈ one slice or clearly-bounded sub-slice. Every session corresponds to a prompt file under `docs/prompts/`, named `M1-S{n}-{summary}.md`. The prompt file is authoritative for that session's scope; the table below exists only to show sequence and shape. The ten rules in `docs/prompts/README.md` govern how prompts are authored.

| # | Prompt file | Scope summary |
|---|---|---|
| 1 | `docs/prompts/M1-S1-solution-baseline.md` | Solution file, `CritterBids.Api` + `CritterBids.Contracts` projects, Layout 2 test projects, `Directory.Packages.props` with minimal xUnit + Shouldly pins. **No Aspire, no Compose, no BCs.** |
| 2 | *TBD* | Infrastructure baseline — Aspire-vs-Compose decision, Postgres + SQL Server + RabbitMQ wiring, Wolverine + Polecat host configuration. |
| 3 | *TBD* | Participants BC scaffold — Polecat config, empty `Participant` aggregate, `AddParticipantsModule()` extension, UUID v5 namespace constant (resolves M1-D4). |
| 4 | *TBD* | Slice 0.2 — `StartParticipantSession` command, event, handler, endpoint, tests mapping to `001-scenarios.md` §0.2. |
| 5 | *TBD* | Slice 0.3 — `RegisterAsSeller` command, event, handler, endpoint, tests mapping to `001-scenarios.md` §0.3; `SellerRegistrationCompleted` published via `OutgoingMessages`. |
| 6 | *TBD* | Retrospective skills + ADR — `aspire.md`, `polecat-event-sourcing.md` 🟡 → ✅, Aspire MCP discovery note, `docs/decisions/0001-uuid-strategy.md` as Proposed. |

Sessions are intentionally small. Merging is discouraged — the ten rules treat "one prompt equals one PR" as the primary constraint. If a session's scope collapses during implementation, split work off rather than absorbing the next session's scope.

**Retrospective-session positioning:** The final session in each milestone is deliberately retrospective. Skills and ADRs are authored from what was learned during implementation, not speculated up front. This is a convention for all future milestones where new skill docs are a deliverable. M1 retrospectives also feed back into `docs/prompts/README.md` — the prompt template and the ten rules are expected to move as M1 sessions reveal what works and what doesn't.
