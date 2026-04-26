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

*(Empty as of this file's authoring. Entry 001 lands at the close of foundation-refresh Phase 2's narrative session, per the foundation-refresh prompt §4.7.)*

---

## Document history

- **v0.1** (2026-04-26): Authored as foundation-refresh Phase 1 Item 6. Lifts CritterCab's methodology-log v0.1 intro (what this file is, what it is NOT, when to write an entry, entry format) verbatim. Adapts the time-box trigger to "close of foundation-refresh Phase 2 or after the third entry, whichever comes first." Entries section starts empty per the foundation-refresh prompt; Entry 001 is authored at Phase 2 close.
