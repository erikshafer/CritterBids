# M2-S8: Skills Documentation + M2 Milestone Retrospective

**Milestone:** M2 — Listings Pipeline
**Session:** S8 of 8 (final)
**Prompt file:** `docs/prompts/M2-S8-retrospective-skills-m2-close.md`
**Agent:** @PSA
**Baseline:** 42 tests passing (Listings: 4, Selling: 30, Participants: 6, Api: 1, Contracts: 1) · `dotnet build` 0 errors, 0 warnings

---

## Goal

Close M2 with documentation. No code changes, no new tests, no new projects. This session authors
one new skill document, updates one existing skill document, and writes the M2 milestone
retrospective.

At session close M2 is fully complete: implementation done, skills up to date, retrospective written.

---

## This session is documentation-only

Do not modify any `.cs` file, `.csproj` file, `.slnx` file, `Program.cs`, or any test file.
Do not run `dotnet build` or `dotnet test` — the build is already clean.
If you find yourself about to write code, stop and re-read this prompt.

---

## Required reading — load before authoring anything

Read all of the following before writing a single word of documentation. The content of these
files is the primary source material.

| File | Purpose |
|---|---|
| `docs/retrospectives/M2-S5-slice-1-1-create-draft-listing-retrospective.md` | Source material for `domain-event-conventions.md` — domain events established here |
| `docs/retrospectives/M2-S6-slice-1-2-submit-listing-retrospective.md` | Source material for `domain-event-conventions.md` — naming collision pattern, slim domain event vs rich contract |
| `docs/retrospectives/M2-S7-listings-bc-read-paths-retrospective.md` | Source material for `adding-bc-module.md` updates — two specific gaps identified |
| `docs/skills/adding-bc-module.md` | File to update — read it fully before editing |
| `docs/milestones/M2-listings-pipeline.md` | Source material for M2 milestone retrospective — goals, exit criteria, session breakdown |
| `docs/retrospectives/M2-S1-marten-bc-isolation-adr-retrospective.md` | M2 arc source material |
| `docs/retrospectives/M2-S2-selling-bc-scaffold-retrospective.md` | M2 arc source material |
| `docs/retrospectives/M2-S3-registered-sellers-consumer-retrospective.md` | M2 arc source material |
| `docs/retrospectives/M2-S4-north-star-alignment-retrospective.md` | M2 arc source material |

Read all nine files. Do not skim.

---

## Deliverable 1: Update `docs/skills/adding-bc-module.md`

Two targeted updates only. Do not restructure the document, add new sections, or modify any
content that is not explicitly called out below.

### Update 1 — Api project reference checklist item

**Where:** The "Integration" block of the checklist section (near the end of the file).

**Add this item** after `Program.cs calls services.AddBcModule()`:

```
- [ ] `CritterBids.Api.csproj` — add `<ProjectReference Include="..\..\src\CritterBids.{BcName}\CritterBids.{BcName}.csproj" />` alongside the existing BC references
```

**Why:** S7 identified that when `Program.cs` references `typeof(SomeNewBcType)`, the Api project
requires a project reference to the new BC. This is implicit but was discovered as a build failure.
Adding it to the checklist prevents repeating the same discovery.

### Update 2 — Alba `GetAsJson` does not exist

**Where:** The "Test fixture for Marten BCs" section, or wherever test query patterns are discussed.
If there is no existing Alba API guidance in the file, add a note directly to the test fixture
code example or to the section's introductory prose.

**Add this note:**

> `IAlbaHost.GetAsJson<T>()` does not exist in Alba 8.5.2. Use the Scenario pattern instead:
> ```csharp
> var result = await Host.Scenario(s =>
> {
>     s.Get.Url("/api/some-endpoint");
>     s.StatusCodeShouldBe(200);
> });
> var response = await result.ReadAsJsonAsync<T>();
> ```
> This is the only correct read pattern for JSON responses in Alba 8.x. Prompts and docs must
> not reference `GetAsJson`.

Place this note near the test fixture code example so it is visible in context.

---

## Deliverable 2: Author `docs/skills/domain-event-conventions.md`

Create this file from scratch. It does not exist yet. It is listed as `🔴 Not yet written` in
`docs/skills/README.md` — update the README status entry from `🔴 Not yet written` to `✅ Complete`
as part of authoring this file.

### Audience

Agents implementing slices in CritterBids. A reader should finish this document able to name,
place, and structure any domain event they need to add — without having to read a BC retro to
discover the conventions.

### Required content

The file must cover all of the following topics. Source material for each is in the S5 and S6
retrospectives. Write from the patterns as implemented, not as aspirational guidelines.

**1. Naming**
- Past-tense verb + noun (e.g., `DraftListingCreated`, `ListingSubmitted`, `ListingPublished`)
- No `Event` suffix — the namespace and usage context make the type role obvious
- Describe the fact that happened, not the command that caused it
- Noun is the aggregate or primary entity; verb is the state transition or fact recorded

**2. File and namespace placement**
- One event per file
- File named identically to the type (e.g., `ListingSubmitted.cs`)
- Namespace: `CritterBids.{BcName}` — the BC that owns the event stream
- Never placed in `CritterBids.Contracts` — domain events are internal to the owning BC

**3. Type shape**
- `sealed record` — no exceptions
- Properties are `init`-only positional or named — choose consistency with the rest of the BC
- Aggregate ID as first property
- `DateTimeOffset` for all timestamps — never `DateTime`
- `IReadOnlyList<T>` for collections — never `List<T>`
- No navigation properties, no methods, no behavior

**4. Aggregate ID field naming convention**
- `{AggregateTypeName}Id` — e.g., `ListingId` for a `SellerListing` aggregate
- Not `Id` alone — the identifier in the event should be unambiguous when events are read
  in isolation (e.g., in projections or handlers)

**5. Slim domain events vs rich integration contracts**

This is one of the most important patterns. Document it explicitly:
- Domain events carry only the data needed to reconstruct aggregate state
- Integration contracts (in `CritterBids.Contracts`) carry the full payload for all downstream consumers
- Example from M2: `CritterBids.Selling.ListingPublished` carries only `ListingId` + `PublishedAt`;
  `CritterBids.Contracts.Selling.ListingPublished` carries 13 fields
- Rationale: keeps the event stream compact; prevents downstream BC coupling to aggregate internals;
  allows the contract to evolve independently from the domain event

**6. Marten event type registration**

Every domain event that appears in a Marten event stream must be registered in the BC's
`ConfigureMarten()` call:

```csharp
opts.Events.AddEventType<ListingSubmitted>();
opts.Events.AddEventType<ListingApproved>();
```

This is required when `UseMandatoryStreamTypeDeclaration` is set. Omitting this causes silent
`null` returns from `AggregateStreamAsync<T>` for streams that include unregistered event types.

**7. Naming collisions between domain and integration events**

When a domain event and an integration contract share the same simple name (e.g., both called
`ListingPublished` in different namespaces), use a `using` alias in the handler file:

```csharp
using ContractListingPublished = CritterBids.Contracts.Selling.ListingPublished;
```

In `Program.cs` or other host-level files, use fully qualified names rather than aliases to
make the source explicit.

**8. Enum types that appear in events**

Enum types used in domain events must be defined in the BC's own namespace, not in
`CritterBids.Contracts`. If a downstream consumer needs to interpret a format or type field,
the integration contract should carry a `string` representation — not the enum. Cross-BC enum
sharing is not permitted.

### Format guidance

Use the same density principle as existing skill files. Every sentence earns its place. Lead
with code examples and annotate them — do not write code-free prose paragraphs. Where the
convention can be shown in a code snippet, show it. Where it requires explanation, keep the
explanation to 2–3 sentences maximum.

Include a "Related skills" section at the bottom pointing to:
- `marten-event-sourcing.md`
- `integration-messaging.md`
- `adding-bc-module.md`

---

## Deliverable 3: `docs/retrospectives/M2-listings-pipeline-retrospective.md`

Write the M2 milestone retrospective. This is the capstone document for the entire milestone.

### Required sections

**Header block**
- Date, milestone, sessions span (S1–S8), author

**Exit criteria status**

Walk each exit criterion from `docs/milestones/M2-listings-pipeline.md` §1 and mark it:

| Exit criterion | Status |
|---|---|
| Solution builds clean | ✅ / ❌ |
| ... | ... |

All criteria should be ✅ at M2 close.

**Session-by-session summary**

A concise table:

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| S1 | ... | ✅ | ... |
...

Draw from each session retrospective for the deviation column. Keep each cell to one line.

**Cross-BC integration map**

Reproduce or summarize the integration map from `M2-listings-pipeline.md` Appendix with a
confirmation that both integrations are verified:

```
Participants ──► SellerRegistrationCompleted ──► Selling   (RegisteredSellers projection)  ✅
Selling      ──► ListingPublished            ──► Listings  (CatalogListingView projection)  ✅
```

**Test count at M2 close**

| Project | Count | Type |
|---|---|---|
| `CritterBids.Listings.Tests` | 4 | Integration |
| `CritterBids.Selling.Tests` | 30 | Mixed |
| Existing (Participants, Api, Contracts) | 8 | Integration |
| **Total** | **42** | |

**Key decisions made in M2**

List each ADR authored or resolved in M2, with a one-sentence outcome:

| ADR | Decision |
|---|---|
| 008 | ... |
| 009 | ... |
| 010, 011 (if applicable) | ... |

**Key learnings — cross-session patterns**

Synthesize the top 5–7 learnings that apply across more than one session or that future milestones
should carry forward. Do not repeat session-local learnings that are already covered in individual
retros. Focus on patterns, anti-patterns, and process observations that generalize.

**Technical debt and deferred items**

List items explicitly deferred out of M2 with the session they were parked in and the target
milestone:

| Item | Deferred in | Target |
|---|---|---|
| `[WriteAggregate]` stream-ID convention for `SubmitListing` | S6 | When HTTP endpoint added |
| RabbitMQ routing in BC modules (vs Program.cs) | S5, S6 | Deferred — architectural refactor |
| `ListingFormat` enum in Contracts | S6 | S8 skills pass (now this session) — document in conventions |
| UUID v7 ADR promotion gates | M1, M2 | M3 re-evaluation |
| Named Polecat stores | M1, M2 | When Settlement or Operations BC arrives |

Add any additional items surfaced across the session retros.

**What M3 should know**

A short paragraph (3–5 sentences) summarizing what state the codebase is in and what M3 inherits.
Include: current test count, BC count, integration event count, and any known fragility areas.

---

## Required last act: update `CURRENT-CYCLE.md`

Update `CURRENT-CYCLE.md` at the solution root as the final act of this session. Record:
- Session: M2-S8 (final)
- Date: today
- Status: M2 complete
- Test count: 42
- Next: M3 planning (or whatever is up next)

---

## Commit sequence

Documentation-only session — one commit per deliverable.

1. `docs: update adding-bc-module.md with Api project reference and Alba GetAsJson gap` (Deliverable 1)
2. `docs: author domain-event-conventions.md skill; update skills README status` (Deliverable 2)
3. `docs: write M2 listings-pipeline milestone retrospective` (Deliverable 3)
4. `docs: update CURRENT-CYCLE.md — M2 complete` (final act)

---

## Session close checklist

- [ ] `docs/skills/adding-bc-module.md` — `CritterBids.Api.csproj` project reference item added to Integration checklist
- [ ] `docs/skills/adding-bc-module.md` — Alba `GetAsJson` absence documented with correct Scenario pattern
- [ ] `docs/skills/domain-event-conventions.md` — file created, all 8 topics covered
- [ ] `docs/skills/README.md` — `domain-event-conventions.md` status updated from 🔴 to ✅
- [ ] `docs/retrospectives/M2-listings-pipeline-retrospective.md` — file created, all required sections present
- [ ] `CURRENT-CYCLE.md` — updated as final act
- [ ] `dotnet build` was **not** run (documentation session — no code changes)
- [ ] No `.cs`, `.csproj`, `.slnx`, or `Program.cs` files were modified
- [ ] All 4 commits made atomically per the commit sequence above
