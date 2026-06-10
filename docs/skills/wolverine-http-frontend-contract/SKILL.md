---
name: wolverine-http-frontend-contract
description: "The wire contract a browser/TypeScript client must honor when consuming a Wolverine.HTTP + System.Text.Json API: JSON body required on every command POST (even empty records), CreationResponse Location-header id extraction, STJ web-default JSON shapes, ProblemDetails rejection contract with domain reason codes, []-never-404 collection reads, and the no-auto-retry rule for server-generated-identity mutations. Use when writing or reviewing frontend code that calls the API, or when designing an endpoint a frontend will consume."
cluster: wolverine
tags: [wolverine, http, frontend, wire-contract, json, problemdetails]
---

# Consuming a Wolverine.HTTP API from a Frontend Client

> The request/response contract between a TypeScript SPA and a Wolverine.HTTP +
> System.Text.Json backend — every rule below was discovered (or its absence shipped a bug)
> during M8. Dual audience: frontend agents writing fetch code, **and** backend authors
> designing endpoints a frontend will consume.
>
> Generic endpoint mechanics live in ai-skills `wolverine-http-fundamentals` /
> `wolverine-http-marten-integration`; **this skill documents the contract as seen from the
> browser.** Each rule is tagged **[framework]** (Wolverine/STJ behavior — carries to any
> Critter Stack project) or **[convention]** (a CritterBids choice worth copying, but verify
> per project).

## When to apply this skill

Use this skill when:

- Writing or reviewing any frontend code that calls `/api/*` (fetch wrappers, TanStack Query
  functions, mutations).
- Designing or changing a Wolverine.HTTP endpoint that a SPA consumes — the frontend half of
  the contract is decided here too.
- Debugging a request that works in mocked tests but 400s/404s against the live host.

Do NOT use this skill for: SignalR push surfaces (client: `.claude/skills/signalr/SKILL.md`;
server: `wolverine-signalr`), or generic endpoint authoring (ai-skills
`wolverine-http-fundamentals`).

## Read upstream first

1. `wolverine-http-fundamentals` — endpoint classes, parameter binding, ProblemDetails flow.
2. `wolverine-http-marten-integration` — `CreationResponse`, aggregate endpoints.

Those cover the server side. This skill picks up at what the browser must send and expect.

## 1. Every command POST needs a JSON body — even an empty-record command **[framework]**

Wolverine.HTTP binds the command from the request body. A body-less POST — including for a
command record with **zero properties** — fails with **400 "Invalid JSON format"** before any
handler runs. The minimal valid request:

```typescript
await fetch("/api/participants/session", {
  method: "POST",
  headers: { "Content-Type": "application/json", Accept: "application/json" },
  body: "{}",   // an empty-record command still needs a JSON body
});
```

This shipped as a real bug in M8-S2: the session POST sent no body, every mocked-fetch test
stayed green, and the first live request 400'd. Mocked tests verify *response handling*, not
*request shape* — see `.claude/skills/frontend-slice-discipline/SKILL.md` § live smoke.

## 2. `CreationResponse<T>` delivers the id via the `Location` header **[framework]**

A creation endpoint returns **201** with `Location: /api/<resource>/{id}` and a camelCase body
`{"value": "<id>", "url": "/api/<resource>/{id}"}`. Read the header first (robust to body
serialization details), fall back to the body's `value`:

```typescript
function extractId(location: string | null, body: unknown): string | null {
  const segment = location?.split("/").filter(Boolean).pop();
  if (segment) return segment;
  if (body && typeof body === "object" && "value" in body) {
    const value = (body as { value: unknown }).value;
    if (typeof value === "string") return value;
  }
  return null;
}
```

Lived reference: `client/bidder/src/session/SessionContext.tsx`.

## 3. JSON wire shapes — System.Text.Json web defaults **[framework, verified live]**

| .NET type | On the wire | Client handling |
|---|---|---|
| property names | **camelCase** | schemas use camelCase keys |
| `decimal` | JSON number | `z.number()` — no string-money parsing |
| `Guid` | string | `z.string()` — don't pin a UUID format (see below) |
| `DateTimeOffset` | ISO-8601 string | `z.string()`, parse at render time |
| `TimeSpan` | `"00:05:00"` string | string, not a number of seconds |
| enums | string name (e.g. `"Open"`) | `z.string()` / literal union |

Two M8 findings worth keeping:

- **Don't over-pin server-issued formats.** Zod 4 moved `.uuid()` off `z.string()`; since the
  boundary's job is catching *shape/type drift* (not re-validating ids the server minted),
  `z.string()` is correct for Guid fields and stable across Zod versions.
- **camelCase was confirmed live, not assumed** — one live probe of any endpoint confirms the
  serializer posture for all of them (they share the host's STJ options).

## 4. Rejections are ProblemDetails carrying a domain `reason` code **[convention]**

Command endpoints reject with a ProblemDetails body extended with machine-readable fields —
CritterBids' bid endpoint adds `reason` (e.g. `BelowMinimumBid`, `SellerCannotBid`) and
`currentHighBid` (the authoritative value the client reconciles a rolled-back optimistic
update against). The client contract:

- Parse the ProblemDetails with a tolerant schema (`safeParse` — a 5xx may have no body).
- Map `reason` → human copy in **one** switch, with a default branch: any non-2xx without a
  recognized reason (including a 5xx from a concurrency exception) becomes a generic
  "something changed, try again" — **always a rollback + retry prompt, never a silent failure**.
- Carry `reason`, `status`, and the reconciliation fields on a typed error
  (`BidRejectedError`) so the mutation's `onError` can both roll back and surface copy.

Lived reference: `client/bidder/src/bidding/placeBid.ts`. Backend authors: when a frontend
needs to *act* on a rejection (rollback, reconcile), put the data on the ProblemDetails — do
not make the client parse prose from `detail`.

## 5. Collection reads return `[]`, never 404; single-resource GETs 404 **[convention]**

- **List endpoints** (`GET /api/listings`, all seven `/api/operations/*` boards) return
  `IReadOnlyList<T>` — an **empty array** when nothing exists, never 404. An empty board is a
  state to render, not an error to catch.
- **Single-resource GETs** (`GET /api/listings/{id}`) **404 on an unknown id**. Throw a
  distinct not-found error so the route renders a real not-found state, and **don't retry a
  404** — a missing resource is a stable answer, not a transient failure:

```typescript
retry: (failureCount, error) =>
  error instanceof ListingNotFoundError ? false : failureCount < 2,
```

Lived reference: `client/bidder/src/catalog/queries.ts`.

## 6. Never auto-retry a mutation whose identity the server generates **[convention, load-bearing]**

`POST /api/auctions/bids` generates the `BidId` server-side. A dropped/lost response is
indistinguishable from a failure — and a blind retry could **double-bid**. The rule:

- The mutation sets `retry: false` (TanStack Query retries are for idempotent reads).
- A lost response surfaces as a rollback; the **user** re-submits deliberately.
- If at-least-once submission is ever genuinely needed, that is a **backend idempotency-key
  slice to escalate** — never a client-side retry loop.

Lived reference: `client/bidder/src/bidding/usePlaceBid.ts` (M8-S3a flag, resolved
frontend-only at S3b).

## 7. Staff-gated surfaces: `X-Staff-Token` header, 401 contract **[convention — ADR 024]**

Staff HTTP requests carry the token in the **`X-Staff-Token` header** (never a query string on
HTTP paths). Any 401 means "clear the held token and re-prompt" — and because the single
shared secret makes 403 structurally unreachable, don't build a 403 branch. The hub credential
is a different transport (`access_token` on the WebSocket upgrade) — see
`.claude/skills/signalr/SKILL.md` § Per-hub auth. Lived reference:
`client/ops/src/auth/staffApi.ts` (`createStaffFetch`, validate-before-store probe).

## Common pitfalls

- **Body-less command POST** → 400 "Invalid JSON format" only against the live host; mocks
  stay green. Send `"{}"` + `Content-Type: application/json` at minimum.
- **Reading the created id only from the response body** → brittle to serialization details;
  the `Location` header is the contract.
- **`z.string().uuid()`-style format pinning on server-issued ids** → breaks across Zod
  versions for zero drift-detection value.
- **Treating an empty collection as an error** (or a 404) → it's `[]` and a renderable state.
- **Retrying a 404** on a single-resource read → a missing resource is stable; burn no
  retries.
- **Auto-retrying a server-generated-identity mutation** → possible double-submit; rollback +
  deliberate re-submit instead.
- **Parsing rejection prose instead of the `reason` code** → the extension fields are the
  contract; `detail` is for humans.
- **Hardcoding the API origin** → same-origin relative `/api/...` paths; the Vite dev proxy
  (ADR 025) owns dev reachability.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install
via `npx skills add`:

- `wolverine-http-fundamentals` — endpoint classes, binding, OpenAPI, ProblemDetails flow.
- `wolverine-http-marten-integration` — `CreationResponse`, aggregate endpoint shapes.
- `wolverine-handlers-railway-programming` — the server-side ProblemDetails discipline §4
  consumes.

**Prerequisites:**

- `wolverine-message-handlers` — the handler shapes behind these endpoints.

**Downstream:**

- `.claude/skills/frontend-slice-discipline/SKILL.md` — the working discipline (lived-contract
  first, live smoke) that catches violations of this contract before they ship.
- `.claude/skills/signalr/SKILL.md` — the push half of the client wire surface.

**External:**

- ADR-013 (Zod at the wire boundary), ADR-024 (staff auth transports), ADR-025 (dev proxy /
  same-origin) in [`docs/decisions/`](../../decisions/).
- Lived client references: `client/bidder/src/session/SessionContext.tsx`,
  `client/bidder/src/bidding/placeBid.ts`, `client/bidder/src/catalog/queries.ts`,
  `client/ops/src/auth/staffApi.ts`.
