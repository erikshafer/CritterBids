# Prompts Workflow — Human Guide

> **Audience:** humans driving CritterBids development. This file is operational
> workflow, not agent instructions. **Agents should not load this file** — no
> session prompt in `docs/prompts/` lists it in its context-load section, and
> none should. Rule 9 in `AUTHORING.md` (context load is finite and listed) is the
> mechanism that keeps it out of agent context; this note is the convention
> that keeps it out intentionally.

This is the loop you run when you want a session prompt to become a merged PR.
It's deliberately boring — the interesting stuff lives in the prompts
themselves and in the code they produce. What matters here is that the loop
runs the same way every time so that drift is visible when it happens.

## The five-step loop

### 1. Open a fresh agent session in the repo root

Claude Code in Rider, GitHub Copilot custom agents (`@PSA` etc.), Claude Code
CLI, Cursor, Codex — any of them work. The prompt format is agent-agnostic.
Use whichever tool you're in the mood for; the prompt doesn't care.

Fresh session matters. Don't resume a session that was doing something else.
A clean context window is how you guarantee the agent is operating only from
the prompt and the files it lists, not from whatever you were chatting about
ten minutes ago.

### 2. Point the agent at the prompt file

Something like: *"Execute `docs/prompts/implementations/M1-S1-solution-baseline.md`"* or
*"Read `docs/prompts/implementations/M1-S1-solution-baseline.md` and complete the session it
describes."*

That's the entire instruction. Do not add context, do not add "and also do
X," do not clarify what you meant. The prompt is the complete brief — if
you're tempted to add instructions on top, stop and see step 5.

### 3. Let the agent do the work

The agent reads the prompt, loads the files the prompt lists in its
context-load section, does the work, and opens a PR. If the agent asks
clarifying questions mid-session, the right answer is usually "follow what
the prompt says" — not to answer the question directly. If the prompt
genuinely doesn't cover the question, that's a rule 7 situation: the agent
should flag and stop, and the prompt should be updated before the session
resumes.

### 4. Review the PR against the acceptance criteria

Every prompt has a checklist. Walk it. Each line is designed to be verifiable
in under a minute — file exists, test passes, package is pinned, endpoint
returns a specific status. If a line is hard to check, that's a signal the
acceptance criterion was badly written, and it goes on the retro list.

Things the review is **not** checking:
- Whether the code is clean (that's what the skill files and linters enforce)
- Whether you'd have written it differently (you're reviewing scope, not style)
- Whether it's the best possible approach (the prompt committed to an approach
  at authoring time; disagreement with the approach is a new prompt, not a
  review comment)

Things the review **is** checking:
- Every acceptance criterion is met
- Nothing in the "explicitly out of scope" section was touched
- The ten rules weren't violated (no code added to the prompt, no ad-hoc
  additions to scope, etc.)

### 5. Merge, retro, feed the template

After merge, spend five minutes on a retro. The questions are always the
same:

- Did the prompt give the agent everything it needed?
- Did anything surface mid-session that should have been in the prompt?
- Did any acceptance criterion turn out to be unverifiable or ambiguous?
- Did the template itself (in `AUTHORING.md`) feel like the right shape, or did
  you want sections it didn't have?
- Did any of the ten rules chafe?

If any answer is "yes, something's off," open a PR against
`docs/prompts/AUTHORING.md` with the change (or `docs/prompts/README.md` if the
gap is in the index/subdirectory layout rather than the template/rules). M1 was
the period where the template and the rules were expected to move; M2 onward
treats them as stable until something breaks.

Then draft the next session's prompt with whatever you learned baked in, and
start the loop again.

## The "don't add instructions in chat" rule

This is worth its own section because it's the most common way the workflow
quietly breaks.

You will be tempted, mid-session, to type something like "actually, also add
a health check endpoint" or "use version 8 instead of 7" or "skip the
Contracts project for now." **Don't.** Every ad-hoc addition in chat is a
piece of the session's brief that exists nowhere except your chat history —
which means six months from now when you're trying to reconstruct what was
actually asked for in M1-S1, the prompt file will be incomplete and you
won't know it.

The rule is: if you want to change what the session does, cancel the session,
edit the prompt file, commit the change, start a fresh session. The prompt
file is the durable record. Chat is ephemeral. When those two disagree, the
prompt file wins, which means the prompt file has to always be current.

Yes, this is slower in the moment. It is much faster six months from now.

## The retro loop is the whole point

M1 isn't really about landing the skeleton — the skeleton is a byproduct. M1
is about discovering what a good CritterBids session prompt looks like, by
running the loop enough times that the template stabilizes. By the end of M1
you should have a README and a ten-rules list that you trust, and a template
that fits how CritterBids sessions actually go. M2 will use them without
further modification until something breaks.

This is the same pattern as the skills layer: write the feature first, let
the code reveal the real patterns, extract the skill doc retrospectively.
Prompts work the same way. Write a few, run them, see what breaks, fix the
template. Don't try to get the template right before M1-S1 runs — you don't
have enough information yet.

## When this file should change

Update this file when the workflow itself changes — when you adopt a new
tool, when the review step gets a new checklist, when the retro cadence
shifts. Do not update it to add agent-facing guidance; index/subdirectory
guidance goes in `README.md`, prompt-authoring rules and templates go in
`AUTHORING.md`. The split is load-bearing: `README.md` and `AUTHORING.md` are
what agents read, `WORKFLOW.md` is what you read. Keep them separate and all
three stay useful.
