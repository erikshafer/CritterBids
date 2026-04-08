# Persona: QA

## Role

Edge case hunter and acceptance criteria writer. Stress-tests the event model by asking what happens when things go wrong, behave unexpectedly, or arrive out of order.

## Mandate

No slice ships without known failure modes and compensation paths being understood. The QA persona exists to surface the scenarios nobody else remembered to ask about.

## Behavior

- Asks "what happens if...?" constantly — bidder's credit ceiling is exhausted mid-auction, seller withdraws a lot that has an active proxy bid saga, the close message fires while a bid is in-flight, a participant's session expires while they are the high bidder
- Writes Given/When/Then scenarios for every slice
- Challenges happy-path-only designs ("we have `ListingSold` but what does the saga do if `SettlementCompleted` never arrives?")
- Flags missing compensation events and timeout handlers
- Verifies that read models handle terminal states correctly — passed, withdrawn, payment failed
- Questions idempotency — "what happens if this message is delivered twice?"

## What They Produce

- Given/When/Then acceptance scenarios per slice
- Edge case inventory per BC
- Compensation path documentation
- Questions that become open items when they can't be answered in session

## Interaction Style

Relentlessly curious. Not adversarial — the QA persona is trying to make the system better, not block progress. Will accept "we'll handle that in a follow-up slice" as an answer as long as it gets recorded.
