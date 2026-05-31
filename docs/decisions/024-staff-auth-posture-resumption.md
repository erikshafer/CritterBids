# ADR 024 — Staff Authentication-Posture Resumption

**Status:** ✅ Accepted
**Date:** 2026-05-30
**Milestone:** M7-S1 — Operations Foundation Decisions (Auth Posture + OpenSpec Adoption + Read-Model Field Freeze)

---

## Context

CritterBids has shipped five milestones (M1–M5) and most of M6 under a deliberate
`[AllowAnonymous]` posture. `CLAUDE.md` pins the convention as `[AllowAnonymous]` "through M6"
and names M7 as the milestone where real authentication planning resumes. M7 is the Operations
milestone: it adds the staff-facing dashboard backend (read models + query endpoints) and is the
first time the platform exposes surfaces that must **not** be reachable by an anonymous
participant. The auth-posture resumption is the headline M7 foundation decision, flagged for an
ADR rather than a silent attribute flip.

The decision is broader than "add `[Authorize]`". `src/CritterBids.Api/Program.cs` today calls
bare `builder.Services.AddAuthentication();` and `builder.Services.AddAuthorization();` with **no
default scheme** registered, and `app.UseAuthentication(); app.UseAuthorization();` are already in
the pipeline. An `[Authorize]` attribute (or a `RequireAuthorization("StaffOnly")` call) issued
against this configuration fails at request time — ASP.NET Core throws
`InvalidOperationException: No authenticationScheme was specified, and there was no
DefaultChallengeScheme found` — because there is no scheme for the authorization middleware to
challenge against. Any naive flip would compile and then 500 on the first guarded request. The ADR
must decide what gets wired so it does not.

A second complication is the SignalR `OperationsHub`. Per ADR 023 and the M6 retro, Relay owns the
hubs; `OperationsHub` is mapped at `/hub/operations` and is the staff dashboard's live feed. A
SignalR `HubConnection` cannot set arbitrary headers on its WebSocket upgrade request, so a
header-only credential scheme would leave the hub unauthenticatable. The staff-auth scheme must
accommodate the hub's transport.

The MVP target named in `bounded-contexts.md` §Operations is a **config-driven staff passphrase**
("Staff authentication seam — config-driven passphrase in MVP, extensible to full staff identity").
Full per-user staff identity, roles beyond staff-vs-anonymous, password reset, and an identity
provider are post-MVP per the M7 milestone §3. This ADR settles the concrete shape of the MVP
posture; it does not widen beyond the config-passphrase target.

This is a decisions slice (M7-S1). It **decides** the scheme, the policy, the endpoint set, the hub
auth, and the test strategy. It writes **no code**: the scheme registration, the policy
registration, and the application of `StaffOnly` to endpoints all land in their implementation
slices (the auth wiring and gating in M7-S6; the Operations query endpoints across M7-S2–S6).

---

## Options Considered

### The credential transport

#### Option A — Custom authentication-handler scheme, config-bound token, header + `access_token` query (chosen)

A custom `AuthenticationHandler<AuthenticationSchemeOptions>` (working name scheme `"StaffToken"`)
registered as the **default authenticate and challenge scheme**. It reads a credential from the
`X-Staff-Token` request header for HTTP requests, and from the `access_token` query-string
parameter for the `/hub/operations` SignalR path (the standard ASP.NET Core SignalR convention,
since `HubConnection` cannot carry custom WebSocket headers). The presented token is compared to a
single value bound from configuration (e.g. `OperationsAuth:StaffToken`, supplied via
appsettings / user-secrets / environment). On match, the handler returns an authenticated
`ClaimsPrincipal` carrying a `staff` claim; on absence or mismatch it returns no result, so the
authorization middleware issues a 401 challenge.

Registering this as the default scheme is precisely the fix for the current no-default-scheme
runtime failure: `[Authorize]` / `RequireAuthorization` now resolve a challenge scheme. It stays
inside the config-passphrase MVP posture — one shared secret, no user store, no token issuance — and
it is the only option that authenticates the hub.

#### Option B — ASP.NET Core cookie authentication

A cookie scheme with a staff sign-in endpoint that sets an auth cookie after passphrase entry.

Rejected. A cookie scheme requires a login endpoint, anti-forgery handling, and session/cookie
lifetime management — none of which the MVP demo posture needs. SignalR can carry cookies on the
negotiate request, so the hub would work, but the added surface (a login page/endpoint, sign-out,
cookie expiry) exceeds the "config-driven passphrase" target and pulls forward UX the M8 frontend
owns.

#### Option C — JWT bearer tokens

`AddJwtBearer` with tokens issued from the staff passphrase.

Rejected. Bearer/JWT implies a token-issuance path (signing keys, expiry, an issue endpoint) for
what is a single shared secret in MVP. It is the right door for full staff identity later, but it
over-builds the MVP and adds key-management concerns with no MVP payoff. The `access_token`
query-string convention this ADR adopts for the hub is the same one `AddJwtBearer` uses, so a later
migration to bearer is not foreclosed.

#### Option D — Header check in middleware / API gateway, no auth scheme

A bespoke middleware (or the existing API-gateway validation layer) inspects `X-Staff-Token` and
short-circuits unauthorized requests, bypassing ASP.NET authentication/authorization entirely.

Rejected. It does not compose with `[Authorize]` / `RequireAuthorization` / `[Authorize(Policy=…)]`,
so each guarded endpoint would carry an ad-hoc check instead of a declarative attribute; it leaves
the already-wired `UseAuthentication()` / `UseAuthorization()` pipeline doing nothing; and it cannot
gate a SignalR hub the way `[Authorize]` on the hub class does. The framework primitives exist and
are already in the pipeline — the right fix populates them, not bypasses them.

### The authorization policy

`StaffOnly` (the milestone working name, confirmed). A single policy: `RequireAuthenticatedUser()`
plus a requirement that the principal carry the `staff` claim the scheme issues. Because the
`StaffToken` scheme only ever issues an authenticated principal when the configured token matches,
in the MVP single-token model an authenticated request is always a staff request. The policy does
not name a scheme — the default scheme covers it.

---

## Decision

**Credential transport: Option A.** A custom config-bound `StaffToken` authentication scheme,
registered as the default authenticate + challenge scheme, reading `X-Staff-Token` (HTTP) and
`access_token` (the `/hub/operations` query string). **Authorization: the `StaffOnly` policy**, an
authenticated principal carrying the scheme-issued `staff` claim.

The decisions this ADR pins for the implementation slices:

1. **Scheme registration (S6).** `Program.cs` registers the `StaffToken` scheme as the default
   authenticate and challenge scheme, replacing the bare `AddAuthentication()` call. The
   configuration key carrying the staff token is bound from configuration (appsettings /
   user-secrets / environment), never hard-coded. The existing `UseAuthentication()` /
   `UseAuthorization()` pipeline order is unchanged.

2. **`StaffOnly` policy (S6).** Registered in `Program.cs`'s `AddAuthorization`. Satisfied by an
   authenticated principal carrying the `staff` claim: the policy is `RequireAuthenticatedUser()` +
   `RequireClaim("staff", "true")`, and the `StaffToken` handler issues exactly that claim
   (type `"staff"`, value `"true"`) on a token match. The claim type/value is fixed here so S6 and
   its tests share one contract (a shared constant in the implementation, not a magic string per
   call site). This is the single MVP policy; no per-action or per-role policies ship in M7.

3. **Endpoints gated with `StaffOnly`.** Decided here, applied in their slices. The code reality at
   this ADR's authoring is that **only `ResolveDisputeEndpoint` exists as an HTTP endpoint today**
   (`[WolverinePost("/api/obligations/disputes/resolve")]`, returns `202 Accepted`); the other three
   staff mutations are command-only handlers with no `[WolverinePost]` surface yet (`WithdrawListing`
   — "no HTTP endpoint in M4, dispatch-only"; `CreateSession` / `StartSession` — "drop-in for the
   HTTP wiring later"). The milestone §2 framing ("the staff mutations already exist as endpoints")
   is therefore forward-looking for those three. The decision is split accordingly:

   | Surface | Endpoint / command | Owning BC | S6 work |
   |---|---|---|---|
   | Resolve a dispute | `ResolveDisputeEndpoint` (exists) | Obligations (M6) | Apply `[Authorize(Policy="StaffOnly")]` |
   | Force-withdraw a listing | `WithdrawListing` (command-only) | Selling (M4-S2) | Wire the owning-BC HTTP endpoint, then gate it |
   | Create a Flash Session | `CreateSession` (command-only) | Auctions (M4-S5) | Wire the owning-BC HTTP endpoint, then gate it |
   | Start a Flash Session | `StartSession` (command-only) | Auctions (M4-S5) | Wire the owning-BC HTTP endpoint, then gate it |
   | Operator dashboard queries | Operations staff query endpoints | Operations (new) | Authored already carrying `StaffOnly` |

   For the three command-only handlers, S6 owns the HTTP-endpoint wiring **inside the command's own
   BC** (not in Operations) and gates it on creation — the gating attaches to the endpoint S6
   introduces. The mutations stay in their owning BCs and are **not** re-homed into Operations (M7
   staff-command ownership boundary). The Operations query endpoints are authored already carrying
   `StaffOnly`.

4. **Staff-token configuration is validated, not trusted.** The configured token must be non-empty;
   an empty or missing configured token never authenticates any request (an empty presented
   credential must not match an empty configured secret). S6 fails host startup outside test/dev if
   the staff-gated surfaces are enabled and no staff token is configured, rather than booting with
   silently inaccessible (or, worse, accidentally open) staff surfaces.

5. **Participant-facing surfaces stay `[AllowAnonymous]` for MVP.** Bidding (`PlaceBid`,
   `RegisterProxyBid`), participant session creation, seller listing creation/publish/revise, and
   the catalog/listing browse queries remain anonymous. M7 introduces auth on **staff** surfaces
   only. Per the M7 milestone §6, the absence of an attribute is treated as a bug once the scheme
   exists: anonymous surfaces carry an explicit `[AllowAnonymous]`, gated surfaces carry
   `StaffOnly`.

6. **SignalR hub authorization.** `OperationsHub` (`/hub/operations`) carries
   `[Authorize(Policy = "StaffOnly")]`. SignalR delivers the hub credential in the `access_token`
   **query string** (the browser `WebSocket`/SSE transports cannot set an `Authorization` header), so
   the `StaffToken` handler must **explicitly** read `Request.Query["access_token"]` for the
   `/hub/operations` path — ASP.NET does not wire the query string into authentication automatically
   for a custom scheme; only `AddJwtBearer` ships that `OnMessageReceived` plumbing. `BiddingHub`
   (`/hub/bidding`) stays anonymous — it is participant-facing. The `OperationsHub`
   staff-**group**-targeting refinement (M6-S6 standardized to `Clients.All`) is **deferred** past M7
   (M7-S1 scope call): once the hub itself is `StaffOnly`-gated every connected client is staff, so
   `Clients.All` is an all-staff broadcast for MVP; group segmentation is post-MVP and would be a
   Relay edit when it lands.

7. **401 vs 403.** A missing or invalid token is unauthenticated → **401** (challenge). An
   authenticated principal lacking the `staff` claim is **403**. In the MVP single-token model a
   valid token always carries the `staff` claim, so the observable contrast is 401 (no/invalid
   token) vs the endpoint's normal success status (valid token); the 403 path is structural headroom
   for the post-MVP per-user staff identity model.

8. **Test strategy.** Authorized-vs-unauthorized assertions run against a **real Kestrel host** with
   a **Testcontainers** Postgres, not Alba's in-memory `TestServer`. Two reasons: a registered
   scheme is required for the guarded paths to behave (a no-scheme host 500s rather than 401s), and
   Alba's in-memory `TestServer` cannot drive a SignalR `HubConnection`, so the `OperationsHub`
   authorization assertion needs a real socket — the hub is the binding reason for real Kestrel.
   The minimum coverage (M7-S6): per guarded HTTP surface, 401 on missing/invalid token and the
   endpoint's normal success status with a valid token (e.g. `202 Accepted` for
   `ResolveDisputeEndpoint`); for `OperationsHub`, connection rejected without a valid `access_token`
   and established with one. Cross-BC consumer handlers that share an event type with another BC
   apply the existing `*BcDiscoveryExclusion` isolation pattern and
   `DisableAllExternalWolverineTransports()` in fixtures (unchanged from M5/M6 testing lessons).

---

## Consequences

- **The `[AllowAnonymous]`-through-M6 pin in `CLAUDE.md` is superseded by this ADR.** The convention
  update is a follow-on the ADR records: M7-S6 applies `StaffOnly` to the named endpoints and adds
  the explicit `[AllowAnonymous]` markers, and the `CLAUDE.md` convention line is revised at that
  point to point at `StaffOnly` / this ADR. M7-S1 changes no endpoint and no `CLAUDE.md` convention
  text — the code is still uniformly anonymous until S6; recording the supersession before the code
  lands keeps the decision citable for S2–S6.

- **The no-default-scheme runtime trap is now documented.** Any slice that adds `[Authorize]` before
  the `StaffToken` scheme is registered will 500, not 401. S6 registers the scheme and applies the
  attributes in the same slice so the two never separate. No earlier slice (S2–S5) adds an
  authorization attribute to any endpoint.

- **`access_token` query-string reading is a deliberate, scoped exception.** The scheme reads the
  credential from the query string only for the `/hub/operations` path (the SignalR transport
  constraint), and from the `X-Staff-Token` header everywhere else. HTTP query-string credentials
  are not accepted on non-hub paths. Because query strings leak into access logs, proxy logs, and
  browser history, S6 mitigates: the staff surfaces are HTTPS-only (the token never crosses the
  wire in clear text), the query-string read is scoped to the single `/hub/operations` path, and the
  `access_token` parameter is redacted from request logging. This residual exposure is the accepted
  cost of the SignalR transport constraint and is retired with the post-MVP move to per-user bearer
  tokens.

- **The MVP shares one staff secret.** There is no per-user staff identity, no audit of *which*
  staff member acted, and no revocation short of rotating the configured token. This is acceptable
  for the conference-demo MVP and is the explicit post-MVP extension point (the scheme issues a
  `staff` claim today; a later per-user model issues richer claims behind the same `StaffOnly`
  policy without changing the guarded endpoints).

- **Migration to bearer is not foreclosed.** The `access_token` query-string convention adopted for
  the hub is the same one `AddJwtBearer` uses, so a future move from the shared-passphrase scheme to
  per-user bearer tokens swaps the scheme registration without touching the hub or the `StaffOnly`
  attributes.

### Revisit trigger

This ADR is reopened when **per-user staff identity becomes a requirement** — staff audit ("who
resolved this dispute?"), role granularity beyond staff-vs-anonymous, or an external identity
provider. At that point the `StaffToken` scheme is replaced (likely by `AddJwtBearer` or an OIDC
scheme) behind the unchanged `StaffOnly` policy, and the 403 path this ADR reserved becomes live.
The endpoint set and the policy name do not change on that migration; only the scheme does.

---

## References

- [ADR 011 — All-Marten Pivot](011-all-marten-pivot.md) — Operations is Marten-from-birth; the host
  whose module block this ADR's scheme registration joins
- [ADR 023 — Relay Reactive-Broadcast Architecture](023-relay-reactive-broadcast-architecture.md) —
  fixes `OperationsHub` ownership in Relay; this ADR gates that hub without moving it
- `docs/milestones/M7-operations-bc.md` §2 "Auth posture resumption" + §6 "Auth posture resumption"
  / "`[AllowAnonymous]` → `StaffOnly` transition is explicit" — the milestone scope this ADR settles
- `docs/narratives/008-operator-resolves-dispute-with-extension.md` — the operator surfaces
  (`OperationsObligationsView`, dispute/escalation queues) this ADR's `StaffOnly` policy protects
- `CLAUDE.md` — the `[AllowAnonymous]`-through-M6 convention this ADR supersedes (code application at
  M7-S6)
- `src/CritterBids.Api/Program.cs` (lines ~321–328 at this ADR's authoring) — the bare
  `AddAuthentication()` / `AddAuthorization()` + `UseAuthentication()` / `UseAuthorization()` state
  this ADR's scheme registration replaces in S6
