# Narrative 003 - Findings

Findings surfaced while authoring `003-bidder-starts-anonymous-session.md` against W001 slice 0.2 and lived `src/CritterBids.Participants/` code. Each finding is routed via the four-lane discipline established in ADR 016 and detailed in the narrative-authoring prompt at `docs/prompts/narratives/003-bidder-starts-anonymous-session.md`:

| Lane | Resolved in this PR? |
|---|---|
| `narrative-update` | n/a (none surfaced) |
| `workshop-update` | n/a (none surfaced; slice 0.2's prior workshop drift was already corrected by narrative 001 Finding 002) |
| `code-update` | Both findings here. F001 resolved in-PR (one-line comment edit). F002 routed to a stub follow-up implementation prompt per Phase 2.5 discipline. |
| `document-as-intentional` | n/a (none surfaced) |

Lane mix: 0 `narrative-update`, 0 `workshop-update`, 2 `code-update`, 0 `document-as-intentional`. The lived-code audit posture surfaced two real `code-update` findings, exactly the lane mix the prompt's "Heads-up sources of likely findings" anticipated.

---

### Finding 001 - `StartParticipantSession.cs` comments overstate display-name uniqueness as guaranteed-by-stream-ID

**Routing:** code-update

**Surfaced at:** Moment 2 (per-Moment handler read before drafting)

**Discrepancy.** The lived `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs` carries two comment lines that overstate display-name uniqueness:

- Line 12 reads: `// Names are derived from UUID v7 random bytes — uniqueness is guaranteed by stream ID uniqueness.`
- Lines 47-48 read (in context with surrounding comments): `// Two UUIDs created in the same millisecond share only the timestamp prefix (bytes 0–7); / // bytes 8–15 are independently random, ensuring display name uniqueness.`

Both claims are wrong. Stream-ID uniqueness (UUID v7's ~10^38 collision space) cannot propagate through the modulo derivations (`bytes[8] % 25`, `bytes[9] % 29`, `((bytes[10] << 8) | bytes[11]) % 9999 + 1`) to a finite tuple space (~7.25M display-name tuples). Two distinct UUIDs can land on the same `(adjective, animal, number)` tuple by independent randomness; collision probability is below 0.001% at the demo's bidder count but is non-zero. Narrative 001 Finding 002 (filed in `001-findings.md`) routed the analogous workshop-side claim `workshop-update` and corrected `001-scenarios.md` slice 0.2's "Display name is unique within active sessions" assertion to "Display names are probabilistically unique within active sessions." The lived code comments are the residual that did not get swept by narrative 001's session.

The M1-S5 retrospective at `docs/retrospectives/M1-S5-slice-0-2-start-participant-session.md` carries the same misclaim; whether the retro should also be corrected is a separate question (retros are point-in-time observations and may stay as-is per the narratives README's "memories are point-in-time observations" framing). The lived code comment is the canonical source-of-truth that downstream readers will see when reading the handler; correcting it is the load-bearing fix.

**Resolution.** In-PR comment edits to `StartParticipantSession.cs`:

- Line 12 rewritten: `// Names are derived from UUID v7 random bytes. Uniqueness is probabilistic, not guaranteed; collision probability is below 0.001% at the demo's bidder count. See narrative 001 Finding 002 (docs/narratives/001-findings.md) for the workshop-side correction; this comment carries the lived-code-side correction.`
- Lines 47-48 rewritten: `// Two UUIDs created in the same millisecond share only the timestamp prefix (bytes 0–7); / // bytes 8–15 are independently random, giving display-name collisions a probabilistic floor under 0.001% at conference scale.`

Per the `feedback_dotnet_build_test_after_cs_touch.md` memory, `dotnet build` and `dotnet test` run after the comment edits. The change is comment-only; no behavior change; tests pass.

---

### Finding 002 - No `GET /api/participants/{id}` endpoint backs the catalog-header display-name UI claim

**Routing:** code-update

**Surfaced at:** Moment 3 (per-Moment surrounding-directory read for the catalog-header UI claim)

**Discrepancy.** Narrative 001 Moment 1 renders SwiftFerret42's phone "transitioning to the catalog page with 'SwiftFerret42' displayed in the header" after the `StartParticipantSession` HTTP response returns. Narrative 003 Moment 3 renders the analogous beat for BoldPenguin7. Both renderings claim the catalog header displays the bidder's `DisplayName`.

Search of `src/CritterBids.Participants/Features/` reveals two HTTP endpoints, both POST:

- `[WolverinePost("/api/participants/session")]` in `StartParticipantSession.cs:34` (the slice 0.2 endpoint).
- `[WolverinePost("/api/participants/{id}/register-seller")]` in `RegisterAsSeller.cs:50` (the slice 0.3 endpoint).

There is no `GET /api/participants/{id}`, no `GET /api/participants/{id}/profile`, no `GET /api/participants/{id}/display-name`, and no participant-projection read endpoint anywhere in the BC. The `StartParticipantSession` POST response carries only the `ParticipantId` Guid in its body and the Location header `/api/participants/{ParticipantId}` (a URI that 404s today). The `DisplayName`, `BidderId`, and `CreditCeiling` are committed to the `ParticipantSessionStarted` event payload and to the `Participant` aggregate's read model (Marten's auto-projected aggregate), but no HTTP endpoint exposes them.

The frontend's catalog-page header rendering of "BoldPenguin7" therefore has no backend backing today. Two interpretations:

1. **Forward-spec for M6 frontend MVP.** Per `CLAUDE.md`'s "[AllowAnonymous] on all endpoints through M6" posture, the frontend itself is M6 territory. The catalog-header claim is forward-spec UI behavior; the backend gap will be filled when the frontend lands.
2. **Backend-side gap independent of frontend timing.** Even before the M6 frontend, integration tests, third-party clients, and the operator dashboard may want to read a participant's display name from a known endpoint. The gap is a real backend deficiency, not just a frontend-deferred concern.

Either interpretation routes to the same fix: add a `GET /api/participants/{id}` endpoint on the Participants BC.

**Resolution.** Stub follow-up implementation prompt authored at `docs/prompts/implementations/get-participant-endpoint.md` (or analogous slug under `docs/prompts/implementations/`) per Phase 2.5 discipline. The stub names the slice scope: add a `GET /api/participants/{id}` Wolverine HTTP endpoint that returns `{ ParticipantId, DisplayName, BidderId }` for a given `ParticipantId`. Returns 404 for unknown ids. Does NOT return `CreditCeiling` (the credit ceiling stays hidden per the design intent established in narrative 001 Setting and reaffirmed in narrative 003 Moment 2). The endpoint reads from the `Participant` aggregate's auto-projected document (or a dedicated read model if the aggregate's shape changes for an unrelated reason). Per the `[AllowAnonymous]` posture through M6, the endpoint carries `[AllowAnonymous]` for now; the global `[Authorize]` convention applies once M6 lands and the bidder's session token gates access.

The stub does not authorise running the slice; that runs as standard product work. Narrative 003 surfaces the gap and routes it; subsequent product work resolves it.
