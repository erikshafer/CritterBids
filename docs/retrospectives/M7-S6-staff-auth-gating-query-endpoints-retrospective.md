# M7-S6: Staff Auth Gating + Query Endpoints - Retrospective

**Date:** 2026-06-08
**Milestone:** M7 - Operations BC
**Slice:** S6 of 7 - staff authentication resumption (ADR-024) + the staff-facing query surface over the operator read models
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M7-S6-staff-auth-gating-query-endpoints.md`

## Baseline

- Build clean at session start: 0 errors / 0 warnings across `CritterBids.slnx`; full suite green
  (240 tests, M7-S5 close).
- The platform had run fully `[AllowAnonymous]` since M1. `Program.cs` called bare
  `AddAuthentication()`/`AddAuthorization()` with **no** default scheme — the latent trap ADR-024
  names: a naive `[Authorize]` flip would 500 (no `DefaultChallengeScheme`) rather than 401.
- All six Operations views existed (S2–S5): `SettlementQueueView`, `LotBoardView`, `BidActivityEntry`,
  `OperationsObligationsView`, `SessionActivityView`, `ParticipantActivityView`. None had a query
  endpoint — the BC was a pure projection consumer.
- Only `ResolveDisputeEndpoint` (Obligations) existed among the four staff mutations; `WithdrawListing`,
  `CreateSession`, `StartSession` were command-only (dispatched via `bus.InvokeAsync` in tests), with
  no HTTP surface.
- `OperationsHub` (Relay) was mapped but anonymous; `BiddingHub` is intentionally public.

## Open questions resolved (escalated to human, then implemented)

1. **Query-endpoint HTTP contract — confirmed with user.** One `[WolverineGet]` per view under
   `/api/operations/*`, each returning the view record as `IReadOnlyList<T>` (empty array, never 404),
   no pagination for MVP, deterministic ordering (timestamp desc then id). `OperationsObligationsView`
   splits into **two** endpoints — `obligations/escalations` (`QueueState.Escalated`) and
   `obligations/disputes` (`QueueState.Disputed`) — both returning real rows. Seven endpoints total.
2. **`OperationsHub` staff-group targeting — deferred.** ADR-024 item 6 already decided defer past M7:
   once gated, `Clients.All` *is* an all-staff broadcast. Gating only this slice; no Relay group edit.
3. **No `wolverine-http`/auth skill exists — authored against precedent, recorded the gap.** Path (a):
   built against ADR-024 + the `CatalogEndpoints` read-path precedent; no skill-predecessor session.
   The skill gap is recorded here (see Key learnings) as a candidate for a future `wolverine-http-auth`
   skill file.
4. **403 reachability — unreachable, sized the matrix down.** Under the single shared secret the
   `StaffToken` handler only ever issues a principal *carrying* the `staff` claim on an exact match,
   else `NoResult` → 401. There is no authenticated-but-unauthorized path, so **403 is structurally
   unreachable**. The test matrix is 401 (missing/invalid) + success only.

## Items completed

| Item | Description |
|------|-------------|
| S6a | `StaffAuthConstants` — scheme/policy/header/query-key/hub-path/claim/config-key constants (Api) |
| S6b | `StaffTokenAuthenticationHandler` — header (HTTP) + `access_token` (hub path) credential read, fixed-time compare, `staff` claim on match, `NoResult`→401 otherwise |
| S6c | `AddStaffTokenAuthentication()` + `AddStaffAuthorizationPolicy()` + `EnsureStaffTokenConfigured()` extensions; `Program.cs` wiring replacing the bare calls; Production-gated empty-token startup guard |
| S6d | `OperationsQueryEndpoints` — seven gated `[WolverineGet]` endpoints over all six views, obligations split by `QueueState`, deterministic ordering, real rows |
| S6e | Gated `ResolveDisputeEndpoint` (`[AllowAnonymous]`→`[Authorize(Policy="StaffOnly")]`) |
| S6f | Wire-then-gate `WithdrawListingEndpoint` (new thin endpoint, Selling) |
| S6g | Wire-then-gate `CreateSessionEndpoint` + `StartSessionEndpoint` (new thin endpoints, Auctions) |
| S6h | `[Authorize(Policy="StaffOnly")]` on `OperationsHub` (Relay); `BiddingHub` stays anonymous |
| S6i | Real-Kestrel + Testcontainers auth fixture + 401/success HTTP assertions + obligations filter + hub connect-rejected/established assertions |
| S6j | `access_token` redaction + HTTPS-only posture note at the hub mapping (Program.cs) |
| S6k | `CLAUDE.md` supersede of the `[AllowAnonymous]`-through-M6 convention |

## S6a–S6c: Auth primitives + wiring

The auth scheme, handler, policy registration, and startup guard all live in **`CritterBids.Api`** —
the host owns auth (ADR-024). Every test project already references `CritterBids.Api`, so the
real-Kestrel fixture reuses the **exact production extension** (`AddStaffTokenAuthentication()`); there
is no prod/test drift in the auth wiring.

The handler is `AuthenticationHandler<AuthenticationSchemeOptions>` with the modern .NET 8+ ctor (no
`ISystemClock`). It reads the configured token from `IConfiguration["OperationsAuth:StaffToken"]`; an
empty/missing configured token authenticates nothing (`NoResult`). For the `/hub/operations` path —
matched with `StartsWithSegments`, **not** equality, so the negotiate POST is covered — it reads the
`access_token` query value; for all other paths it reads the `X-Staff-Token` header. The presented
credential is compared with `CryptographicOperations.FixedTimeEquals` over UTF-8 bytes. A match issues a
principal carrying the `staff=true` claim → `Success`; a mismatch is `NoResult` → 401.

**The startup guard is Production-gated** (`environment.IsProduction()`). This was the load-bearing
decision for the test surface: the eight `AlbaHost.For<Program>` fixtures boot real `Program` in the
Development environment, and the new auth fixture runs in a non-Production environment — so the guard
never fires there, while a real Production deploy with no configured token fails fast. Verified by the
standalone `StaffTokenStartupGuardTests` (4 cases) and by the green full suite.

## S6d: Query endpoints

`OperationsQueryEndpoints` is one file of seven `[WolverineGet]` endpoints in `CritterBids.Operations`,
each `[Authorize(Policy = "StaffOnly")]`. Each loads via a lightweight Marten query with **explicit
deterministic ordering** (the per-view timestamp desc, then id) and returns `IReadOnlyList<T>` — an
empty array when there are no rows, never a 404. The obligations surface is two endpoints filtering on
`QueueState.Escalated` / `QueueState.Disputed` respectively; the filter is asserted by seeding mixed
queue states and proving each endpoint returns only its state (terminal/Active excluded).

**BC call sites use the literal policy string `"StaffOnly"`**, not the `StaffAuthConstants.PolicyName`
constant: a BC must not reference `CritterBids.Api` (cycle), and minting a Contracts constant was barred
by the work-order. Each gated BC endpoint/hub carries an ADR-024 comment naming the literal as
intentional.

## S6e–S6g: Wire-then-gate the mutations

`ResolveDisputeEndpoint` was simply re-attributed. The three command-only mutations got **new thin
endpoints** that return `(IResult, TCommand)` → `(Results.Accepted(...), command)`, cascading the
command to its existing `[WriteAggregate]` handler — a uniform **202**, precondition-free, never
exposing the aggregate handler. They are **separate classes**, deliberately: attaching `[WolverinePost]`
to the existing handlers would have broken the dispatch tests (`CreateSessionDispatchTests` et al.) that
call `bus.InvokeAsync` directly. This is why the full suite stayed green with zero ripple.

Command shapes wired: `WithdrawListing(Guid ListingId, Guid WithdrawnBy)`,
`CreateSession(string Title, int DurationMinutes)`, `StartSession(Guid SessionId)`,
`ResolveDispute(Guid ObligationId, Guid DisputeId, string ResolutionType)`. Routes:
`/api/selling/listings/withdraw`, `/api/sessions`, `/api/sessions/start`,
`/api/obligations/disputes/resolve`.

## S6h: Hub gating

`OperationsHub` carries `[Authorize(Policy = "StaffOnly")]`; `BiddingHub` stays anonymous (bidders are
the public). The Relay hub fixtures (`RelayHubTestFixture`) were updated with the auth wiring + an
in-memory token + `?access_token=` appended to `OperationsHubUrl`, so the 15 existing
`OperationsHubPushTests` connect through the gate unchanged and still pass.

## S6i: Real-Kestrel + Testcontainers auth tests

ADR-024 item 8 mandates a **real Kestrel** host — SignalR's WebSocket transport cannot run under the
in-memory `TestServer`. `StaffAuthTestFixture` hand-builds a real-Kestrel host registering Marten + all
seven modules + Relay (mirroring `Program` exactly, so **no discovery exclusions** are needed — every
endpoint maps, every handler dependency resolves). It runs Wolverine in solo mode with external
transports disabled and **buffered** (not durable) local queues, so the benign mutation cascades against
non-existent aggregates don't storm durable retries. Forty tests total:

- `StaffAuthQueryEndpointTests` — 401 (missing), 401 (invalid), 200-with-real-rows per endpoint, plus
  the obligations queue-state filter.
- `StaffAuthMutationEndpointTests` — 401 (missing), 401 (invalid), 202 (valid) for all four mutations.
- `StaffAuthHubTests` — connect rejected without `access_token` (`HttpRequestException` at negotiate),
  rejected with an invalid token, established with the valid token.
- `StaffTokenStartupGuardTests` — 4 standalone unit cases for the Production-gated empty-token guard.

The valid hub test connects to `/hub/operations?access_token=<token>` explicitly in the URL — it does
**not** rely on `AccessTokenProvider` (which would send a Bearer header the scheme ignores).

## S6j: Redaction + HTTPS posture

A comment at the `OperationsHub` mapping in `Program.cs` records the two hardening facts: (1) no HTTP
request-logging middleware is registered in this pipeline and the handler never logs the token, so the
`access_token` query value is never written to logs; (2) Production must terminate TLS in front of the
host (HTTPS-only) so the query-string credential is never sent in cleartext — host/ingress config, not
application code.

## Test results

- `CritterBids.Api.Tests`: 40 passed (all new this slice — auth fixture, query, mutation, hub, startup
  guard).
- Full `dotnet test CritterBids.slnx`: **280 passed, 0 failed, 0 skipped** across 10 assemblies, green
  on the first red-run — zero ripple. The predicted Relay hub-fixture fix held; the Auctions/Selling
  dispatch tests survived (thin separate endpoints); the eight Alba `Program`-boot fixtures survived
  (Production-gated guard + `[AllowAnonymous]` existing endpoints bypass the policy).

## Build state at session close

- `dotnet build CritterBids.slnx -c Debug`: Build succeeded, 0 Warnings, 0 Errors.

## Key learnings

- **A separate thin endpoint is the zero-ripple way to wire-then-gate.** Re-homing `[WolverinePost]`
  onto an existing `[WriteAggregate]` handler would have collided with the direct-dispatch tests. The
  thin `(IResult, TCommand)` cascade endpoint adds the HTTP+auth surface without touching the handler
  the dispatch tests exercise.
- **Production-gate the startup guard or it fights the test hosts.** Eight fixtures boot real `Program`;
  an unconditional empty-token guard would have failed every one. Gating to `IsProduction()` keeps the
  fast-fail in real deploys while leaving Development/Testing hosts free.
- **403 is unreachable under a single shared secret — don't write the test.** The matrix is 401 +
  success. Recognising this up front kept the test surface honest rather than asserting an impossible
  status.
- **Skill gap recorded:** there is no `wolverine-http-auth` / Critter-Stack auth-scheme skill file.
  This slice authored against ADR-024 + the read-path precedent; a future skill could codify the
  default-scheme trap, the `StartsWithSegments` hub-path read, and the real-Kestrel hub-test mandate.

## Judgment calls flagged (autopilot defaults taken)

- **Hub group targeting deferred** (OQ2 / ADR-024 item 6) — gating only; no Relay edit. A per-group
  staff broadcast is a post-M7 refinement.
- **Mutation response = uniform 202, new-id-in-body deferred** — `CreateSession` returns 202 without a
  created-id body for MVP, matching the `ResolveDisputeEndpoint` shape rather than minting a 201+Location
  contract this slice.
- **Literal `"StaffOnly"` at BC call sites** — the alternative (a Contracts constant) was barred; the
  literal-with-ADR-024-comment is the pinned choice, accepted as a minor duplication.

## Spec delta — landed?

The spec delta landed as written. ADR-024 is now code: the config-bound `StaffToken` default
authenticate+challenge scheme and the single `StaffOnly` policy are registered in `Program.cs`
(replacing the bare `AddAuthentication()`/`AddAuthorization()` that carried the no-default-scheme trap),
with a Production-gated empty-token startup guard. All six Operations views gained read-only
`StaffOnly`-gated query endpoints (seven endpoints — obligations split into escalations/disputes by
`QueueState`), returning real rows as `IReadOnlyList<T>`. The three command-only staff mutations
(`WithdrawListing`, `CreateSession`, `StartSession`) were wired as new owning-BC HTTP endpoints **then**
gated, and `ResolveDisputeEndpoint` was gated in place — all four uniform 202. `OperationsHub` is
`[Authorize(Policy="StaffOnly")]` while `BiddingHub` stays public. The gate is proven on a **real
Kestrel** host + Testcontainers Postgres with 401-vs-success HTTP assertions and hub
connect-rejected/established assertions (403 omitted as structurally unreachable). The
`[AllowAnonymous]`-through-M6 line in `CLAUDE.md` was superseded to name the `StaffOnly`/ADR-024 posture.
No Contracts type was added; no BC references another BC; no "Event"-suffixed name; no "paddle". **S7**
(end-to-end cross-BC journey, route audit, `bounded-contexts.md` Operations status flip) and per-user /
role auth (post-MVP) remain.

## Verification checklist

- [x] `StaffToken` registered as the default authenticate **and** challenge scheme; `StaffOnly` policy
  registered once in `Program.cs`; the bare `AddAuthentication()`/`AddAuthorization()` calls replaced.
- [x] Empty-token startup guard fails fast in Production only; the eight Alba `Program`-boot fixtures and
  the auth fixture (non-Production) are unaffected; guard asserted by `StaffTokenStartupGuardTests`.
- [x] Seven `[WolverineGet]` endpoints over the six views, each `[Authorize(Policy="StaffOnly")]`,
  returning `IReadOnlyList<T>` (empty, never 404) with deterministic ordering; obligations split by
  `QueueState` into escalations/disputes, asserted to return only their state.
- [x] `ResolveDisputeEndpoint` gated in place; `WithdrawListing`/`CreateSession`/`StartSession` wired as
  new thin owning-BC endpoints then gated — uniform 202; existing direct-dispatch tests unbroken.
- [x] `OperationsHub` `[Authorize(Policy="StaffOnly")]`; `BiddingHub` anonymous; the hub credential read
  from `access_token` for the `/hub/operations` path via `StartsWithSegments` (negotiate covered).
- [x] Gate proven on real Kestrel + Testcontainers: 401 (missing/invalid) + success per HTTP surface;
  hub connection rejected without token, rejected with invalid token, established with valid token; no
  403 case (unreachable under the single shared secret).
- [x] `access_token` never logged (no request-logging middleware; handler logs no token value);
  HTTPS-only posture documented at the hub mapping.
- [x] `CLAUDE.md` `[AllowAnonymous]`-through-M6 convention superseded to the `StaffOnly`/ADR-024 posture.
- [x] Auth primitives live in `CritterBids.Api`; BC call sites use the literal `"StaffOnly"` with an
  ADR-024 comment; no BC references `CritterBids.Api`; no new `CritterBids.Contracts` type.
- [x] `dotnet build CritterBids.slnx` 0 warnings / 0 errors; full `dotnet test` green (280/280) with no
  regressions, green on the first red-run.
- [x] No "Event"-suffixed type name; no "paddle" reference; `sealed record` / `IReadOnlyList<T>` held.
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer.

## What remains / next session should verify

- **S7** owns the end-to-end cross-BC journey test that exercises the gated staff surface, the
  `Program.cs` route audit, and the `bounded-contexts.md` Operations status flip.
- **Per-user / role-based auth** (beyond the single shared staff secret) remains post-MVP, as ADR-024
  records.
- **Hub group targeting** (per-staff-group SignalR broadcast) is the deferred OQ2 refinement past M7.
- A future **`wolverine-http-auth` skill** could codify the patterns this slice authored against
  precedent (default-scheme trap, `StartsWithSegments` hub-path credential read, real-Kestrel hub test).
