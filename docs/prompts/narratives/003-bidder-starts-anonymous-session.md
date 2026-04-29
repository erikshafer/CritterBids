# Prompt 003 - Author the Participants-BC Backfill Narrative: Bidder Starts Anonymous Session

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-29 |
| **Phase** | Foundation Refresh, Phase 5, Item 1b |
| **Subdirectory** | `docs/prompts/narratives/` |
| **Journey** | An anonymous bidder lands at the platform; the system mints her session, identity, and credit ceiling (happy path) |
| **Protagonist** | BoldPenguin7 (offstage in narrative 001 Cast → onstage here) |
| **Target artifact** | `docs/narratives/003-bidder-starts-anonymous-session.md` (to be produced) |
| **Companion artifact** | `docs/narratives/003-findings.md` (to be produced; conscious-skip note acceptable if zero findings) |
| **Source-of-truth dependencies** | W001 §"Tier 0 — Bidder onboarding" (slice 0.2); lived `src/CritterBids.Participants/` code; M1-S4 (BC scaffold) and M1-S5 (slice 0.2) retros |
| **Workflow position** | Second of four Phase 5 backfill narratives. Companion to narrative 001 Moment 1 at finer grain. |

---

## Framing

This session authors the Participants BC's first dedicated narrative. It picks the same slice narrative 001 Moment 1 covered (slice 0.2, anonymous session start) and zooms in: where narrative 001 collapsed the entire session-start cascade into one bidder-visible Moment, narrative 003 dramatises the Participants BC's internal mechanics — the UUID v7 stream creation, the byte-derived display-name and BidderId mints, the credit-ceiling roll, the aggregate's HasActiveSession flip, the HTTP response shape — at the grain at which a Participants-BC developer would think about the system.

Narrative 003 inverts narrative 002's posture in two ways. First, **the audit surface is fully lived**: M1 has shipped, the Participants BC is in production for the conference demo, the `StartParticipantSession` handler is real code at `src/CritterBids.Participants/Features/StartParticipantSession/`. There is no forward-spec defer for this narrative; every Moment can be audited against shipped behavior. Second, **`code-update` findings are a real lane** rather than a structural impossibility. Narrative 001 already surfaced one Participants-grade discrepancy as a `workshop-update` (Finding 002, display-name probabilistic uniqueness vs the workshop's hard-uniqueness assertion); narrative 003 may surface additional `code-update` candidates as the saga of session-start gets dramatised at finer grain than narrative 001 reached.

The protagonist switch — from SwiftFerret42 (narrative 001's bidder, narrative 002's winner) to BoldPenguin7 (narrative 001's offstage competing bidder) — is deliberate. Each backfill narrative either varies the protagonist or zooms in on a narrative-001 actor; narrative 002 chose continuity (SwiftFerret42 at finer grain on Moment 8), narrative 003 chooses variance (BoldPenguin7 brought onstage). The cumulative effect across narratives 001-005 is a multi-perspective Cast where no single bidder over-anchors the project's narrative library.

ADR 016 (Spec-Anchored Development) governs the relationship: specs describe intent; code is authoritative for runtime; drift is caught at retrospective time. Narrative 003's audit floor is shipped Participants code, so the ADR-016 routing is the standard four-lane discipline: `narrative-update` if the narrative is wrong, `workshop-update` if W001 is wrong, `code-update` if the code is wrong, `document-as-intentional` if the apparent disagreement is two valid expressions of the domain.

The Phase 5 prompt §3.3 misnamed the BC scaffold retro as M1-S2; the actual scaffold retro is `M1-S4-participants-bc-scaffold.md` (M1-S2 is the infrastructure-orchestration ADR session). Narrative 003 cites the correct retro names.

---

## Goal

Author the Participants BC's backfill narrative covering BoldPenguin7's experience as her phone scans the QR code at the conference, the system mints her anonymous session and identity, and she lands on the catalog page ready to bid. Audit W001 §"Tier 0 — Bidder onboarding" and lived `src/CritterBids.Participants/` code against the narrative as drafted, route every disagreement through the four-lane findings discipline, add per-row narrative back-references on the W001 slice 0.2 entry that narrative 003 implements.

---

## Orientation files (read in order before starting)

1. `C:\Code\CritterBids\CLAUDE.md` — routing layer and global conventions.
2. `C:\Code\CritterBids\docs\narratives\README.md` — format manual v0.1.
3. `C:\Code\CritterBids\docs\narratives\001-bidder-wins-flash-auction.md` — Moment 1 specifically (slice 0.2 at coarser grain). Cast and Setting sections also relevant for the BoldPenguin7 introduction.
4. `C:\Code\CritterBids\docs\narratives\002-winner-clears-settlement.md` and `002-findings.md` — the prior backfill narrative's discipline reference. Narrative 003 inherits the closing-arc shape (deferred section, retrospective, findings file, status flip).
5. `C:\Code\CritterBids\docs\workshops\001-flash-session-demo-day-journey.md` §"Tier 0 — Bidder onboarding" plus the slice tables for slice 0.2 (and 0.3 if seller registration enters the audit surface).
6. `C:\Code\CritterBids\docs\workshops\001-scenarios.md` slice 0.2 scenarios.
7. `C:\Code\CritterBids\docs\retrospectives\M1-S4-participants-bc-scaffold.md` and `M1-S5-slice-0-2-start-participant-session.md` — design-time decisions the code alone does not show (per narrative 001 retro's "reading the slice's retrospective alongside its code revealed design-time decisions" pattern).

Per-Moment lived-code reads under `src/CritterBids.Participants/`:
- `Participant.cs` (the aggregate root; `Apply` methods)
- `ParticipantsModule.cs` (DI wiring)
- `Features/StartParticipantSession/` (the slice 0.2 feature folder; handler, command, endpoint)

---

## Working pattern

Same interactive cadence as narrative 002. Cast and Setting first; Moment-by-Moment thereafter with sign-off and commit per beat. Multi-paragraph `Response.` blocks for any Moment that covers multiple system mechanics (likely Moment 2's mint-cascade — UUID v7 creation, byte-derived field derivations, credit-ceiling roll, event commit — fits the multi-paragraph pattern even without crossing slice boundaries).

Per-Moment lived-code audit:
- For each Moment, read the implementing code path before drafting (`StartParticipantSession.cs`, `Participant.cs`, the endpoint).
- Read the matching scenario from `001-scenarios.md` slice 0.2.
- Read the M1-S5 retro entries that touch the Moment's surface.
- Draft the Moment in the README's Guardrail-1 shape.
- Identify findings as the draft is written.
- Sign-off, commit.

Per-Moment "deliberately not included" subsection at draft time, tagged with a disposition. Aggregates into `## Deferred from this narrative` at session close.

---

## Voice and perspective

Single-named-protagonist plus omniscient narrator, locked by the narratives README v0.1.

Protagonist is **BoldPenguin7**, brought onstage from narrative 001's offstage Cast. She is the competing bidder who outbids SwiftFerret42 in narrative 001 Moment 5 and ultimately loses the keyboard in narrative 001 Moments 6 and 7; narrative 003 covers her *entry* into the system, days or hours before she ever places a bid. Her journey is small in scope (one slice, one session-start cascade) but intricate in system-internal mechanics.

The narrator is omniscient about the system. It can name `ParticipantId`, `BidderId`, the byte-by-byte derivation of `DisplayName`, the credit-ceiling band, the aggregate's state machine — facts BoldPenguin7 does not perceive. It dramatises only what BoldPenguin7 actually experiences (the QR scan, the page load, the catalog landing). The narrator carries the system-internal beats; the protagonist carries the journey arc.

---

## Findings discipline (lived-code lane mix)

Audit-floor is shipped Participants code. Expected lane mix:

| Lane | Meaning | Expected frequency in narrative 003 |
|---|---|---|
| `narrative-update` | Code is right; the narrative renders something inaccurate. | Moderate. First-pass drafts will need correction against the lived `StartParticipantSession.cs` derivation. |
| `workshop-update` | Workshop is stale (event renamed, payload drifted, slice intent shifted). | Low to moderate. Narrative 001 already surfaced Finding 002 (display-name probabilistic uniqueness) on this slice; remaining workshop drift on slice 0.2 is unlikely but possible. |
| `code-update` | Code is wrong relative to domain understanding. | **Real lane.** Possible candidates from narrative 001's heads-up territory: the `ParticipantId` vs `BidderId` distinction in the response payload, the `HasActiveSession` aggregate flip semantics, edge cases around stream-ID derivation. |
| `document-as-intentional` | Code and workshop are both right; apparent disagreement is two valid expressions. | Moderate. Some narrative 001 Moment 1 framing (e.g., the credit-ceiling-as-silent-ceiling pattern) is settled design that may surface as a deliberate-but-undocumented finding for narrative 003 to capture. |

`code-update` findings produce stub follow-up implementation prompts at `docs/prompts/implementations/<slug>.md` per the Phase 2.5 discipline. They are not resolved in this PR; they queue for product work.

### Findings file shape

Same schema as narratives 001 and 002:

```
### Finding NNN - <one-line title>

**Routing:** narrative-update | workshop-update | code-update | document-as-intentional

**Surfaced at:** Moment X | per-Moment proposal | session close

**Discrepancy.** What disagrees with what. Cite the workshop slice, the
code file or commit, and the narrative Moment that surfaced it.

**Resolution.** What was done in this PR (for narrative-update,
workshop-update, document-as-intentional). For code-update: the path to the
stub follow-up prompt under docs/prompts/implementations/.
```

A conscious-skip note in the narrative-internal retro suffices if zero findings surface, though zero is unlikely given the slice's prior history of surfacing Finding 002 in narrative 001's session.

### Heads-up sources of likely findings

Do not pre-decide outcomes. Be ready when these come up:

1. **Display-name uniqueness (already-known territory).** Narrative 001 Finding 002 routed this `workshop-update` (probabilistic uniqueness, MVP posture). The lived `StartParticipantSession.cs` code comment may still carry the misclaim. If the narrative dramatises BoldPenguin7's `DisplayName` mint at the Moment grain narrative 001 did not reach, the comment correction may surface as a small `code-update` candidate distinct from the workshop's already-resolved Finding 002.
2. **`ParticipantId` vs `BidderId` distinction.** Narrative 001 mentions both: `ParticipantId` as the long-form stream key, `BidderId` as the short-form "Bidder N" identifier riding on bids. The lived response payload returns `ParticipantId` only (per narrative 001 Moment 1's Response paragraph). Whether the BidderId is durably persisted and how it's exposed to downstream consumers is worth auditing — the answer may be a `document-as-intentional` clarification or a `code-update` if the projection-side rendering is incomplete.
3. **Credit-ceiling band edges.** Narrative 001 Finding 001 corrected the band to "$200 to $1000 in $100 steps". Narrative 003's Moment dramatising the credit-ceiling roll should derive the value from byte 14 per `StartParticipantSession.cs:62`. Edge cases (byte 14 = 0 yields $200; byte 14 = 8 yields $1000; values 9-255 wrap modulo 9) may surface as either `narrative-update` or `code-update` depending on whether the wrapping is intentional or a derivation oversight.
4. **`HasActiveSession` aggregate flip.** Narrative 001 Moment 1 mentions the `Participant` aggregate's `Apply` method flipping `HasActiveSession` to true. The aggregate's full state shape, the `Apply` method's other handled events, and whether the aggregate is read anywhere else in the system are worth auditing at the BC-narrative grain. May surface `narrative-update` (narrative 001 understated the aggregate's complexity), `code-update` (aggregate has dead state), or `document-as-intentional` (the aggregate is intentionally minimal for MVP).
5. **HTTP endpoint shape and the Location header.** Narrative 001 Moment 1 says the response returns `ParticipantId` and a Location header; the credit ceiling never appears in the payload. The lived endpoint code should be verified at the HTTP-response level. Possible findings around the endpoint method signature, the route name (`/api/participants/session`), or the `[AllowAnonymous]` posture (M6 deferred per CLAUDE.md).
6. **Marten-8 projection-validator concerns.** A memory entry notes that Marten 8's projection validator rejects empty-Apply aggregates. The Participant aggregate must therefore have at least one Apply/Create/ShouldDelete method; the narrative's audit should confirm this and surface any minimum-Apply quirks as either `document-as-intentional` (minimum-Apply-for-validator is intentional) or `code-update` (the aggregate has a stub Apply that should be removed once a real domain event is added).

---

## Cross-reference discipline

- Each Moment cites its slice via `**Implements:** slice 0.2`. If the narrative scope expands to include slice 0.3 (seller registration, if it touches the Participants BC) at session start, the closing Moment cites both as multi-slice.
- Domain event names render in code-style backticks: `ParticipantSessionStarted`. Plain text for ordinary nouns: Participant, Bidder Session, Bidder Identifier, Display Name, Credit Ceiling.
- Do not restate `001-scenarios.md` slice 0.2 content. Reference the slice number; the workshop is the test specification, the narrative is the journey.
- W001 already carries a consolidated Narrative Cross-References block (extended in narrative 002's PR to reference narrative 001 + narrative 002). Narrative 003 extends the same block with a new bullet for narrative 003 implementing slice 0.2 (and 0.3 if scope expands). Per Phase 3 Item 2's consolidated form for journey-grain workshops; narrative 001 already implements 0.2, so the cross-reference block lists both narratives 001 and 003 against slice 0.2.

---

## What the narrative does NOT carry

- **No code or pseudocode.** Aggregate Apply methods, handler return types, byte-derivation expressions are described in prose.
- **No implementation choices.** Marten primitive choices, Wolverine handler routing, ASP.NET endpoint conventions belong to skill files.
- **No architectural decisions.** ADR candidates surface in the deferred section, not as in-narrative resolutions.
- **No GWT test specifications.** Reference `001-scenarios.md` slice numbers; do not restate.
- **No UX or UI design.** Render at the bidder-experience grain ("the page loads", "the catalog header shows her display name"); do not design the screens.
- **No re-authoring of narrative 001.** Narrative 001 is `status: accepted`. Single-paragraph cite-and-edit fixes against narrative 001 Moment 1 are permitted per Phase 5 §7 if the audit surfaces drift; structural rewrite is not.

---

## In scope (proposed Moment list)

| Moment | Slice from W001 | Bidder experience |
|---|---|---|
| 1 | 0.2 | BoldPenguin7 scans the QR code at the conference; her phone loads the demo's landing route. The HTTP request is in flight. |
| 2 | 0.2 (continuation) | The handler mints her identity. Multi-paragraph `Response.`: UUID v7 stream creation; byte-by-byte derivations of `DisplayName`, `BidderId`, and credit ceiling; `ParticipantSessionStarted` committed; the `Participant` aggregate's `Apply` flips `HasActiveSession`. BoldPenguin7 perceives nothing yet (the request is mid-flight). |
| 3 | 0.2 (continuation) | The HTTP response returns. BoldPenguin7's phone receives the `ParticipantId` and Location header; the credit ceiling is invisible by design. The page transitions to the catalog with "BoldPenguin7" in the header. **First (and primary) bidder-visible beat.** |

Three Moments. Narrative 001's Moment 1 collapsed all three into one bidder-visible beat; narrative 003's three-Moment grain dramatises the system-internal split. Alternative groupings:

1. **Two Moments** (collapse 1 and 2 into one, keep 3 separate) — defensible if the QR scan and the system response feel like one beat from BoldPenguin7's window. Counter-argument: the narrator's responsibility to dramatise the saga between the request and the response is what justifies finer grain in the first place.
2. **Four Moments** (split Moment 2 into "stream created and identity minted" plus "credit ceiling rolled and aggregate state advanced") — defensible if Moment 2's multi-paragraph `Response.` feels too dense. Counter-argument: the credit-ceiling roll happens in the same handler call and is structurally part of the same atomic operation; splitting may dramatise more than the system actually does at runtime.

Lean: three Moments. Flag at session start if a different grain fits better.

---

## Out of scope for this session

- **Failure paths.** A duplicate-session-start (rejoin-vs-new-session on QR re-scan), an HTTP request rejected by infrastructure, a UUID v7 collision (vanishingly improbable), a byte-derivation edge case producing a malformed `DisplayName`. Each is an `alternate-path-failure` deferral.
- **Authentication or account binding.** M6 introduces real authentication and the `[AllowAnonymous]` posture lifts at that point. Out of scope per CLAUDE.md.
- **Seller registration (slice 0.3).** Even if 0.3 touches the Participants BC, the seller's perspective is a candidate for narrative 004 (Selling BC backfill) or a future seller-perspective Participants narrative. Out of scope here unless surfaced at session start.
- **The bidder's first bid placement (Moment 4 of narrative 001).** That belongs to narrative 005 (Auctions BC backfill) or remains under narrative 001's coverage; out of scope here.
- **The session lifecycle beyond start.** Session expiry, session re-entry, cross-session identity continuity. All `defer` deferrals.
- **Any code refactor.** `code-update` findings produce stub follow-up prompts; the slices run in subsequent product work, not in Phase 5.
- **W001 broad backfill of narrative back-references.** Only slice 0.2 (and 0.3 if scope expands) gets an entry on W001's consolidated Narrative Cross-References block; other W001 slices are out of scope for this session.
- **Methodology format changes.** The narratives README v0.1 dialect remains locked.
- **Phase 5 cross-narrative retrospective.** Item 4 territory.

---

## Deliverable plan

Per Phase 5 prompt §3.3 acceptance gates:

1. **Narrative file** at `docs/narratives/003-bidder-starts-anonymous-session.md`. Frontmatter v1, prose-paragraph Moments, single-named-protagonist voice. `status: accepted` at session close.
2. **Findings file** at `docs/narratives/003-findings.md`, OR a conscious-skip note in the narrative-internal retro if zero findings surface.
3. **Stub follow-up prompts** at `docs/prompts/implementations/<slug>.md`, one per `code-update` finding.
4. **Narratives README Index update** in `docs/narratives/README.md`. Row 003 added.
5. **W001 cross-reference extension** on the consolidated Narrative Cross-References block: a new bullet for narrative 003 listing slice 0.2 (and 0.3 if scope expands). Narrative 001's existing entry stays unchanged.
6. **Methodology log Entry 001 candidate.** Per the Phase 4 retro time-box, the lived-BC narratives are the chance for Entry 001 to land. Apply the entry-criteria gate at session close: a genuinely cross-cutting observation about lived-code-audit narrative authoring warrants the entry. Conscious-skip note in the narrative-internal retro is acceptable.
7. **Narrative-internal retrospective** appended in the narrative file after `## Deferred from this narrative`, mirroring narratives 001 and 002.

---

## Acceptance criteria

- [ ] `docs/narratives/003-bidder-starts-anonymous-session.md` exists. Frontmatter conforms to v1 vocabulary. `status: accepted`.
- [ ] Every Moment cites its W001 slice via `Implements:`.
- [ ] No bulleted lists appear inside any Moment body (Guardrail 1).
- [ ] No frontmatter keys outside the v1 vocabulary (Guardrail 2).
- [ ] Each Moment has a `Context.`, `Interaction.`, `Response.` body. `Why this matters to the bidder.` is present where it adds meaning.
- [ ] `## Deferred from this narrative` exists. Items are bucketed by the seven disposition tags.
- [ ] `docs/narratives/003-findings.md` exists with at least one finding, OR the narrative-internal retro contains an explicit conscious-skip note with rationale.
- [ ] Each `code-update` finding (if any) has a stub follow-up prompt at `docs/prompts/implementations/<slug>.md`.
- [ ] `docs/narratives/README.md` Index table contains row 003.
- [ ] W001's consolidated Narrative Cross-References block carries a new bullet for narrative 003 implementing slice 0.2 (and any other slices in scope).
- [ ] Narrative-internal retro appended after `## Deferred from this narrative`, mirroring narratives 001 and 002.
- [ ] No file under `src/` or `tests/` was edited in this session.

---

## Open questions to flag (not decide)

These are session-start decisions; surface them and ask the user before locking Cast and Setting.

- **Moment grain: three vs two vs four.** §"In scope" leans three. The two-Moment alternative (collapse QR scan + handler mint into one Moment) reads as one beat from BoldPenguin7's window; the four-Moment alternative (split the handler mint into "stream + identity" plus "credit-ceiling + aggregate") may dramatise more than the runtime atomic operation does. Lean: three.
- **Slice scope: 0.2 only vs 0.2 + 0.3.** Slice 0.3 (seller registration) may or may not touch the Participants BC; if it does, narrative 003 could optionally cover both slices' bidder-side and seller-side identity-mint mechanics. Lean: 0.2 only; defer 0.3 to narrative 004 (Selling BC).
- **Display-name uniqueness comment correction.** If the lived `StartParticipantSession.cs` code comment still carries the "stream-ID uniqueness propagates" misclaim that narrative 001 Finding 002 already routed `workshop-update`, narrative 003's audit may surface the comment correction as an additional `code-update`. The fix is a one-line edit; resolution scope (in this PR vs stub follow-up prompt) decided at the time the finding surfaces.
- **`ParticipantId` vs `BidderId` exposure audit depth.** The narrative will dramatise both identifiers; the audit may reveal that `BidderId` is computed at session-start time but not durably persisted or exposed. Whether the gap is `code-update`, `narrative-update`, or `document-as-intentional` depends on what `001-scenarios.md` slice 0.2 says about each identifier's lifecycle. Resolve at the relevant Moment.
- **Methodology log Entry 001 trigger.** The three lived-BC narratives ahead of Item 4 cutover are the lived chance for Entry 001 per the Phase 4 retro time-box. Narrative 003's session-close should explicitly weigh whether a genuinely cross-cutting observation about lived-code-audit narrative authoring warrants Entry 001. Silence is fine.
- **PR shape: fold prompt + narrative session into one PR vs separate prompt PR.** Default per the Phase 5 Item 1a precedent: fold into one PR with per-commit cadence. Confirm at session start.

---

## Memory inheritance

Phase 1, Phase 2, and narrative 002 session memories apply unchanged with one update:

- **Depth over brevity** when explaining tradeoffs.
- **Ubiquitous language** (auction-domain, Participants-flavored): Bidder, Seller, Auctioneer, Bidder Session, Anonymous Session, Display Name, Bidder Identifier (`BidderId`), Participant Identifier (`ParticipantId`), Credit Ceiling, Credit Ledger.
- **DDD, CQRS, Event Sourcing, EDA** assumed background.
- **SDD and NDD methodology vocabulary is NOT assumed background.** Define on first use: spec-anchored development, narrative-driven development, the findings discipline, the seven disposition tags, the Cast / Setting / Moment primitives, the Klefter pattern, the Bruun pattern, the Ralph Loop. Point at the foundation-refresh handoff §10 glossary, ADR 016, ADR 017, and the narratives README as durable references.
- **Lean opinions on questions.** Propose a default with rationale rather than open-ended elicitation.
- **Em-dash hygiene does NOT apply to internal docs.** Per the memory clarification at narrative 002 close, the no-em-dash convention was intended for external-facing prose only (blogs, conference abstracts, talk submissions). Internal narratives, workshops, retros, ADRs, prompts, and commit messages do not need em-dash hygiene. Skip the audit-after-write sweep.
- **Punchy prose; no AI-tool references in committed text.**
- **No `git push` to `main`** without explicit authorization. Commit freely on the narrative-003 branch; push only when asked.

---

## Starting move

When the session begins:

1. Re-read this prompt and `docs/narratives/README.md` v0.1 in full.
2. Re-read narrative 001 Moment 1 specifically (the coarser-grain companion this narrative refines) and narrative 001 Findings 001 and 002 (the already-resolved territory on this slice).
3. Confirm with user: Moment grain (three vs two vs four), slice scope (0.2 only vs 0.2 + 0.3), PR shape (fold vs separate). Lock these before drafting Cast.
4. Propose Cast and Setting. Sign-off. Commit.
5. Walk Moment-by-Moment per the working pattern. For each Moment: read the implementing slice in W001, read the lived `src/CritterBids.Participants/` code path, read the M1-S5 retro entries that touch the Moment's surface, draft the Moment, surface findings, sign-off, commit.
6. At session close: classify all findings, write any `code-update` stub prompts, update the narratives README Index, extend W001's consolidated Narrative Cross-References block, evaluate methodology-log Entry 001 (or document the skip in the narrative-internal retro), append the narrative-internal retro, flip the narrative's `status:` to `accepted`.

---

## Document history

- **v0.1** (2026-04-29): Authored as foundation-refresh Phase 5 Item 1b session prompt. Adapts the Phase 5 Item 1a (narrative 002) prompt template to lived-code audit posture: orientation reads include `src/CritterBids.Participants/` code and M1-S4 / M1-S5 retros; "Heads-up sources of likely findings" reframes the lane mix toward `code-update` as a real lane (vs narrative 002's structural-impossibility framing). Three-Moment proposal at finer grain than narrative 001 Moment 1 is the principal departure. The Phase 5 Item 1a session's discipline (per-Moment disposition-tag-at-draft-time, fold-into-one-PR pattern, title-ambiguity check) carries forward as inherited working pattern. Em-dash hygiene language dropped per the memory clarification at narrative 002 close (the no-em-dash rule was intended for external prose only and does not apply to internal narrative authoring). The Phase 5 prompt §3.3's reference to "M1-S2 retro" for the BC scaffold was a small error; the actual scaffold retro is `M1-S4-participants-bc-scaffold.md`, which this prompt cites correctly.
