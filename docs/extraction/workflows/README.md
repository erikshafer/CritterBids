# Workflow Traces

One file per cross-BC business process. Each trace names the triggering event, the ordered hops with event/command at each step, the outcomes, and a process-level maturity tag.

| Process | File | Maturity |
|---|---|---|
| Publish a listing through to bidding open | [`publish-to-bidding-open.md`](./publish-to-bidding-open.md) | Implemented |
| Timed listing close (saga → outcomes) | [`timed-listing-close.md`](./timed-listing-close.md) | Implemented |
| Buy It Now terminal path | [`buy-it-now.md`](./buy-it-now.md) | Implemented |
| Proxy bidding (per-bidder-per-listing saga) | [`proxy-bidding.md`](./proxy-bidding.md) | Implemented |
| Settlement, post-sale obligations, notifications | [`post-sale-obligations.md`](./post-sale-obligations.md) | Partial — crosses into Planned-only territory |
| Flash session container flow | [`flash-session.md`](./flash-session.md) | Implemented |
