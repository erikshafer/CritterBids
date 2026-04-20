# gRPC Opportunities Research — CritterBids

**Status:** Draft — Research Phase
**Owner:** Erik Shafer
**Last Updated:** 2026-04-19
**Suggested repo location:** `docs/research/grpc-opportunities-research.md`

---

## 1. Purpose & Position

This document captures a research effort to identify where **gRPC** — a contract-centric, HTTP/2-based, high-performance service-to-service transport — could plausibly earn a place in CritterBids. It is explicitly a **research document**, not a commitment. Decisions that emerge from it will be promoted to Architecture Decision Records (ADRs) through the normal milestone process.

The document is written in the shadow of three facts that shape every section:

1. **CritterBids is event-driven and event-sourced by design.** Marten (and later Polecat) stores every state change as an immutable event on disk. RabbitMQ persists integration messages through the Wolverine outbox. The durability floor is non-negotiable — every domain fact survives a process restart.
2. **CritterBids is a modular monolith.** All eight bounded contexts run in-process inside `CritterBids.Api`. Intra- and inter-BC communication is Wolverine, not HTTP, not gRPC. Introducing a network hop between BCs that currently share a process is a regression, not a feature, until or unless a BC is deliberately extracted.
3. **Wolverine is gaining first-class gRPC handler support.** Erik landed the first iteration of gRPC handlers in Wolverine during the week this document was written; release is imminent. The premise of this research is that "Wolverine over gRPC" becomes a real option in the near term, in the same spirit that "Wolverine over RabbitMQ" or "Wolverine over Azure Service Bus" is today.

The guiding question is therefore not *"should CritterBids adopt gRPC?"* — that framing invites a premature answer. The guiding question is **"given our architecture, where would gRPC earn its cost, and where would it be ceremony?"**

---

## 2. Project Context

The relevant CritterBids facts that constrain any gRPC decision:

- **Single deployable host.** `src/CritterBids.Api` wires all eight BC modules together. All cross-BC traffic is already in-process via Wolverine — there is no network hop to optimize.
- **Durability floor.** Marten event streams, the Wolverine outbox on top of Marten, and RabbitMQ persistent queues ensure that every command and integration event is recorded to disk *before* any downstream handler runs. A transport that bypasses the outbox is a regression.
- **Real-time fan-out lives in Relay.** `BiddingHub` and `OperationsHub` push to browsers via SignalR. SignalR is load-bearing, not a flourish. See `docs/vision/bounded-contexts.md` and the frontend stack research doc for context.
- **Two frontends, both SPAs.** `critterbids-web` (participant) and `critterbids-ops` (staff) are static Vite builds served through a reverse proxy alongside the .NET host. Neither has a server runtime of its own.
- **Post-MVP swap milestones are already planned.** `M-transport-swap` (RabbitMQ → Azure Service Bus) and `M-storage-swap` (Marten → Polecat on one BC) are intentional demo moments for the Critter Stack's infrastructure-agnostic programming model. gRPC, if adopted, should slot into the same "swap" narrative rather than compete with it.
- **Hetzner single-VPS deployment.** No Kubernetes, no service mesh, no Envoy sidecar per process. Anything requiring a gRPC-Web proxy needs to coexist with Caddy or Nginx on the same box.

---

## 3. Scope

### 3.1 In Scope

- What gRPC is good at, and the industry patterns where it is a clear win.
- Places in the CritterBids architecture where gRPC could plausibly earn its keep.
- The durability question: how Wolverine-over-gRPC interacts with the outbox, and where it does not.
- gRPC-Web as a potential frontend transport vs SignalR (WebSockets / SSE).
- Post-MVP demo opportunities where gRPC becomes a teaching moment.
- Anti-patterns — where adopting gRPC would be ceremony.

### 3.2 Out of Scope (for this document)

- Concrete `.proto` schemas for any specific BC. Contracts remain in `CritterBids.Contracts` as C# records; `.proto` generation, if adopted, is a downstream concern.
- Migration plans for existing RabbitMQ transport to gRPC. The research position is that they complement each other; a wholesale swap is not on the table.
- Authentication and authorization over gRPC channels beyond a passing note. The Participants BC auth story is upstream.
- Load testing, benchmarks, or performance measurements. Any performance claim here is drawn from published industry reports, not from CritterBids experiments.
- Kubernetes, service mesh, or sidecar deployment topologies. CritterBids is a single-VPS deployment.

---

## 4. Constraints & Guiding Principles

1. **Durability first.** A command or integration event must reach disk before downstream handlers run. A transport that does not integrate with the Wolverine outbox is disqualified for domain traffic.
2. **Additive, not replacement.** gRPC is evaluated as an *additional* transport, not as a substitute for RabbitMQ, SignalR, or HTTP. The Critter Stack's transport-agnostic posture is load-bearing for the demo narrative.
3. **Modular monolith discipline is preserved.** Introducing gRPC must not puncture BC boundaries or invite cross-BC point-to-point calls that bypass the shared contracts package.
4. **LLM-friendly by default.** Prefer patterns that agents and human contributors can write without arcane protoc toolchain knowledge in the default path.
5. **Demo-first tie-breaker.** When two approaches are close on technical merit, pick the one that tells a better story from a conference stage. CritterBids exists to be demonstrated.
6. **No premature adoption.** If gRPC does not earn its presence in a milestone of real work, it is not in the stack. Research is not commitment.

---

## 5. What gRPC Is and Isn't

This section grounds the rest of the document. Skip if you are already fluent.

### 5.1 The Shape of gRPC

gRPC is a **contract-first, binary, HTTP/2-based RPC framework** originally released by Google in 2016 and now a CNCF graduated project. Its defining features:

- **Protocol Buffers (`.proto`) as the contract language.** A `.proto` file declares messages and service methods; a code generator produces strongly typed clients and server stubs in C#, Go, Rust, Python, TypeScript, and a dozen other languages. This is the "contract-centric" positioning — the `.proto` file is the source of truth, not an OpenAPI spec that drifts from the implementation.
- **Four method shapes.** Unary (one request, one response), server-streaming, client-streaming, and bidirectional streaming. The last three all ride HTTP/2 streams and allow long-lived data flow with backpressure.
- **HTTP/2 multiplexing.** Multiple concurrent RPCs share a single TCP connection, with header compression (HPACK), flow control, and cancellation propagation built in.
- **Deadlines, retries, and cancellation as first-class concerns.** Context propagation across call boundaries is idiomatic.
- **Binary encoding.** Protobuf is more compact than JSON and significantly faster to parse in most languages. Published benchmarks routinely report 2×–10× throughput improvements over REST+JSON for small-payload, high-frequency internal service traffic.

### 5.2 What gRPC Is Not

- **Not a message broker.** There is no queue, no durable acknowledgment, no retention, no subscriber group. If the receiver is down, the call fails. The caller owns retry policy.
- **Not a pub/sub system.** A gRPC stream is point-to-point between two endpoints. Fan-out to many consumers is the caller's problem.
- **Not inherently durable.** The wire is in-flight only. Any durability guarantee has to come from the application layer (a sending outbox, a receiving idempotency key store).
- **Not a first-class browser transport.** Browsers do not speak gRPC natively. gRPC-Web is a constrained subset that requires a proxy — more on this in §9.
- **Not a replacement for SignalR.** SignalR's WebSocket/SSE/long-polling negotiation, automatic reconnection, and backplane abstractions solve problems gRPC does not address on its own.

### 5.3 How It Compares, Briefly

| Concern | gRPC | REST+JSON | RabbitMQ | SignalR |
|---|---|---|---|---|
| Typical shape | Strongly typed RPC | Resource-oriented request/response | Asynchronous messages with durable queue | Server-push streams with transport negotiation |
| Contract language | `.proto` + codegen | OpenAPI (optional) | Application-defined | Method names + payloads |
| Durability | None (in-flight only) | None | Persistent queues + ack semantics | Lost if server down during send |
| Streaming | First-class (4 modes) | Via SSE or chunked transfer | Native (consumer-paced) | First-class (WebSocket/SSE) |
| Browser native | No (needs gRPC-Web proxy) | Yes | No | Yes (via client library) |
| Typical use | Internal service-to-service | External APIs, browser APIs | Inter-service events | Real-time push to browser |

---

## 6. Industry Patterns — Where gRPC Thrives

The observed pattern across companies that adopted gRPC at scale: **gRPC wins where an internal, trusted, high-frequency, synchronous request/response path exists between services whose contracts move together.** It does not win where asynchrony, durability, or fan-out is the shape of the problem.

### 6.1 Canonical Adoption Stories

- **Google.** Invented gRPC as the open-source successor to the internal Stubby framework. Google's internal service graph has tens of thousands of services; gRPC is the default internal RPC transport.
- **Netflix.** Migrated much of its internal microservice graph from HTTP/JSON to gRPC. Their published experience reports focus on reduced p99 latency, stricter contract enforcement, and easier polyglot development.
- **Uber.** Published extensive engineering writeups on moving their fraud, pricing, and dispatch services to gRPC. The consistent theme: synchronous, read-heavy, latency-sensitive service-to-service traffic.
- **Square.** Uses gRPC for internal service-to-service calls where payment correctness requires tight, versioned contracts.
- **Dropbox.** Migrated significant Python services to gRPC; their reports emphasize codegen discipline and reduced client churn.
- **Slack.** Uses gRPC internally for service-to-service calls, particularly in the core messaging fabric.
- **Cloudflare, Lyft, Pinterest, Spotify.** All have production gRPC service graphs at varying scales.

### 6.2 Infrastructure-Layer Adoption

Where gRPC is genuinely everywhere — often invisibly:

- **Kubernetes.** `kubelet` → API server, the container runtime interface (CRI), the container storage interface (CSI), and `etcd` all speak gRPC.
- **Observability.** OpenTelemetry's OTLP protocol is gRPC-first (with an HTTP+protobuf fallback). Most modern collectors and exporters default to gRPC.
- **Distributed databases.** CockroachDB, YugabyteDB, TiDB, and FoundationDB all expose or use gRPC for intra-cluster communication.
- **Service meshes.** Envoy's xDS control plane APIs are gRPC. Istio, Linkerd, and Consul Connect all depend on gRPC internally.
- **AI / ML serving.** TensorFlow Serving, Triton Inference Server, and BentoML all ship gRPC endpoints as the high-performance alternative to their REST APIs.

### 6.3 The Common Thread

In every adoption story, gRPC lives between **two services that already want to talk synchronously and whose teams can move contracts together.** The wins are typed contracts, lower latency, richer streaming semantics, and a universal framework for deadline and cancellation propagation.

The cases where adopters later regretted gRPC — or pulled back — share a pattern too: they tried to use it as a backbone for *asynchronous, decoupled, fan-out* traffic and ended up reinventing what a broker already provides.

---

## 7. Research Findings — Where gRPC Could Fit in CritterBids

Each sub-section evaluates a specific seam and gives a provisional verdict. Verdicts are consolidated in §11.

### 7.1 Intra-BC Command and Query Handling

Intra-BC traffic is in-process, handled by Wolverine's mediator. A gRPC transport here would require serializing across loopback to talk to the same process. This is pure ceremony.

**Verdict: No.** Wolverine in-process handlers remain the canonical path.

### 7.2 Inter-BC Integration Events Within the Monolith

Inter-BC integration events are the contract boundary between modules. Today these travel via Wolverine using either in-process routing or RabbitMQ (with the outbox on top of Marten). Introducing gRPC here would either (a) bypass the outbox, violating the durability floor, or (b) wrap RabbitMQ semantics in a gRPC envelope, which is worse on both counts.

**Verdict: No.** Integration events stay on RabbitMQ (and Wolverine in-process when appropriate). The durability and pub/sub fan-out shape of the problem is brokered, not RPC.

### 7.3 BC-to-BC Queries Within the Monolith

A BC occasionally needs to *ask* another BC for a read-side answer. Today this is rare in CritterBids — projections generally duplicate the data each BC needs, which is the point of the `docs/vision/domain-events.md` vocabulary. But the pattern does come up (e.g., Settlement asking Participants for a bidder's credit ceiling at settlement time).

Wolverine's request/reply pattern covers this in-process. gRPC adds nothing when the counterparty is a class library reference away.

**Verdict: No** for the current monolithic deployment. See §7.5 for the extracted-BC variant.

### 7.4 Synchronous External Integrations (Payment, Carrier Tracking)

Settlement will eventually integrate with a real payment processor. Obligations will eventually integrate with carrier webhooks. These are external, third-party surfaces.

The pragmatic reality: payment processors (Stripe, Square, Adyen) and carrier APIs (UPS, FedEx, USPS) overwhelmingly expose **REST+JSON** or **webhooks**, not gRPC. Some newer fintech infrastructure (e.g., some stablecoin rails, some card-not-present risk services) ships gRPC endpoints, but this is the minority.

For CritterBids, an external integration seam should be modeled in the BC that owns it (Settlement, Obligations) with an **anti-corruption layer** that adapts the external API's shape to a domain-native command or event. Whether that external API is REST or gRPC is an implementation detail behind the seam.

**Verdict: Agnostic.** Use whatever the external party offers. CritterBids does not benefit from forcing gRPC here.

### 7.5 A Post-Extraction Deployable — The "Relay as Edge Worker" Story

The single most compelling near-term gRPC opportunity is a **deliberately extracted Relay deployment.**

Today, Relay is an in-process BC that receives integration events, transforms them into SignalR push messages, and fans them out to connected browsers. In a production scenario where CritterBids wanted to:

- Run SignalR hubs at the network edge (closer to a room full of conference attendees) while keeping the authoritative Auctions BC in a back-office data center,
- Horizontally scale the real-time tier independently of the auction-core tier,
- Demonstrate Wolverine's transport-agnostic model in a way that is fundamentally distributed and not just "swap RabbitMQ for ASB,"

... then splitting Relay (or a subset of it) out of the monolith and wiring it to the core over **Wolverine-over-gRPC** becomes a genuinely interesting architecture.

The shape:

- The core host (`CritterBids.Api`) emits domain events and writes them to the Wolverine outbox as today.
- An outbound gRPC transport streams those events to one or more edge Relay workers.
- Edge workers maintain SignalR hubs for locally connected browsers and perform the fan-out there.
- The gRPC channel is Wolverine-managed, so retry, ordering, and outbox discipline are preserved.

**Verdict: Strong candidate for a post-MVP demo milestone.** This pairs well with the existing transport-swap and storage-swap milestones and adds a "topology swap" dimension the other two do not cover.

### 7.6 Admin and Operations Tooling

CritterBids will eventually want operator tooling beyond the browser-based `critterbids-ops` dashboard. Examples include:

- A CLI for demo reset, session creation, and flagged-session review.
- Automation hooks for CI/CD, release engineering, or on-call diagnostics.
- A scripted "prime the room" workflow that a conference presenter runs from a laptop before a session starts.

gRPC is an excellent fit for internal CLI-to-service communication. The contracts are strongly typed, the tool is operator-authored so protoc ceremony is acceptable, and the same `.proto` can generate a Go CLI, a PowerShell module, and a C# client interchangeably.

This is the classic "kubectl analog" pattern: the server exposes a well-defined admin API; the tool is a small client. The admin API is intentionally distinct from the user-facing HTTP/SignalR surface, which keeps staff concerns out of the bidder contracts.

**Verdict: Good candidate, low priority.** Worth revisiting once Operations BC has defined its full staff-command surface.

### 7.7 Cross-Cluster / Multi-Region Replication (Hypothetical)

If CritterBids ever ran in more than one region (unlikely for a showcase but worth naming), a gRPC-based event replication channel between regional Wolverine hosts is an obvious pattern. This is effectively a more general form of the §7.5 Relay extraction story.

**Verdict: Not relevant to MVP or near-term post-MVP. Named for completeness.**

### 7.8 Storage-Swap Demo Topology

The planned `M-storage-swap` milestone migrates one BC's event store between Marten/PostgreSQL and Polecat/SQL Server. The migration itself is an in-process configuration change per ADR 011's reversal path.

A gRPC-mediated variant — where the "old" and "new" stores live behind two separate service hosts that speak to a dispatcher over Wolverine-over-gRPC — is more elaborate than the milestone requires, but it is a coherent stretch-stretch goal for a conference keynote.

**Verdict: Not required. Possible elaboration if the storage-swap talk wants a bigger visual.**

### 7.9 Conference Demo Vehicle — gRPC as a Teaching Moment

CritterBids is a teaching artifact. Every architectural choice has a "what does this demonstrate on stage?" dimension. gRPC has a specific story to tell:

- **"The same Wolverine handler runs over an in-memory bus, over RabbitMQ, over Azure Service Bus, and over gRPC."** This is the Wolverine transport-agnostic story given a new dimension.
- **"Events are persisted to the outbox before they cross the wire, whether that wire is a queue or a stream."** This is the durability story made concrete.
- **"Extract a BC, point at it over gRPC, and the application code does not change."** This is the modular monolith story with teeth — the claim that BCs can become services is demonstrable, not just asserted.

The Relay-as-edge-worker story (§7.5) is the cleanest way to dramatize all three on stage in a single demo.

**Verdict: gRPC's strongest argument in CritterBids is pedagogical, not performance.**

### 7.10 Event Sourcing and gRPC

One question worth naming explicitly: does gRPC belong *inside* the event-sourced write path?

It does not. The write path is:

```
Command → Aggregate.Decide() → Events → Marten session → stream append + outbox
```

Everything in that chain is in-process against a single Marten session. A gRPC hop anywhere on the write path either splits a single transactional unit across machines (breaking the atomicity of event append + outbox insert) or serializes identical in-process work over loopback. Neither is a good idea.

gRPC appears *after* the outbox has flushed, as a transport for durable, already-persisted envelopes to travel to their destination. That is the Wolverine-over-gRPC position.

**Verdict: The event-sourced write path stays in-process. gRPC is for what happens *after* the outbox.**

---

## 8. The Durability Question — How Wolverine-over-gRPC Protects the Invariant

This is important enough to call out as its own section because the user explicitly flagged it.

The durability floor states: **every domain fact must be on disk before any handler acts on it.** In the current architecture this is achieved by:

1. Marten appends events to a stream and the Wolverine outbox within a single transaction.
2. Wolverine's outbox agent flushes the outbox to RabbitMQ asynchronously.
3. Consumers acknowledge delivery only after they have written their own downstream side effects (including, if they produce events, their own outbox rows).

If gRPC replaces the RabbitMQ step — as it could for a specific point-to-point hop — the following must hold:

- **Sender side:** The outbox must still be written transactionally with the originating aggregate append. Wolverine's gRPC transport must dequeue from the outbox, not emit directly from the handler. The "same envelope, different wire" posture means the gRPC transport is a sink for outbox flushing, equivalent to the RabbitMQ sender.
- **Receiver side:** The receiver writes incoming envelopes to its own inbox (or a `DurableReceiver`-equivalent) before invoking the handler, so a crash between ack and handler execution does not lose the message.
- **Ordering and idempotency:** gRPC streams provide per-stream ordering, but a crash-and-reconnect sequence can replay. The receiver needs an idempotency key store, just as it does for RabbitMQ redeliveries.

If any of these three is missing, the gRPC transport is a regression from RabbitMQ. The premise of this research assumes Wolverine's new gRPC handler surface integrates the outbox correctly, because Erik is the one building it; the exact semantics should be confirmed against the release notes when they land.

**Where gRPC is legitimately non-durable:** admin RPC calls (§7.6), health checks, live queries against projections (where the answer is derived from already-durable state), and diagnostic streams. None of these carry domain facts that must survive a crash.

---

## 9. gRPC-Web vs SignalR — The Frontend Question

The user specifically asked whether gRPC-Web could replace SignalR for CritterBids' real-time frontend. Short answer: **no, SignalR remains the right choice.** The longer answer is worth writing down because it is not obvious.

### 9.1 What gRPC-Web Is

Browsers cannot speak native gRPC. The wire protocol uses HTTP/2 trailers and other semantics that are not fully exposed through browser APIs (fetch, XHR). gRPC-Web is the workaround: a protocol variant that reframes gRPC messages into a format a browser can emit over HTTP/1.1 or HTTP/2, and a **proxy** (typically Envoy, though Connect-Go, gRPC-Web proxy, and some .NET middleware options exist) that translates between gRPC-Web on the browser side and native gRPC on the backend side.

Connect (from Buf Technologies) is the most polished modern variant. Connect-Web, gRPC-Web, and native gRPC all share the same `.proto` file; a single contract targets three wires with different transport capabilities.

### 9.2 What gRPC-Web Supports

- Unary calls (request/response). ✅
- Server-streaming (one request, many responses back). ✅
- Client-streaming and bidirectional streaming. ❌ **Not supported.** This is the critical limitation.

Flash auctions involve clients *sending* bids as fast as the auction allows, while simultaneously *receiving* an updated bid feed. That is bidirectional traffic by definition. gRPC-Web can model it only by combining separate unary calls (for bid placement) with a server stream (for the feed), which works but is not appreciably simpler than the SignalR model.

### 9.3 What SignalR Provides That gRPC-Web Does Not

- **Transport negotiation.** SignalR client connects via WebSockets when available, falls back to Server-Sent Events, then long polling. Conference Wi-Fi — see §7.5 of the frontend stack research doc — is hostile to WebSockets, and this fallback is not theoretical.
- **Automatic reconnection with configurable backoff.** gRPC-Web clients must implement this per-stream.
- **Backplane abstraction.** SignalR's Redis backplane lets multiple .NET instances synchronize broadcasts transparently. gRPC-Web has no equivalent built in; fan-out across multiple servers is the application's problem.
- **Idiomatic .NET story.** `@microsoft/signalr` on the client, `Hub` on the server, strongly-typed hub proxies — all first-class, all in the Critter Stack's native idiom.
- **Mature LLM training coverage.** Agents write idiomatic SignalR code without specific coaching.

### 9.4 What gRPC-Web Offers in Exchange

- **Contract sharing with backend-to-backend code.** A single `.proto` for both internal service traffic and browser traffic is aesthetically appealing.
- **Binary efficiency.** Smaller payloads than JSON over SignalR. For CritterBids' bid volumes this does not matter; bids are small.
- **Uniform streaming semantics across the service graph.** Only meaningful if the service graph is large and polyglot. CritterBids' service graph is, for the foreseeable future, one process.

### 9.5 Conclusion

For the live bid feed, the ops dashboard, and any other participant- or staff-facing real-time surface, **SignalR (WebSockets with SSE fallback) is the correct choice and should remain so.** The frontend stack research already landed on this; this document reconfirms the decision under the pressure of a gRPC option being on the table.

The one narrow caveat: if CritterBids ever wanted to expose a typed *programmatic* API for external consumers (e.g., a partner wants to drive CritterBids from their own software), a gRPC or gRPC-Web API surface is a reasonable way to do it. That is a different audience than the SPA frontends.

**Verdict: SignalR stays. gRPC-Web may earn a place as a programmatic partner-facing surface later, if that audience ever materializes.**

---

## 10. Anti-Patterns — Where gRPC Would Be Ceremony

Equally important as where gRPC fits is being specific about where it does not.

- **Replacing Wolverine in-process handlers with loopback gRPC.** Pointless serialization overhead.
- **Replacing RabbitMQ for inter-BC events.** Loses the broker's fan-out, retention, and consumer-paced semantics; gains nothing.
- **Exposing the bidder-facing HTTP API as gRPC.** Browsers do not speak native gRPC, and gRPC-Web is inferior to SignalR for the real-time piece. A gRPC surface on top of what is already a clean Wolverine HTTP endpoint is duplicated work.
- **Inventing a bespoke protobuf contract layer alongside `CritterBids.Contracts`.** The contract boundary is the C# record types in `CritterBids.Contracts`. Having a second, parallel `.proto` definition of the same events invites drift. Any gRPC adoption should treat `.proto` generation as a downstream artifact of the C# contract, not a parallel source of truth.
- **Using gRPC to "make the monolith feel like microservices."** The monolith's boundaries are enforced by the project reference graph and the integration-events-only rule, not by network hops. Adding network hops for their own sake loses the "modular monolith" advantage without gaining any microservice advantage.

---

## 11. Recommended Posture (Provisional)

Consolidated from §7 and §9. All recommendations are subject to the ADR process.

| Seam | gRPC role | Confidence |
|---|---|---|
| Intra-BC command/query handling | None — Wolverine in-process | High |
| Inter-BC integration events | None — RabbitMQ over outbox | High |
| BC-to-BC queries (same process) | None — Wolverine request/reply | High |
| External payment / carrier APIs | Whatever the vendor offers; likely REST | High |
| **Extracted Relay (edge workers) over Wolverine-over-gRPC** | **Strong candidate for post-MVP milestone** | **Medium-High** |
| Operations admin CLI | Good fit, low urgency | Medium |
| Multi-region replication | Not in scope | — |
| Storage-swap demo elaboration | Possible stretch | Low |
| Real-time browser push (bidder / ops) | gRPC-Web rejected; SignalR wins | High |
| Partner-facing programmatic API | Possible gRPC surface, no concrete audience yet | Low |
| Event-sourced write path | None — stays in-process to protect outbox atomicity | High |

"Strong candidate" means the shape of the problem and gRPC's shape align, *and* the move would produce a demonstrable conference narrative. "Good fit" means gRPC is well-suited but priority is low. "Possible" means a future team could justify it but the research did not find a compelling current case.

---

## 12. Architectural Concerns

### 12.1 Contracts: One Source of Truth

The existing contract discipline is that `CritterBids.Contracts` holds C# records that cross BC boundaries. A gRPC adoption must preserve this. The practical question becomes: is `.proto` generated *from* the C# records, or are the C# records generated *from* `.proto`?

Both are mechanically possible. For CritterBids, **C# records remain primary, `.proto` is derived** when and if needed. Reasons:

- The C# surface is ergonomic for Wolverine handlers, Marten projections, and the test suite. A generated-from-`.proto` C# type loses the idiomatic feel.
- gRPC is an *additional* transport. Additional transports should adapt to the domain, not vice versa.
- Tooling like `protobuf-net` or hand-maintained `.proto` files both work; the investment is modest when contracts are stable.

### 12.2 Versioning

`.proto` has its own conventions for backward and forward compatibility (reserved field numbers, never renumber, never change types). These do not map one-to-one onto the versioning approach in ADR 005. If gRPC becomes a real transport, ADR 005 will need a companion section on protobuf versioning rules, and the generator pipeline will need to enforce them (or the test suite will).

### 12.3 Deployment Topology on Hetzner

A single-VPS deployment serving gRPC to itself adds nothing. A single-VPS deployment serving gRPC to an edge Relay worker on a *second* VPS is the minimum interesting topology. Any post-MVP Relay extraction decision is therefore also an infrastructure decision — it introduces a second node and a cross-node dependency that the current "one box, `docker compose up`" story does not.

This does not kill the idea; it just means the Relay extraction milestone needs a second Hetzner instance (or a small cluster) to be real, which is a budgetary and operational addition that should be named explicitly in the milestone scope.

### 12.4 TLS and Trust

gRPC between internal services requires TLS to be safe on anything other than loopback. Caddy's automatic TLS handling covers the external surface; internal gRPC between the core and an extracted Relay needs either mTLS (overkill for a demo-scale deployment) or a shared-secret bearer token on each call (sufficient, simpler). This is a solvable problem, but it is the kind of thing that drifts from "obvious" to "two days of work" if unplanned.

### 12.5 Observability

gRPC's tracing ergonomics are excellent — every call propagates context trivially, and OpenTelemetry's gRPC instrumentation is mature on .NET. If CritterBids ever adopts gRPC transport between processes, end-to-end distributed tracing (outbox append → gRPC send → edge worker handler → SignalR push) becomes a compelling dashboard for a conference demo. This is a pedagogical argument for §7.5 rather than a technical requirement.

---

## 13. Open Questions (Parked)

1. **Exact Wolverine-over-gRPC semantics.** The first iteration of the handler surface lands this week. Specifically: does the transport integrate with `IntegrateWithWolverine()`'s outbox wiring out of the box, or does it require additional configuration to participate in durable send/receive? Re-read the release notes and revisit.
2. **`.proto` pipeline, if adopted.** Hand-maintained, generated from C# records, or a reverse approach. Only matters if gRPC becomes a transport.
3. **Relay extraction milestone feasibility.** How much of Relay is genuinely stateless and extractable vs tightly coupled to in-process state? A short audit would resolve this.
4. **Which demo narrative wins?** Transport swap (RabbitMQ → ASB), storage swap (Marten → Polecat), and a hypothetical topology swap (monolith → monolith-plus-edge-Relay) all compete for the same conference minutes. No need to pick yet, but eventually only one or two will ship as talks.
5. **Partner-facing gRPC API.** Has anyone actually asked for one? Pure speculation today. Revisit only when a real partner appears.
6. **OpenTelemetry / OTLP relationship.** If CritterBids adopts OTLP for observability export, it will be running gRPC in the operator tier regardless of domain decisions. Worth noting; not worth deciding yet.
7. **gRPC testing ergonomics.** Wolverine's test harness is excellent for in-process handlers. How does it compose with gRPC? This is a skill-file question for whenever a concrete gRPC milestone lands.

---

## 14. Candidate ADRs

Ranked by likelihood of materializing. None of these should be written before the Wolverine gRPC release notes are in hand.

### ADR-GRPC-001: gRPC Posture and Adoption Scope

**Confidence:** High if we want to lock in the posture, Low-urgency if not.

**Core claim:** CritterBids adopts gRPC as an *additional* Wolverine transport for narrowly scoped use cases (admin tooling, post-extraction edge workers, potential partner APIs). gRPC does not replace RabbitMQ, SignalR, or HTTP on any existing seam. The event-sourced write path remains in-process; the durability floor is preserved by routing gRPC traffic through Wolverine's outbox/inbox semantics.

### ADR-GRPC-002 (defer): Extracted Relay Over Wolverine-over-gRPC

**Confidence:** Medium. Contingent on a real milestone being scoped for it.

**Core claim:** One or more Relay workers can be deployed as separate processes receiving domain events from the core over Wolverine-over-gRPC, fanning out to locally connected SignalR clients. This preserves the monolith-as-default deployment while demonstrating the Critter Stack's ability to scale and extract at BC boundaries.

### ADR-GRPC-003 (defer): Operations Admin API as gRPC

**Confidence:** Low-Medium. Contingent on operator tooling becoming a real concern.

**Core claim:** Staff-facing admin APIs (demo reset, session lifecycle commands, participant flag, dispute resolve) are exposed on a dedicated gRPC surface distinct from the participant HTTP/SignalR surface. A small CLI (Go or C#) consumes this surface.

### ADR-GRPC-004 (defer): `.proto` Generation Strategy

**Confidence:** Low. Irrelevant unless any of the above ADRs ships.

**Core claim:** `.proto` definitions, if adopted, are derived from `CritterBids.Contracts` C# records. The `.proto` files are generated artifacts, not hand-maintained, and are versioned alongside the source contracts.

---

## 15. Sequencing Plan

This research stands as input to future milestones. No immediate implementation is proposed.

### Immediate (this milestone)

- Publish this research document.
- Read the Wolverine gRPC release notes when they land (expected within the week).
- Note any semantics that invalidate §8 assumptions. If the outbox integration is not automatic, capture that as a known constraint.

### Near-term

- When `M-transport-swap` (RabbitMQ → Azure Service Bus) is scoped, consider whether gRPC deserves a named third transport step in the same demo to strengthen the "Wolverine is transport-agnostic" narrative.
- Draft ADR-GRPC-001 to formalize the posture — specifically, the explicit "not a replacement" stance — if any confusion arises about gRPC's role.

### Post-MVP

- Scope a `M-relay-extraction` milestone that moves Relay out of the monolith onto a second host over Wolverine-over-gRPC. Treat as an experimental milestone with its own retrospective.
- Evaluate Operations admin CLI as a separate small milestone if and when staff tooling becomes a real friction.

---

## 16. Glossary

- **gRPC.** A contract-first, HTTP/2-based RPC framework using Protocol Buffers for schema definition and binary encoding. Originally from Google, now CNCF.
- **Protocol Buffers (protobuf).** The schema language and binary encoding used by gRPC. `.proto` files declare messages and service methods; `protoc` generates client and server code in many languages.
- **gRPC-Web.** A protocol variant that lets browsers participate in gRPC, using a proxy to bridge browser-emittable HTTP to native gRPC. Supports unary and server-streaming; does not support client or bidirectional streaming.
- **Connect (Connect-Go, Connect-Web).** A modern alternative gRPC-compatible protocol from Buf Technologies that unifies gRPC, gRPC-Web, and its own Connect protocol under a single client/server API.
- **Outbox pattern.** A transactional pattern where outbound messages are written to a database table within the same transaction as the originating state change, then flushed to the transport asynchronously. Ensures "message sent" and "state changed" either both happen or neither does.
- **Inbox pattern.** The dual of the outbox on the receiving side — incoming messages are recorded durably before being handed to the handler, supporting idempotency and safe retry.
- **Backplane.** A shared pub/sub layer that synchronizes messages across multiple server instances. For SignalR this is typically Redis.
- **HTTP/2 trailers.** Trailing headers sent after the response body. gRPC uses them for status and metadata; browsers cannot natively emit them, which is why gRPC-Web exists.

---

## 17. References

### gRPC and Protocol Buffers

- gRPC official documentation (grpc.io): protocol specification, language guides, four streaming shapes.
- Protocol Buffers language reference (protobuf.dev): `.proto` syntax, versioning rules, wire format.
- CNCF gRPC graduation announcement (2019): positioning of gRPC as a CNCF-graduated project.
- Connect documentation (connectrpc.com): modern gRPC/gRPC-Web/Connect unified API.
- "gRPC-Web is Generally Available" (grpc.io blog, 2018): protocol description and proxy requirements.

### gRPC in .NET and Wolverine

- "gRPC services with ASP.NET Core" (learn.microsoft.com): canonical .NET server and client setup.
- `Grpc.AspNetCore` package documentation: integration with ASP.NET Core.
- Wolverine documentation (wolverine.netlify.app): transport-agnostic message handling, outbox integration, `IntegrateWithWolverine()` semantics. Check release notes for the pending gRPC handler surface.
- JasperFx CritterStackSamples (github.com/jasperfx): reference samples for transport configuration.

### Industry Adoption Reports

- Google Cloud Blog and Google AI Blog: multiple posts on internal gRPC usage as the successor to Stubby.
- Netflix Tech Blog: reports on gRPC adoption for internal microservices.
- Uber Engineering: "How We Built Uber Engineering's Highest Query per Second Service Using Go," and later posts on gRPC migration.
- Square Engineering: posts on gRPC for internal payment service communication.
- Dropbox Tech Blog: "Courier: Dropbox migration to gRPC."
- Slack Engineering: posts on internal gRPC usage.

### Event-Driven and Durability

- "Outbox, Inbox patterns and delivery guarantees explained" (event-driven.io, multiple posts by Oskar Dudycz).
- Martin Fowler, "What do you mean by 'Event-Driven'?": taxonomy distinguishing event notification, event-carried state transfer, event sourcing, and CQRS.
- Marten documentation: event store, projections, and integration with Wolverine's outbox.
- RabbitMQ documentation: persistent queues, publisher confirms, consumer acks.

### SignalR and Real-Time

- Microsoft Learn: "ASP.NET Core SignalR production hosting and scaling."
- Microsoft Learn: "Redis backplane for ASP.NET Core SignalR scale-out."
- `@microsoft/signalr` npm package documentation.
- CritterBids internal: `docs/research/frontend-stack-research.md` §5.8 and §5.9 (SignalR integration and backplane patterns).

### gRPC vs REST and Alternatives

- "gRPC vs REST: Comparing APIs Architectural Styles" (multiple sources; representative: Google Cloud Architecture Center): performance characteristics and use-case guidance.
- "When to use gRPC vs REST" (grpc.io blog and various engineering blogs): industry decision heuristics.
- "Connect: An Alternative to gRPC for Web" (buf.build): positioning of Connect relative to gRPC and gRPC-Web.

### Observability

- OpenTelemetry documentation: OTLP protocol, gRPC vs HTTP/protobuf exporters.
- OpenTelemetry .NET instrumentation: gRPC client and server instrumentation packages.
