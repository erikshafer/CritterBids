# Un-stale the `wolverine-signalr` skill against ADR-023 — Retrospective

**Date:** 2026-06-07
**Prompt:** `docs/prompts/handoffs/wolverine-signalr-unstale-handoff.md`
**Agent:** Claude Code (Opus 4.8, interactive mode)
**Branch:** `docs/wolverine-signalr-unstale-adr023`
**Commit:** `1839e42`

This retro records a focused doc-hygiene fix driven by a handoff, not a milestone slice. It anchors
to the handoff's stale-spot list and acceptance criteria, not to an `M{N}-S{N}` prompt. Precedent for
a non-slice retro: `business-extraction-handoff-retrospective.md`, `foundation-refresh-phase-*`.

---

## Baseline

- `docs/skills/wolverine-signalr/SKILL.md` was internally contradictory: a `## CloudEvents type
  format` section described the Wolverine SignalR *transport* (path a — CloudEvents envelope,
  `cloudEvent.type`, `.split(".").pop()`, a `WolverineMessageNaming` type-shape table), while the
  rest of the file correctly described plain-`Hub` + direct `IHubContext` push (path b).
- ADR-023 (Accepted 2026-05-29) chose path (b) and **explicitly rejected** path (a): the raw
  notification record is the `ReceiveMessage` payload, no envelope.
- The client companion `.claude/skills/signalr/SKILL.md` (authored prior session) was already
  path-(b)-correct and actively warned readers that the server skill *still* carried the stale
  snippet — three such references.
- Lived client code `client/bidder/src/useBiddingHub.ts` already documents the path-(b) raw-record
  contract in comments. ADR ledger sat at 025.

## Items completed

| Item | Description |
|------|-------------|
| H1 | Replaced `## CloudEvents type format` with `## Wire contract — raw notification record (ADR-023)`; deleted the rejected `cloudEvent.type` / `.split(".")` client snippet. |
| H2 | Deleted the `WolverineMessageNaming` type-shape table (decision D1). |
| H3 | Reframed the marker-interface section as documentation / future-door only — under path (b) the interfaces carry no routing behaviour. |
| H4 | Corrected two `## Common pitfalls` entries: the envelope one, and the "let Wolverine route them" one that contradicted the direct-push decision. |
| H5 | Refreshed frontmatter (dropped `cloudevents` tag; rewrote `description`), intro blockquote, when-to-apply bullet, and read-upstream line. |
| H6 | Updated the client skill's three references to the now-fixed server snippet so they describe agreement, not staleness, keeping the two skills consistent. |

## D1 — the `WolverineMessageNaming` type-shape table

- **Resolved: delete** (handoff's recommended option, confirmed with Erik before finalizing).
- **Why delete over relocate-and-fence.** The table is accurate generic knowledge *for path (a)*,
  which CritterBids does not use. Correct-but-inapplicable content is harder for a reader to discount
  than wrong content — a frontend dev cannot tell from the table that it never applies to a hub mapped
  with `app.MapHub<BiddingHub>(...)`. ADR-023's own problem statement is that the transport pushes to
  `IHubContext<WolverineHub>`, a hub the application never maps. The `WolverineFx.SignalR` future-door
  (Option C) stays documented in ADR-023 itself, so deleting the table loses no decision record.

## H6 — keeping the two skills consistent

The handoff named the client skill "already correct," but its three staleness warnings became false
the moment H1 landed: a reader sent to the server skill expecting a stale snippet, finding none, is a
new inconsistency. The handoff's own constraint ("the two must tell the same story; cross-link them")
therefore *required* editing the client skill too. The edits were minimal — turning "the backend
skill still contains a stale snippet" into "both skills agree per ADR-023," and generalizing the
"don't copy the backend snippet" pitfall to "don't copy any `cloudEvent.type` / `.split(".")`
snippet."

## Scope held — what was deliberately not done

- **No client message-type discrimination scheme invented.** Under path (b) there is no envelope
  `type`, so how a client tells one record from another on the single `ReceiveMessage` method is
  genuinely unresolved — that discriminator plus its Zod schema is M8-S3 work, recorded in ADR-014
  (not yet authored). The server skill now states the contract and defers discrimination to the
  client skill + ADR-014, as the handoff's don't-over-reach boundary demanded.
- **Path-(b)-correct sections left untouched:** Lived Relay update (M6), the `IHubContext`/`SendAsync`
  handler example, group keys, ADR-024 hub auth, request/reply / JSON / group-change postures, and
  testing posture.

## Test / build state

- Docs-only change — two `.md` files, no `.cs` / `.csproj` / build files touched. The CI path-filter
  skips the build and integration-test jobs for a doc-only diff, so no `dotnet build` / `dotnet test`
  is owed. The handoff's §5 constraint says this explicitly; the commit body records the rationale.
- Residual-envelope sweep (handoff §2.4): `grep -i "cloudEvent|CloudEvents|envelope|\.split|\.data|\.type|kebab"`
  over `docs/skills/wolverine-signalr/SKILL.md` returns only **negations** — every remaining mention
  tells the reader the envelope does *not* exist. Zero instructions to read `cloudEvent.type` /
  `cloudEvent.data` or to `.split(".")` remain.

## Key learnings

1. **Correct-but-inapplicable documentation is more dangerous than wrong documentation.** A reader
   can dismiss an obviously-wrong snippet, but accurate generic knowledge framed as current behavior
   (the `WolverineMessageNaming` table) silently re-imports the exact confusion an ADR resolved.
   Deleting it was safer than fencing it.
2. **A "fix one file" handoff can have a transitive consistency obligation.** The client skill's
   warnings *about* the server skill became stale the instant the server skill was fixed. When two
   docs reference each other's state, editing one to match an authority can require editing the
   other to stop describing the old state.
3. **The ADR is the authority, the skill is downstream.** Both SignalR skills now cite ADR-023 as the
   tie-breaker so a future drift resolves to the decision record, not to whichever skill was read
   first.

## Verification checklist (handoff §7 acceptance criteria)

- [x] No remaining text tells the client to read `cloudEvent.type` / `cloudEvent.data` or
      `.split(".").pop()` (grep-confirmed: negations only).
- [x] Wire contract stated correctly — raw notification record on `ReceiveMessage`, no CloudEvents
      envelope (ADR-023 path b), with the ADR cited.
- [x] `## Common pitfalls` envelope entry corrected (no longer instructs the wrong behavior).
- [x] Client message-type discrimination deferred to `.claude/skills/signalr/` + ADR-014, not invented.
- [x] D1 resolved with Erik; naming table deleted.
- [x] Server and client SignalR skills cross-link and agree.
- [x] Path-(b)-correct sections (Lived Relay, handler example, group keys, ADR-024 auth, testing)
      left intact.
- [x] On a branch (`docs/wolverine-signalr-unstale-adr023`), not `main`; commit body notes the
      doc-only / no-test rationale.

## Spec delta — landed?

No spec consequence. This was a doc-hygiene chore aligning a skill file to an already-Accepted ADR
(023). No narrative, workshop, or ADR was authored or amended; the `wolverine-signalr` skill is not a
spec under ADR 020. ADR-023 was the authority enforced, not changed.

## What remains / next session should verify

- **ADR-014 (SignalR integration pattern — Provider + `useListen` + cache bridge + message-type
  discriminator) is still unauthored.** Both SignalR skills point a reader there for client
  discrimination; the pointer dangles until M8-S3 authors it. Out of scope here, tracked by M8-S3.
- **Frontend CI is still unwired** (existing TODO, `project_frontend_ci_not_wired`): `client/**` and
  `.claude/**` doc changes skip all jobs via the path filter. This change rode that gap intentionally
  (docs-only), but the gap itself is earmarked M8-S7 housekeeping.
