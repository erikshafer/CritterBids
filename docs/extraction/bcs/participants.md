# Participants BC

**Maturity:** Partial.

**Evidence for the call:** `src/CritterBids.Participants` exists with the `Participant` aggregate (`Participant.cs`) and two feature folders, `StartParticipantSession` and `RegisterAsSeller`. Both flows have tests (`tests/CritterBids.Participants.Tests/StartParticipantSession/StartParticipantSessionTests.cs`, `RegisterAsSeller/RegisterAsSellerTests.cs`). The module is registered in `src/CritterBids.Api/Program.cs` line 198. However:

- The `SellerProfile` aggregate declared in `docs/vision/bounded-contexts.md` line 18 does not exist as a separate type; the seller-registered flag is folded into the `Participant` aggregate as a single `bool IsRegisteredSeller` (`Participant.cs` lines 14–16, 24–27).
- The `ParticipantSessionEnded` event declared in `docs/vision/bounded-contexts.md` line 29 and `docs/vision/domain-events.md` line 17 has no type in `src/CritterBids.Contracts/Participants/` and no emitter anywhere in `src/`. There is no session-end command, handler, or expiry path.
- The vision-doc claim that "Bidder IDs are the participant's identifier across all BCs" (line 25) is contradicted by code: cross-BC consumers (Settlement `BidderCreditView`, Auctions `ParticipantCreditCeiling`) key on `ParticipantId` (`Guid`), not `BidderId` (`string`). The `BidderId` string is carried for display only — see `ParticipantSessionStarted` field rationale (`src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs` lines 33–37).

## Business purpose

Manages anonymous participant sessions and the one-time seller-registration gate. A participant is created the moment they hit the platform; no email or account is required. The session carries a generated display name, a generated bidder id, and a hidden credit ceiling that is the participant's effective bidding cap. Seller registration is a separate flag on the same aggregate — a participant must register before creating listings.

## Project layout

Mixed layout. The aggregate and module registration are flat (`Participant.cs`, `ParticipantsModule.cs`, `ParticipantsConstants.cs`). Feature commands and their internal domain events are under `Features/<feature>/` (`Features/StartParticipantSession/`, `Features/RegisterAsSeller/`). This differs from Selling, Settlement, and Auctions, which are flat.

## Aggregates

| Aggregate | File | Stream lifecycle |
|---|---|---|
| `Participant` | `Participant.cs` | Stream starts on `ParticipantSessionStarted` (UUID v7 generated in `StartParticipantSessionHandler.Handle`, `Features/StartParticipantSession/StartParticipantSession.cs` line 47). Stream is appended to once more on `SellerRegistered` if the participant registers. No terminal event in code. |

## Domain events (internal)

| Event | File | Notes |
|---|---|---|
| `SellerRegistered` | `Features/RegisterAsSeller/SellerRegistered.cs` | Internal to the Participants BC; distinct from the integration event `SellerRegistrationCompleted`. The XML doc on the type (line 4–7) explicitly notes the duplication is to maintain BC boundary separation. |

## Commands and handlers

| Command | File | Endpoint | Result |
|---|---|---|---|
| `StartParticipantSession` | `Features/StartParticipantSession/StartParticipantSession.cs` | `POST /api/participants/session`, `[AllowAnonymous]` | Generates UUID v7 stream id, derives display name (adjective × animal × 1–9999), bidder id ("Bidder N"), and a random credit ceiling (200–1000, 100-step). Returns `CreationResponse<Guid>` and starts the stream with `ParticipantSessionStarted`. |
| `RegisterAsSeller(ParticipantId)` | `Features/RegisterAsSeller/RegisterAsSeller.cs` | `POST /api/participants/{id}/register-seller`, `[AllowAnonymous]`, `[WriteAggregate]` | `Before` rejects when `!HasActiveSession` (400) or `IsRegisteredSeller` (409). On success, appends `SellerRegistered` and emits `SellerRegistrationCompleted` via `OutgoingMessages` (transactional outbox). Returns 200 OK. |

## Integration events (out)

| Event | Contract file | Trigger |
|---|---|---|
| `ParticipantSessionStarted` | `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs` | Emitted from the aggregate's domain event stream via Marten event forwarding (`Program.cs` line 193 sets `UseFastEventForwarding = true`). Carries `ParticipantId`, `DisplayName`, `BidderId`, `CreditCeiling`, `StartedAt`. Consumed by Settlement (`BidderCreditViewHandler`) and Auctions (`ParticipantCreditCeilingHandler`). |
| `SellerRegistrationCompleted` | `src/CritterBids.Contracts/SellerRegistrationCompleted.cs` (flat — not nested under `Participants/`) | Emitted via `OutgoingMessages` in `RegisterAsSellerHandler.Handle` (`Features/RegisterAsSeller/RegisterAsSeller.cs` line 61). Consumed by Selling's `SellerRegistrationCompletedHandler` to build the `RegisteredSellers` projection. |
| `ParticipantSessionEnded` | **Not present.** Declared in `docs/vision/bounded-contexts.md` line 29 and `domain-events.md` line 17. No type in `Contracts`; no emitter in `src/`. Recorded as drift. |

## Integration events (in)

None.

## Projections

None inside the Participants BC. The `Participant` aggregate itself is a Marten event-sourced aggregate; there is no separate read model. The `RegisteredSellers` projection mentioned in the Selling vision section is owned by the Selling BC.

## Storage

PostgreSQL via Marten. Registered via `services.ConfigureMarten()` inside `AddParticipantsModule()` (`ParticipantsModule.cs` lines 15–19), which only registers the two event types `ParticipantSessionStarted` and `SellerRegistered` (`UseMandatoryStreamTypeDeclaration = true` is set globally in `Program.cs` line 188).

## Identity strategy

UUID v7 for new participant streams. `Guid.CreateVersion7()` in `StartParticipantSessionHandler.Handle` (line 47). The comment at line 44–46 cites ADR 007: no natural business key for anonymous participants, so UUID v5 determinism does not apply. `ParticipantsConstants.ParticipantsNamespace` (`ParticipantsConstants.cs` line 8) is a UUID v5 namespace held for future deterministic stream IDs; no current code uses it.

## Test-evidenced behaviors

From `tests/CritterBids.Participants.Tests/`:

- **`StartParticipantSession`** — starting a session from an empty stream produces `ParticipantSessionStarted`; starting a second session produces a different display name from any active session (`StartParticipantSession/StartParticipantSessionTests.cs`).
- **`RegisterAsSeller`** — registering with an active session produces `SellerRegistrationCompleted`; without an active session is rejected; when already registered is rejected idempotently (`RegisterAsSeller/RegisterAsSellerTests.cs`).
- Fixtures: `Fixtures/ParticipantsTestFixture.cs` provides `CleanAllMartenDataAsync()` between tests.

## Open questions

- The vision doc's "Bidder IDs are the participant's identifier across all BCs" (line 25) is inverted by code (cross-BC keys are `ParticipantId`, the `Guid`). Captured as drift; not an open question in itself.
- `ParticipantSessionEnded` is declared but absent. Captured as declared-but-not-built; no observable behavior contradicts the absence.
