# N004-FU: SubmitListing HTTP Endpoint

**Source:** `docs/narratives/004-findings.md` Finding 002
**Narrative:** `docs/narratives/004-seller-publishes-and-withdraws-listing.md` (joint-authority citation per AUTHORING.md rule 3)
**Milestone:** M2 follow-up (Selling BC gap surfaced post-M2; not blocking subsequent milestones)
**Slice:** 1.2-FU (slice 1.2's HTTP companion; the slice 1.2 SubmitListing handler shipped in M2-S6 as a Wolverine-internal aggregate handler with no HTTP endpoint)
**Agent:** @PSA (Selling BC owner)
**Estimated scope:** ~1 PR; one new endpoint registration; one HTTP-grade test class

## Goal

Add a `[WolverinePost]` HTTP endpoint to `src/CritterBids.Selling/SubmitListing.cs` that dispatches the existing `SubmitListing` command to the existing `SubmitListingHandler.Handle` against a `[WriteAggregate]`-loaded `SellerListing`. The endpoint backs the seller-dashboard "Submit" button (forward-spec UI for M6 frontend MVP) which is rendered in narrative 001 (offstage), narrative 004 Moment 3 (onstage from GreyOwl12's window), and narrative 004 Moment 4 (compressed). The handler logic does not change; only the HTTP entry point is added.

## Context to load

- `docs/milestones/M2-listings-pipeline.md` — the M2 milestone doc; authoritative for scope
- `CLAUDE.md` — routing layer, [AllowAnonymous] through M6 posture
- `docs/skills/wolverine-message-handlers.md` — handler patterns for HTTP endpoints, the (Events, OutgoingMessages) tuple-return shape
- `docs/skills/marten-event-sourcing.md` — `[WriteAggregate]` aggregate-loading pattern
- `docs/narratives/004-seller-publishes-and-withdraws-listing.md` — the narrative whose Moment 3 journey requires this endpoint
- `docs/narratives/004-findings.md` — Finding 002 with full discrepancy and resolution rationale
- `src/CritterBids.Selling/SubmitListing.cs` — the existing handler; the endpoint registration goes here (or in a sibling file if the session prefers)
- `src/CritterBids.Selling/SellerListing.cs` — the aggregate the endpoint loads
- `docs/retrospectives/M2-S6-slice-1-2-submit-listing-retrospective.md` — design-time decisions including why no HTTP endpoint was authored at M2-S6

## In scope

- Add `[WolverinePost("/api/listings/{id}/submit")]` registration on a method that dispatches the existing `SubmitListingHandler.Handle`. The route uses `{id}` to align with the existing `RegisterAsSeller` route convention (`/api/participants/{id}/register-seller`).
- The endpoint method carries `[AllowAnonymous]` per CLAUDE.md M1-through-M6 posture.
- The endpoint accepts no request body (or accepts an empty body with optional `SellerId` for `FindIdentity` resolution per the existing `RegisterAsSeller` pattern at `RegisterAsSeller.cs:13-17`).
- The endpoint returns `200 OK` on the happy path (consistent with the existing `RegisterAsSeller` post-registration response — appending to an existing resource, not creating).
- Consider returning the listing's post-submit `Status` in the response body (e.g., `{ Status: "Published" }` for the auto-approve happy path or `{ Status: "Rejected", Reason: "..." }` for the validation-rejected path). This shape change is the session's design call.
- Map the existing handler exceptions to HTTP status codes: `InvalidListingStateException` (state guard violation) → 409 Conflict; validation rejections → handled internally (the handler emits `ListingRejected` event but returns successfully; the endpoint may translate this to a 422 Unprocessable Entity for HTTP semantics, or return 200 with the rejection in the body — design call).
- One xUnit HTTP-grade test class at `tests/CritterBids.Selling.Tests/Features/SubmitListing/SubmitListingEndpointTests.cs` covering: 200 happy path (auto-approve and publish), 409 on submitting a `Submitted`/`Published`/`Withdrawn` listing, 422-or-200-with-rejection on validation failure (depending on response-shape choice), 404 on unknown ListingId.

## Explicitly out of scope

- Changes to `SubmitListingHandler.Handle` itself. The handler logic stays.
- Changes to the domain events (`ListingSubmitted`, `ListingApproved`, `ListingPublished`, `ListingRejected`) or the integration event (`Contracts.Selling.ListingPublished`).
- Changes to `ListingValidator` or its validation rules.
- The seller-dashboard frontend (M6 territory).
- Any other Selling BC HTTP endpoints (the analogous `WithdrawListing` HTTP endpoint is M4-S2's responsibility per `docs/prompts/implementations/M4-S2-selling-withdraw-listing.md`; the analogous `UpdateDraftListing` HTTP endpoint is its own potential future stub).
- Changes to the existing M2-era integration tests for `SubmitListingHandler` via aggregate-handler invocation.

## Conventions to pin or follow

- Wolverine HTTP endpoint conventions per `docs/skills/wolverine-message-handlers.md`. Handler-method-tuple-return patterns, anti-pattern avoidance, route-template consistency with existing endpoints.
- `[AllowAnonymous]` posture per CLAUDE.md until M6.
- Route template consistency with `RegisterAsSeller`'s `/api/participants/{id}/register-seller` shape: nest the action verb under the resource path.

## Acceptance criteria

- [ ] `[WolverinePost("/api/listings/{id}/submit")]` (or analogous slug) exists in the Selling BC.
- [ ] The endpoint dispatches `SubmitListing` to `SubmitListingHandler.Handle` via `[WriteAggregate]` loading.
- [ ] 200 response on the happy path (auto-approve and publish completes).
- [ ] 409 response on state-guard violation (submit from `Submitted`/`Published`/`Withdrawn`).
- [ ] 404 response on unknown `ListingId` (handled by `[WriteAggregate]` `OnMissing.Simple404` per the existing pattern).
- [ ] Validation-rejection response shape decided and documented (200-with-body vs 422-with-body).
- [ ] xUnit tests pass on the Selling test project.
- [ ] `dotnet build` clean (0 warnings, 0 errors); `dotnet test` clean on the Selling test project.
- [ ] Slice retrospective at `docs/retrospectives/M2-FU1-submit-listing-endpoint.md` (or analogous slug) appended; mirrors the M2 retro shape.

## Open questions

- **Endpoint co-location vs separate file.** The existing `SubmitListing.cs` carries the command record and the `SubmitListingHandler` static class. Adding the `[WolverinePost]` method in the same file is the simplest path; a sibling file `SubmitListingEndpoint.cs` is the alternative. Lean: same file for simplicity; the handler and endpoint are tightly coupled.
- **Validation-rejection response shape.** When `ListingValidator.Validate` returns `IsRejection: true`, the handler emits `ListingSubmitted + ListingRejected` events successfully but does not produce an outgoing integration event. The HTTP endpoint can translate this to (a) 200 OK with `{ Status: "Rejected", Reason: <validator reason> }` in the body, or (b) 422 Unprocessable Entity with `{ ProblemDetails }`-shaped body. Lean: 422 with ProblemDetails for HTTP semantic correctness; the seller dashboard can render either. Confirm at session start.
- **Response body shape for the 200-happy-path.** Three options: (a) empty body (matches `RegisterAsSeller`'s post-registration shape), (b) `{ Status: "Published", PublishedAt: <utc> }` (gives the seller dashboard the post-submit aggregate state without a follow-up GET), (c) full SellerListing read-model shape. Lean: (b) — minimal extension to the empty-body precedent that adds load-bearing seller-visible state. Confirm at session start.

---

## Stub provenance

This stub was authored at narrative 004's session close (foundation-refresh Phase 5 Item 1c, 2026-04-29) per the Phase 2.5 discipline for `code-update` findings whose resolution exceeds a one-line edit. The narrative 004 session surfaced the gap (Finding 002) and routes it to this stub; the stub does not authorise running the slice. The actual implementation runs as standard product work whenever the M2 follow-up is scheduled.
