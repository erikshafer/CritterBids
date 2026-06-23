---
name: dual-evaluation
description: Prepare a contested architectural or sanction/defer decision by commissioning two independent evaluations under deliberately different lenses (and ideally different models), then writing a comparison document that extracts convergence, divergence, and uniquely-caught deliverables for the human decision owner. Use when a decision is hard to reverse or scope-expanding and a single recommendation would under-inform it — e.g. "should we sanction this slice", "evaluate both options independently", "run a dual evaluation", or when Erik asks for independent counsel on a fork in the road.
---

# Dual Evaluation — Independent Counsel for Contested Decisions

> First lived use: the M8 ops-feed-completion sanction (2026-06-10) —
> `docs/research/ops-feed-completion-evaluation.md` (staff architect, Fable 5),
> `…-evaluation-es-specialist.md` (event-sourcing specialist, Opus 4.6),
> `…-evaluation-comparison.md` (comparison + recorded decision). Those three files are
> the reference implementation; this skill generalizes them.

## When to apply — and when not to

Use it when **all three** hold:

1. The decision is genuinely contested or hard to reverse (sanction-vs-defer, a scope
   expansion, a structural pivot) — the cost of a wrong call exceeds the ~session the
   method costs.
2. A recommendation is wanted, not just options — the output feeds a human decision,
   it does not make one.
3. Two *materially different* lenses exist for the question (pragmatic/observable vs
   structural/invariant; product vs platform; cost vs correctness).

**Exit ramp:** if during framing the evidence turns out to be one-sided, say so and skip
the ceremony. Precedent: the M8-S6b dispute-control decision was folded into the same
comparison doc as an Addendum *without* an evaluation pass, because the backend was
already fully proven — "no evaluation needed" is a legitimate, recordable outcome.

## The method

### 1. Frame once, neutrally

Write a single decision statement both evaluators receive **identically**: the question,
the 2–3 options, the background facts (lived code state, prior precedent, constraints),
and what kind of answer is wanted (recommendation + reasoning, not a survey). Do not
bake a preferred option into the framing — a tilted prompt produces two echoes, not two
evaluations. Name the decision owner and date.

### 2. Commission two independent evaluations

- **Different lenses, chosen to disagree about what matters.** The lived pair: a
  *staff architect* (modular-monolith guardian, conventions, pedagogy, "what does the
  operator see?") vs an *event-sourcing specialist* (first principles, topology
  invariants, "what guarantee is violated?"). Two flavors of the same reviewer is an
  echo chamber, not a second opinion.
- **Different models when available** (lived case: Codex Fable 5 + Codex Opus 4.6) —
  cheap extra decorrelation on top of the persona split.
- **True independence:** separate sessions/agents; neither sees the other's output or
  knows a second evaluation exists. Independence is the load-bearing property — step 3's
  convergence signal is worthless if the chains could have copied each other.
- Each evaluation is **self-contained**: decision framing restated, numbered evaluation
  points, an explicit recommendation, references into the lived code. It must stand
  alone as counsel even if the other evaluation were lost.

### 3. Write the comparison document

The comparison is its own artifact (`…-evaluation-comparison.md`), authored only after
both evaluations are complete. Its sections, in order:

1. **Header table** — file, lens, model per evaluation; decision owner; date.
2. **Where they agree (strong signal)** — for each agreement, show *both reasoning
   chains*. Convergence through different reasoning is the method's whole product;
   agreement restated in the same words is suspicious, not reassuring.
3. **Where they diverge (the interesting differences)** — each divergence gets a
   **Significance** note: what turns on it, and who should care.
4. **"Anything only one evaluation caught?"** — a first-class question, not a footnote.
   These uniquely-caught items are often real deliverables that survive *regardless* of
   which overall recommendation wins (lived case: A's topology-test invariant and
   `onreconnected` reconciliation; B's precise 10-event gap count — all three entered
   the slice prompt).
5. **Summary for the decision** — a question/answer table the owner can act on, ending
   with the residual open questions only the human can close.
6. **Decision (owner, date)** — left for the human; recorded *in this file* when made,
   naming which pieces of each evaluation are accepted. Later decisions on the same
   topic may land as dated Addenda (the M8-S6b Decision-2 precedent).

### 4. Route the outcome

The comparison's Decision section is the citable record: downstream docs (milestone
Document History, slice prompts, STATUS) cite it rather than restating the reasoning.
Uniquely-caught deliverables go into the resulting prompt's scope explicitly — they are
the easiest thing to lose between decision and execution.

## Artifact conventions (CritterBids)

| Artifact | Path |
|---|---|
| Evaluation (first/default lens) | `docs/research/{slug}-evaluation.md` |
| Evaluation (second lens) | `docs/research/{slug}-evaluation-{lens}.md` |
| Comparison + recorded decision | `docs/research/{slug}-evaluation-comparison.md` |

All three are committed; `docs/research/README.md` indexes them.

## Pitfalls

- **Same lens twice** → two echoes; the comparison has nothing to compare.
- **Evaluator 2 sees evaluator 1** (or the framing leaks "the other reviewer said…")
  → independence broken; convergence becomes noise.
- **A tilted framing** ("should we do this obviously-good thing?") → both evaluations
  inherit the tilt; write the decision statement as the *undecided* owner would.
- **Treating unanimity as the decision** → the lived case was unanimous on *what* and
  split on *when* (separate slice vs folded into S7); the human closed it. Residual
  disagreements are the owner's, always.
- **Skipping the comparison** → two stapled documents make the owner do the synthesis;
  the comparison *is* the deliverable, the evaluations are its inputs.
- **Running it for everything** → the method costs about a session; the exit ramp
  exists so cheap, evidence-one-sided decisions get an Addendum, not a ceremony.

## See also

- `docs/research/ops-feed-completion-evaluation-comparison.md` — the reference
  comparison, including a recorded Decision and a no-evaluation-needed Addendum.
- `docs/decisions/README.md` — when the *outcome* warrants an ADR, the comparison doc
  is the ADR's evidence trail, not a substitute for it.
- `docs/prompts/README.md` — the resulting slice prompt carries the accepted
  deliverables; the prompt cites the comparison's Decision section.
