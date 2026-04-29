# N003-FU: GET /api/participants/{id} Endpoint

**Source:** `docs/narratives/003-findings.md` Finding 002
**Narrative:** `docs/narratives/003-bidder-starts-anonymous-session.md` (joint-authority citation per AUTHORING.md rule 3)
**Milestone:** M1 follow-up (Participants BC gap surfaced post-M1; not blocking subsequent milestones)
**Slice:** 0.2-FU (slice 0.2's read-side companion; the slice 0.2 POST `StartParticipantSession` shipped in M1-S5; this follow-up adds the corresponding GET)
**Agent:** @PSA (Participants BC owner)
**Estimated scope:** ~1 PR; one new feature folder under `src/CritterBids.Participants/Features/`; one endpoint, one test class

## Goal

Add a `GET /api/participants/{id}` endpoint to the Participants BC that returns a participant's `DisplayName` and `BidderId` for a given `ParticipantId`. The endpoint backs the catalog-page header rendering of the bidder's display name (rendered in narratives 001 Moment 1 and 003 Moment 3 but not lived-backed today). The endpoint does NOT return `CreditCeiling`; the credit ceiling stays hidden per the design intent established in narrative 001 Setting and reaffirmed in narrative 003 Moment 2.

## Context to load

- `docs/milestones/M1-skeleton.md` — the M1 milestone doc; authoritative for scope
- `CLAUDE.md` — routing layer, [AllowAnonymous] through M6 posture
- `docs/skills/wolverine-message-handlers.md` — handler patterns for HTTP endpoints
- `docs/skills/marten-event-sourcing.md` — Marten aggregate read patterns
- `docs/narratives/003-bidder-starts-anonymous-session.md` — the narrative whose journey requires this endpoint (Moment 3's catalog-header display-name rendering)
- `docs/narratives/003-findings.md` — Finding 002 with full discrepancy and resolution rationale
- `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs` — the existing slice 0.2 POST, for naming and structural consistency
- `src/CritterBids.Participants/Participant.cs` — the aggregate the endpoint reads from

## In scope

- New feature folder `src/CritterBids.Participants/Features/GetParticipant/`.
- One endpoint with `[WolverineGet("/api/participants/{id}")]` and `[AllowAnonymous]`.
- One response record type carrying `ParticipantId`, `DisplayName`, `BidderId`.
- Endpoint reads from the `Participant` aggregate via `IDocumentSession.LoadAsync<Participant>(id)` or `LoadAggregateAsync<Participant>(id)` per the Marten convention; choose at session time based on whether the read needs the latest event or the projected document is sufficient.
- Returns 404 with no body for unknown `ParticipantId`.
- Returns 200 with the response record body for known `ParticipantId`.
- Note: `Participant` aggregate today carries `Id`, `HasActiveSession`, `IsRegisteredSeller` only; it does NOT currently store `DisplayName` or `BidderId`. The aggregate must grow to persist these (read from the `ParticipantSessionStarted` event payload during `Apply`), OR the endpoint must read from the event stream directly (`session.Events.AggregateStreamAsync<...>()` or a dedicated Marten projection). Resolution path is part of this slice's design call; flag at session start.
- One xUnit test class at `tests/CritterBids.Participants.Tests/Features/GetParticipant/GetParticipantTests.cs` covering: 200 happy path returns expected fields; 404 for unknown id; `CreditCeiling` is NOT in the response payload (negative assertion).

## Explicitly out of scope

- Authentication / authorization beyond `[AllowAnonymous]`. The global `[Authorize]` posture does not apply through M6 per CLAUDE.md.
- Returning `CreditCeiling` in the response. The credit ceiling remains hidden by design.
- Any participant lookup mechanism beyond stream-ID. No "lookup by display name", no "lookup by BidderId", no listing endpoints.
- Changes to the existing `StartParticipantSession` POST endpoint or its response shape.
- Updates to narrative 001, narrative 003, or `001-scenarios.md` slice 0.2. The narratives' UI claims are now backed by this endpoint when it ships; no narrative edits are required.
- Adding a `GET /api/participants` collection endpoint. Single-entity-by-id only.

## Conventions to pin or follow

- Wolverine HTTP endpoint conventions per `docs/skills/wolverine-message-handlers.md`. Handler-method-tuple-return patterns, anti-pattern avoidance, route-template consistency with the existing POST.
- `[AllowAnonymous]` posture per CLAUDE.md until M6.
- Negative-assertion test for `CreditCeiling`: `response.Should().NotContain("CreditCeiling")` or equivalent. The credit ceiling staying hidden is the design intent; the test enforces it.

## Acceptance criteria

- [ ] New feature folder exists at `src/CritterBids.Participants/Features/GetParticipant/`.
- [ ] `GET /api/participants/{id}` endpoint registered via `[WolverineGet]`; carries `[AllowAnonymous]`.
- [ ] 200 response shape: `{ ParticipantId, DisplayName, BidderId }`. No `CreditCeiling`. No `HasActiveSession`. No `IsRegisteredSeller`.
- [ ] 404 response for unknown `ParticipantId`.
- [ ] xUnit tests pass on the Participants test project: 200 happy path, 404 unknown id, `CreditCeiling` negative assertion.
- [ ] `dotnet build` clean (0 warnings, 0 errors); `dotnet test` clean on the Participants test project.
- [ ] Slice retrospective at `docs/retrospectives/M1-FU1-get-participant-endpoint.md` (or analogous slug) appended; mirrors the M1 retro shape.

## Open questions

- **Aggregate-grows vs separate-projection.** The `Participant` aggregate currently does not carry `DisplayName` or `BidderId`. Either grow the aggregate (add fields, populate them in `Apply(ParticipantSessionStarted)`) or build a dedicated read model. Trade-off: aggregate-grows is simpler and keeps related state together; separate-projection isolates read concerns and avoids enriching the aggregate with display-only data. Lean: aggregate-grows for MVP; the aggregate is small and the fields are tightly coupled to its identity. Flag at session start.
- **`AggregateStreamAsync` vs `LoadAsync<Participant>` for the read.** Marten's auto-projected `Participant` document is updated as events land; `LoadAsync` reads it directly. `AggregateStreamAsync` rebuilds from events on every read. Lean: `LoadAsync` for MVP read latency; `AggregateStreamAsync` is overkill for a stable post-`ParticipantSessionStarted` aggregate. Flag at session start.
- **Test for "credit ceiling stays hidden" — string assertion vs typed response.** The endpoint's response type is statically constrained to not include `CreditCeiling`; a string assertion against the JSON body is an extra check. Lean: include the string assertion as a regression-safety belt-and-braces, since the credit-ceiling-hidden invariant is design-intent that future contributors might inadvertently violate by adding a field.

---

## Stub provenance

This stub was authored as part of foundation-refresh Phase 5 Item 1b's narrative 003 session at session close, per the Phase 2.5 discipline for `code-update` findings whose resolution exceeds a one-line edit. The narrative 003 session surfaced the gap (Finding 002) and routes it to this stub; the stub does not authorise running the slice. The actual implementation runs as standard product work whenever the M1 follow-up is scheduled.
