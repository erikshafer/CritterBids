# CritterBids Methodology Log

This file is an **append-only journal of cross-cutting methodology observations** that surface during sessions but don't fit cleanly inside any single session's retrospective. Each entry captures a pattern noticed *across* artifact layers, sessions, or methodology techniques: the kind of observation a per-session retro can't make because it would violate the retro's scope.

## What this file is

- A durable home for cross-cutting methodology learnings. Each entry is a small, dated observation paired with what it implies for future sessions.
- Append-only. Entries are not edited after writing; if an observation is later disconfirmed, a follow-up entry records the correction.
- Time-boxed pilot. Decision to keep, fold, or remove the file is revisited at the close of foundation-refresh Phase 2 or after the third entry lands, whichever comes first. The file is prepared to delete itself.

## What this file is NOT

- A replacement for per-session retrospectives. Workshop §12 retros and narrative retrospectives stay layer-bounded and stay where they are.
- An ADR backlog. ADRs capture architectural decisions; this log captures methodological observations that may *eventually* harden into ADRs but typically don't.
- A vision doc surrogate. The vision docs in `docs/vision/` capture *committed* methodology choices; this log captures *observed* patterns that may or may not become commitments.

## When to write an entry

Write an entry only when a session produces a cross-cutting observation that meets all three criteria:

1. **Spans** multiple artifact layers (e.g., a pattern visible in workshops *and* narratives) or multiple sessions.
2. **Wouldn't fit** inside that session's per-session retro without violating its scope.
3. **Predicts** something about how the methodology will or should evolve.

If no entry is warranted, no entry is written. Silence is fine; the file stays legitimate by being silent when there's nothing cross-cutting to say.

## Entry format

```
### Entry NNN: <one-line title> (YYYY-MM-DD)

**Trigger.** What session or artifact prompted the observation.

**Observation.** The cross-cutting pattern itself, in plain language.

**Implication.** What this predicts or changes for future sessions, and how a future entry would confirm or disconfirm it.
```

Numbers are zero-padded to three digits, mirroring narrative and workshop numbering.

---

## Entries

### Entry 001: Audit-floor heterogeneity is the norm for narrative authoring (2026-04-29)

**Trigger.** Foundation-refresh Phase 5 Items 1a-1d (the four backfill narratives: 002 Settlement, 003 Participants, 004 Selling, 005 Auctions). The four sessions spanned a deliberate range of audit-floor postures: narrative 002 was fully forward-spec (Settlement BC unshipped); narrative 003 was fully lived (Participants BC shipped end-to-end); narrative 004 mixed lived M2 with forward-spec M4-S2 (WithdrawListing prompt-backed but unshipped); narrative 005 mixed lived M3+M4-S1 with forward-spec M4-S5/S6 (session-start cascade with no prompt yet authored). At each session close the per-narrative retrospective recorded local observations; this entry captures the cumulative pattern that is visible only across the four.

**Observation.** Audit-floor heterogeneity — the practice of splitting a narrative's audit-floor by Moment, where each Moment audits against either lived code or a written spec depending on what is shipped — is the structurally expected mode for narrative authoring in any project that has work both shipped and planned. The narrative-authoring discipline does not require a uniform audit floor across a narrative; it requires per-Moment clarity about which audit-floor applies. The findings-lane mix follows from this: lived Moments produce `code-update` and `narrative-update` candidates routinely, with `document-as-intentional` flipping `code-update` candidates whenever the code's inline comments document the design choice (the code-comment-as-routing-evidence discipline narrative 004 refined); forward-spec Moments produce `workshop-update` candidates against the workshop or spec, with `code-update` structurally impossible for the forward-spec BC. The two postures coexist within a single narrative without friction. Five lane-mix outcomes were observed across the four backfills: narrative 002's 1+3+0+1 split, narrative 003's 0+0+2+0, narrative 004's 0+0+1+2, narrative 005's 0+0+0+0 (`narrative-update` + `workshop-update` + `code-update` + `document-as-intentional`). The mix varied with the audit-floor posture, exactly as the structural prediction implies.

**Implication.** Future narrative-authoring sessions should default to the mixed-posture pattern rather than treating fully-lived or fully-forward-spec as the canonical shape. Narrative-authoring prompts should explicitly identify per-Moment audit-floors at the prompt-author stage so the future session knows which lane mix to expect for each Moment. A future entry would confirm or disconfirm the prediction by examining: does the mix get unwieldy beyond a certain Moment-count or audit-floor-split-count? Does the mixed-posture pattern hold for narratives whose forward-spec base lacks an existing implementation prompt (narrative 005's M4-S5/S6 case had no prompt; narrative 004's M4-S2 had a prompt; the implementation-prompt-backed forward-spec posture proved easier to render because the spec was concrete)? Does zero-findings become more common as the project's narrative library grows and earlier narratives' findings establish the post-fix audit floor (narrative 005's zero-findings outcome was structurally a verification-of-prior-fix rather than a fresh audit)? The answers will inform whether mixed-posture remains the default or whether it carries hidden friction at scale, and whether implementation-prompt-backing should become a recommended precondition for forward-spec Moments.

---

## Document history

- **v0.1** (2026-04-26): Authored as foundation-refresh Phase 1 Item 6. Lifts CritterCab's methodology-log v0.1 intro (what this file is, what it is NOT, when to write an entry, entry format) verbatim. Adapts the time-box trigger to "close of foundation-refresh Phase 2 or after the third entry, whichever comes first." Entries section starts empty per the foundation-refresh prompt; Entry 001 is authored at Phase 2 close.
