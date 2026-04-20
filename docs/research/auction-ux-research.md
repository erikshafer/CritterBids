# Auction UX Research — CritterBids

**Status:** Draft — Research Phase (Milestone RF-2)
**Owner:** Erik Shafer
**Last Updated:** 2026-04-19
**Companion doc:** `docs/research/frontend-stack-research.md`

---

## 1. Purpose & Position

This document captures research into the user experience and user interface patterns that characterize successful online auction platforms, with the goal of informing what CritterBids should build when it moves from stack selection (covered in `frontend-stack-research.md`) to actual product work. The frontend stack research answered "which libraries do we use"; this document begins to answer "what do we build with them."

The scope is deliberately descriptive rather than prescriptive. No component decisions are being finalized here. What this document does is establish a shared vocabulary, surface patterns that are clearly industry-standard versus clearly proprietary, identify failure modes and dark patterns that CritterBids as a reference architecture must avoid, and set up a follow-on milestone that can move to concrete UI specification with confidence.

CritterBids has two distinct auction formats (Timed and Flash) plus Buy-It-Now. The research covers all three, with particular depth on the live bidding patterns that make Flash sessions viable as a conference demo.

---

## 2. Project Context

Relevant constraints from earlier decisions:

- **Two auction formats plus Buy-It-Now.** Timed auctions run over days, eBay-style. Flash auctions run in short session windows (30 seconds to a few minutes per lot) and are the primary live-demo vehicle. Buy-It-Now provides a fixed-price path for sellers who do not want an auction.
- **Mobile is the primary form factor.** Conference audiences primarily use phones; tablets and laptops follow.
- **Event-sourced backend.** Every bid, timer reset, outbid, and settlement is an event in Marten. Projections provide snapshots. Reconciliation on reconnect replays from a projection, not from a bespoke sync protocol.
- **Two frontend applications.** `critterbids-web` for participants (bidders, sellers, buyers) and `critterbids-ops` for staff (auction operators, admin controls). ADR 012 established both as Vite SPAs.
- **Real-time transport.** SignalR over WebSockets, with HTTP fallback. Redis backplane when scaled beyond one .NET instance.
- **Reference-architecture framing.** CritterBids is a showcase first and a product second. Patterns should be legible, substitutable, and honest. Gimmicks, dark patterns, and novelty for its own sake work against the showcase mission.

---

## 3. Scope

### 3.1 In Scope

- Page typology across buyer, seller, and staff surfaces.
- Component-level UX patterns (bid button, timer, price display, bid history, outbid notification, and related primitives).
- Real-time interaction patterns (optimistic updates, cache reconciliation, reconnection UX, connection-state surface).
- Mobile-first considerations (thumb zones, bottom sheets, sticky bid bars, swipe gestures, safe areas).
- Trust, transparency, and information-density principles.
- Identification of dark patterns common to commerce and auction UIs, with an explicit stance that CritterBids does not use them.

### 3.2 Out of Scope

- Visual design tokens (colors, typography, spacing, iconography).
- Copywriting and microcopy.
- Live video streaming UX (Flash format may or may not include video; that is a separate product decision).
- Seller onboarding and KYC flows.
- Formal accessibility audit (WCAG compliance is assumed as a requirement but the audit itself is its own milestone).
- Internationalization and localization.
- Merchandising, recommendations, and discovery algorithms.

---

## 4. Constraints & Guiding Principles

1. **The backend is the authority.** The UI is a view onto projections and a dispatcher of commands. When the UI disagrees with the server, the server wins. Every optimistic update has an explicit reconciliation path.
2. **Showcase honesty.** No fake scarcity, no artificial urgency, no fabricated social proof. The auction is already exciting; manufactured pressure would dilute the genuine tension and contradict the reference-architecture mission.
3. **Mobile-first is a hard rule.** If a pattern works on desktop but fails on a one-handed phone grip, it fails.
4. **Liveliness matters.** A bidding UI that lags, stutters, or feels unsure of its own state breaks the demo. Real-time feedback is a functional requirement, not a polish item.
5. **Transparency over cleverness.** Bidders should be able to explain what they are seeing and why. Hidden state is a correctness hazard and an accessibility hazard.
6. **Proportional information density.** Flash sessions want minimal chrome and maximum focus on the bid moment. Timed listings want more detail (condition notes, seller info, history) because the user has time to read.

---

## 5. Reference Platforms Studied

The research draws on publicly documented behavior from the following platforms. Each contributes distinct lessons.

**Whatnot.** Live livestream auctions, 30-second to 1-minute lot durations, swipe-to-bid on mobile, hidden max-bid ("Secret Max Bid"), sudden-death and extended-bidding variants. The Whatnot Engineering blog on Medium documents several UX decisions with reasoning, which makes it an unusually rich source. Particularly relevant to CritterBids' Flash format.

**eBay.** The archetype for timed auctions. Establishes the dominant patterns for countdown timers, proxy bidding, watchlist, sniping behavior, and the listing-detail-page anatomy. Also a case study in how *not* to handle client-clock drift in a countdown timer (see community threads where users report the timer mis-rendering in the final seconds).

**Artsy.** High-value live auctions with a human auctioneer in the room, staff operator inputting floor bids on behalf of online bidders, and bidders on their phones participating globally. Artsy Engineering's "The Tech Behind Live Auction Integration" is a frequently-cited case study on the topology of operator + bidder interfaces. Relevant because the `critterbids-ops` dashboard has a similar operator role.

**StockX.** Bid/ask marketplace rather than pure auction, but its price-graph and "bid below ask / ask below bid" mechanics inform how price information can be visualized in a dense, data-rich UI.

**HiBid, Proxibid, iRostrum, Bidpath.** White-label auction software vendors. Their public product pages reveal the baseline feature set expected of any serious auction platform: watchlist, auto-bid, bid history, seller profiles, search and filtering, timer-with-extensions, checkout, and invoicing.

**Baymard Institute and Nielsen Norman Group.** Not auction-specific, but their e-commerce UX benchmarks establish the product-detail-page conventions that auction listings inherit (image gallery, description, price, CTA, trust signals, reviews).

---

## 6. Page Typology

Across the platforms studied, an identifiable set of page types recurs. CritterBids will need some subset of these. Each is described with the primary job it does, the must-visible information, and the primary actions.

### 6.1 Home / Discovery Feed

The entry point. Job: help the user find something to bid on or watch.

**Must show:** live-now auctions (or soon-starting shows, in Whatnot-style live platforms), featured categories, items the user is watching or pre-bidding on, recently viewed. On mobile, typically a vertical scroll of cards.

**Primary actions:** enter a live session, open a category, search, tap a featured listing.

**Notes:** Whatnot emphasizes the "live now" rail above all else because live attendance drives the business. eBay emphasizes search and category browsing because most users arrive with intent. CritterBids' discovery surface can start minimal; the demo flow will often bypass it via QR code deep-linking directly into a Flash session.

### 6.2 Category / Search Results

A paginated or infinite-scroll list of listings matching filters.

**Must show, per card:** primary image, title, current bid or Buy-It-Now price, time remaining, bid count, watch toggle. Baymard's research on product listings emphasizes that each card must expose the *same* attributes in the *same* place so users can scan and compare. Inconsistency across cards is a known usability failure.

**Primary actions:** open a listing, toggle watch, refine filters.

**Notes:** For auctions specifically, "time remaining" is a first-class attribute and should be visually distinct. Sorting by "ending soonest" is the most-used auction sort on eBay and should be a default option.

### 6.3 Listing Detail Page (LDP)

The page a bidder inspects before placing a bid on a timed auction or Buy-It-Now item. Analogous to a product detail page in general e-commerce.

**Must show, above the fold:** primary image with access to the gallery, title, current bid (or Buy-It-Now price), time remaining with countdown, bid count, primary CTA (Place Bid or Buy Now), watch toggle. Nielsen Norman Group and Baymard both emphasize the "inverted pyramid" principle: most important information first, progressive disclosure below.

**Must show, below the fold (or in tabs):** full description, item condition, seller card with ratings and trust signals, shipping information, buyer's premium and taxes disclosed up-front, bid history, Q&A if applicable, related listings.

**Primary actions:** place bid, set max bid, watch, ask a question, view seller profile, view bid history.

**Notes:** Auction LDPs have more structural similarity to e-commerce PDPs than to live-session UIs. The key auction-specific additions are the countdown timer (prominent and server-synced), the bid history (often collapsed by default), and the max-bid affordance. Buyer's premium disclosure is a trust-and-legal item; hiding it until checkout is a dark pattern per FTC guidance.

### 6.4 Live Auction / Flash Session

The defining CritterBids experience. A high-velocity, low-chrome page where one item is currently under the hammer, a countdown is visible, and bids stream in real-time.

**Must show:** current high bid (large, animated on change), who is winning (username or anonymized handle, matching Whatnot's "smekicks is winning!" pill pattern from their engineering blog), countdown timer, primary bid button, custom-bid option, next valid bid amount pre-calculated and visible, viewer count, recent bid activity (at least the last 3-5 bids), current item image and short title, what lot is coming next if known.

**Primary actions:** place bid at next increment (swipe on mobile, click on web), place custom bid, set max bid, follow seller.

**Notes:** The Whatnot model uses swipe-to-bid on mobile because it reduces misfire risk on a small tap target under time pressure. The web equivalent is a tap-to-bid with the next increment amount on the button face (e.g., "Bid $32"). Sudden-death variants do not extend the timer; regular variants extend it by 10 seconds on any bid in the final 10 seconds. CritterBids Flash should specify which variant per session.

This is also the page where the ops dashboard and the participant view diverge most. Operators see seller-facing controls (start lot, end lot, withdraw, announce) and a projection of the same live state participants see. The shared-state-different-view framing aligns with the event-sourced backend naturally.

### 6.5 Bid Confirmation Flow

Some platforms confirm bids with a modal (eBay's legacy pattern); others submit directly on tap (Whatnot). Both have tradeoffs.

**Modal confirmation pros:** reduces accidental bids, lets user see next-valid-bid and total (bid + premium + tax) before committing.

**Modal confirmation cons:** adds friction during fast-paced bidding. eBay's timer-in-modal UX has known failure modes where the modal timer drifts from the listing page timer, causing users to miss the window.

**Direct submission pros:** matches the pace of live sessions.

**Direct submission cons:** requires bulletproof mis-tap prevention (swipe gestures are the standard mitigation) and an unmistakable visual feedback loop.

**Notes:** CritterBids should use direct submission with swipe-to-bid (mobile) or a large tap-to-bid button (web) for Flash sessions, and modal confirmation only for custom bids where the user has explicitly deviated from the default increment. For Timed auctions, modal confirmation is appropriate because the pacing allows it.

### 6.6 Checkout / Post-Win Flow

After a win, the user needs to pay, provide shipping, and understand fees.

**Must show:** final total broken down (winning bid, buyer's premium, shipping, taxes), payment method, shipping address, order summary.

**Primary actions:** pay, review, confirm.

**Notes:** Buyer's premium is the single most common source of post-win surprise in the auction world. It must be visible on the LDP, in the bid confirmation (if one exists), and broken out here. The FTC considers hidden fees a dark pattern. CritterBids should display the premium prominently at every step.

### 6.7 My Activity / My Bids / Watchlist / Purchases

A user's dashboard of active and past engagement.

**Must show:** active bids (winning / outbid status visible), watched items, pre-bids placed, won items pending payment, completed purchases, sold items (if seller).

**Primary actions:** jump back to a live listing, increase a max bid, pay for a won item, view a past order.

**Notes:** The "outbid" status indicator is critical. eBay surfaces this prominently and notifies via email and push. Artsy does the same plus SMS. Whatnot uses push notifications for followed sellers and won auctions.

### 6.8 Seller Dashboard

Seller-facing view for managing listings, live shows, and sales.

**Must show:** active listings, scheduled live shows, revenue and payout history, bid activity on owned items, messages from potential buyers.

**Primary actions:** create listing, start a live show, adjust reserve, withdraw listing, respond to questions.

**Notes:** Out of scope for deep analysis here, but a necessary page for CritterBids to demonstrate the full bounded-context story (Selling BC has a user-facing surface).

### 6.9 Ops / Staff Dashboard (`critterbids-ops`)

Internal staff view of the live system.

**Must show:** active Flash sessions, operator controls per session (start lot, end lot, extend, withdraw, announce), live bid stream, saga state (Settlement, Obligations), alerts and anomalies.

**Primary actions:** start or advance a session, intervene on a stuck lot, inspect a participant's activity, view saga timelines.

**Notes:** Artsy's "operator in the saleroom entering floor bids on behalf of online bidders" topology is the closest public reference for CritterBids' ops role. The operator's UI must show everything a participant sees plus the authoritative controls. The bounded-context split (Operations BC) maps naturally onto this page.

### 6.10 Authentication, Profile, and Settings

Standard but worth flagging: phone number for outbid SMS notifications, payment methods, shipping addresses, notification preferences, 2FA.

---

## 7. UX Qualities and Principles

Cross-cutting qualities that should be true regardless of which page the user is on.

### 7.1 Trust and Transparency

Auctions are high-stakes transactions. Bidders need to trust the platform, the seller, and the bid validity. Design elements that build trust:

- Clear seller identity with ratings and sale history.
- Honest fee disclosure from the LDP forward (never first-seen at checkout).
- Visible bid history so the auction's trajectory is auditable.
- Connection-state indicators so users know whether they are seeing live data or stale state.
- Server-synced timers so "time remaining" matches reality.
- No fabricated scarcity or social proof.

### 7.2 Liveliness

The auction should feel alive. Static pages that require manual refresh feel broken. Liveliness is produced by:

- Real-time bid stream updates via SignalR, reconciled into the TanStack Query cache.
- Subtle animation on current-bid changes (a quick pulse or number-tick animation).
- Viewer count that reflects actual concurrent attention.
- Countdown that ticks in real-time, synced periodically to server time to prevent client-clock drift.
- Bid button state that reflects the current highest bid (the amount on the button updates when the user is no longer the next valid bidder).

### 7.3 Authority Clarity

The server is the authority. The UI is a view. When an optimistic update conflicts with the server, the server wins and the UI rolls back. This has UX implications:

- Pending bids render distinctly from confirmed bids (e.g., reduced opacity, a small spinner, a "pending" pill).
- On reconnect, a reconciliation flash is acceptable; a silent state-reset is not.
- "You are winning" should never be shown unless the server has confirmed it. Optimistic UI can show "your bid was submitted" without asserting victory.

### 7.4 Ethical Urgency

Auctions have legitimate urgency. The timer is real. Scarcity is real (one winner). Use these honestly:

- Countdown timers accurately reflect server-authoritative end time.
- Extended-bidding rules are disclosed before the session starts, not surprise-applied.
- Viewer counts and bid counts are actual, not fabricated.
- No "selling fast" or "X people watching" messages unless they are true and refreshed live.

The difference between ethical urgency and dark-pattern urgency is whether the pressure is genuine. Auctions have genuine pressure built in. There is no need to manufacture more.

### 7.5 Information Density Calibration

Flash sessions want minimum chrome. The bid moment is the entire experience; ancillary information is a distraction. Timed listings want maximum useful information because the user has time to read and deliberate.

A concrete implication: the Flash session UI should have a near-empty background, one image, one timer, one current bid, one bid button. The Timed listing UI should have a full product page layout.

### 7.6 Feedback Loops

Every user action should produce immediate, unambiguous feedback:

- Tap bid button → button state changes instantly (pending state).
- Bid accepted by server → pending state clears, bid confirmation appears, user's status updates.
- Bid rejected (outbid in flight, auction ended, insufficient funds) → clear error, pending state reverts, suggested next action.
- Connection drops → visible indicator, automatic reconnect, snapshot reconciliation.
- Reconnection succeeds → brief "refreshed" flash, no silent state changes.

---

## 8. Component-Level Patterns

Specific reusable components that recur across auction UIs. Not a commitment to build each; a catalog to pull from.

### 8.1 Bid Button

The primary CTA. Typical anatomy:

- Current next-valid-bid amount on the button face: "Bid $32" rather than just "Bid."
- Large tap target (minimum 48x48 px, ideally larger for the primary CTA).
- Pressed / pending / success / error states visually distinct.
- On mobile, often a swipe gesture (Whatnot) to prevent mis-taps under time pressure.
- Secondary "Custom" button adjacent for non-default bid amounts.
- Disabled state with explanation (e.g., "Auction not started" or "You are already winning").

### 8.2 Countdown Timer

Ubiquitous on auction pages. Design considerations:

- Server-synced at regular intervals (every few seconds) to prevent client-clock drift, which is a documented eBay failure mode.
- Visual urgency escalation in the final moments (color shift, slight size increase, pulse).
- Clear about what "0:00" means (auction ends vs extended bidding starts).
- On Flash sessions with extended-bidding, reset animation when a bid triggers extension so the extension is visible and understood.
- Accessible text equivalent for screen readers ("2 minutes 14 seconds remaining").

### 8.3 Current Bid Display

Large, prominent, animated on change. Should include:

- Current high bid amount (bold, large typography).
- Currency indication (for multi-currency platforms).
- Who is winning (username or anonymized handle).
- Number of bids placed so far.
- Subtle animation on change (tick up, brief highlight).

### 8.4 Bid History

Typically a collapsible list of recent bids:

- Each bid shows bidder handle (possibly anonymized), amount, timestamp.
- Most recent at top.
- On Flash sessions, streamed live; on Timed, loaded on demand.
- For proxy/max bid clarity, Whatnot explicitly does not reveal the max amount; only the current effective bid is shown. This is a privacy affordance.

### 8.5 Watchlist / Follow Toggle

Present on every listing card and LDP:

- Icon-based (heart, star, bookmark), state-toggled.
- Feedback on add (toast, icon fill animation).
- Accessible label ("Watch this lot" / "Watching").

### 8.6 Outbid Notification

Critical for retention. Appears when another user outbids you:

- In-app toast or banner on the currently-open page.
- Push notification if the app is in background (requires PWA notification permission).
- Email or SMS as fallback, user-configurable.
- Message includes the lot name, new high bid, and a direct CTA to bid again.

### 8.7 Max Bid / Proxy Bid UX

For users who cannot be present or who want automated bidding:

- Entry via "Custom Bid" or a dedicated "Set Max Bid" affordance.
- Max amount kept private from other users (Whatnot's "Secret Max Bid" pattern).
- Clear explanation of how increments work (the system bids in minimum increments up to the max).
- Status indicator ("Your max bid is active" / "You are winning with a max of $X visible").

### 8.8 Product Card (Feed / Search Results)

Consistent, scannable, attribute-aligned across all cards in the same list:

- Primary image (same aspect ratio across cards).
- Title (clamped to 2 lines).
- Current bid / Buy-It-Now price.
- Time remaining (for auctions).
- Bid count.
- Watch toggle.
- Seller indicator if space allows.

### 8.9 Bid Status Pill

Whatnot's "smekicks is winning!" pattern. A small, high-contrast pill that tells the current user their status relative to the auction:

- "You are winning" (positive color).
- "You were outbid" (warning color).
- "Bid $X to reclaim the lead" (action pill).
- "Pending..." (during optimistic state).

### 8.10 Connection State Indicator

Given SignalR's real-time role, users need to know when they are connected:

- Small persistent badge (top corner of the live-session view).
- Green: connected.
- Yellow: reconnecting.
- Red: disconnected, with retry button.
- Accessible alternative text.

### 8.11 Live Viewer Counter

Genuine social proof. Shows how many people are watching the current lot.

- Must be real, not fabricated.
- Updates at a polite cadence (every 3-5 seconds is plenty).
- Not over-emphasized; a quiet indicator.

### 8.12 Seller Card and Trust Signals

On the LDP:

- Seller handle and avatar.
- Star rating and number of sales.
- Response rate and time for Q&A platforms.
- Verification badges if the platform verifies sellers (Whatnot verifies for certain categories).
- Link to seller profile with their other listings and full history.

### 8.13 Image Gallery and Zoom

Standard e-commerce pattern applies:

- High-resolution primary image above the fold.
- Thumbnail strip for additional angles.
- Tap to open full-screen viewer with pinch-to-zoom on mobile.
- At least four to five images for most listings; for Timed auctions of higher-value items, more.

---

## 9. Real-Time Interaction Patterns

Real-time behavior is the single most distinctive frontend challenge for CritterBids. This section goes deeper than Section 5.8 of the stack research doc did.

### 9.1 Optimistic Bid Submission

When a user places a bid, the UI should update immediately with a pending state rather than waiting for server acknowledgment. The round-trip time to a Hetzner VPS from a conference venue could be anywhere from 20ms to several hundred milliseconds depending on network conditions; waiting for that round-trip visibly erodes the sense of responsiveness.

Flow:

1. User taps Bid. Local UI updates: bid button shows pending state, user's optimistic bid appears in the bid history with a "pending" pill.
2. SignalR `PlaceBid` invocation is sent.
3. Server validates (amount, auction state, user authorization, sufficient funds) and either accepts or rejects.
4. On accept: SignalR broadcasts `BidPlaced` event. Client reconciles the optimistic state with the authoritative event, removes the "pending" pill, updates the user's status pill to "You are winning."
5. On reject: SignalR returns an error. Client rolls back the optimistic state, clears the pending pill, surfaces an error (outbid in flight / auction ended / validation failure).

React 19's `useOptimistic` hook is the idiomatic way to express this pattern, composed with TanStack Query's `useMutation` and its `onMutate` / `onError` / `onSettled` callbacks. TkDodo's "Using WebSockets with React Query" remains the canonical reference for the SignalR-to-query-cache bridge.

### 9.2 High-Frequency Bid Stream

In a Flash session with an active audience, bids can arrive at multiple per second. Rendering each bid as a separate React commit would thrash the UI.

Mitigations (established patterns in the literature):

- **Debouncing incoming updates.** 100-200 ms debounce window is the commonly-cited sweet spot; long enough to batch visible re-renders, short enough to feel live.
- **React 19 transitions.** Wrap incoming bid-stream reconciliation in `startTransition` so it is marked as non-urgent. Typing in the custom-bid input and button presses remain at urgent priority.
- **Coalescing.** Within a debounce window, collapse multiple `BidPlaced` events to the latest one for rendering purposes. Bid history can still accumulate all of them; only the "current high bid" display needs coalescing.

### 9.3 Reconnection and Reconciliation

SignalR reconnection restores the socket but not application state. A client that was disconnected for 10 seconds during a Flash session may have missed ten bids.

The reconciliation pattern, leveraging the event-sourced backend:

1. Connection drops. Client transitions to "reconnecting" connection-state indicator.
2. SignalR attempts automatic reconnection with exponential backoff and jitter (1s, 2s, 4s, 8s, up to 30s, each with ±20% randomization).
3. On reconnect, client calls an HTTP endpoint to fetch a snapshot of the auction state from the relevant Marten projection. Snapshot includes: current high bid, winning bidder handle, bid count, last N bids, timer state, current phase.
4. Snapshot is applied to the TanStack Query cache.
5. Client resubscribes to the SignalR event stream.
6. Client filters incoming events against the snapshot's version / sequence number to avoid double-counting events that the snapshot already included.
7. A brief "refreshed" flash in the UI confirms reconciliation succeeded without silently mutating state.

This is not a novel pattern. Event-sourced systems have been solving this problem for years. The notable thing is that CritterBids' backend shape makes it natural rather than bolted-on.

### 9.4 Timer Authority

The single most common auction-UX failure is timer drift. eBay community threads document cases where the client-rendered countdown is out of sync with the server, users place bids in what their screen tells them is the final second, and the bids are rejected.

Mitigations:

- Countdown renders against a server-authoritative end-time, not a local counter.
- Periodic time-sync pings (every 10-30 seconds) re-align the client's clock offset from the server's.
- In the final 30 seconds of a lot, sync more aggressively or use a server-sent tick event.
- Never use the client's `Date.now()` as the sole source of truth for countdown rendering.
- Server rejects late bids based on server-side clock, not client-reported timestamp.

### 9.5 Optimistic Display vs Authoritative Claim

A subtle distinction that affects copy and layout:

- "Your bid was submitted" (optimistic, provable locally, always safe to show immediately).
- "You are winning" (authoritative, requires server confirmation, do not show before).

Mixing these causes mistrust when the user sees "winning" and then immediately "outbid." Keep the status pill conservative during the pending window.

### 9.6 Connection-State Surface

Users need ambient awareness of whether their view is live. The research is consistent on this: when a user cannot tell whether data is fresh, they assume the worst and disengage.

A small top-corner badge with three states (connected, reconnecting, disconnected) is the standard pattern. When in reconnecting or disconnected state, bid actions should be disabled or clearly marked as queued-but-unsent.

---

## 10. Mobile and Thumb-Zone Considerations

Conference audiences are on phones. The UI must work one-handed.

### 10.1 Thumb Zones

The standard three-zone model: green (bottom center, natural thumb rest), yellow (mid-screen sides, reachable with a stretch), red (top corners, requires grip adjustment or two hands). Primary actions go in green. Secondary actions in yellow. Destructive or rarely-used actions in red.

For the Flash session UI on mobile, the bid button is the primary action and must live in the green zone. Next to it, the custom-bid affordance. Countdown and current-bid display can occupy the yellow band.

### 10.2 Bottom Sheet for Bid Input

When a user taps Custom Bid, the input affordance should slide up from the bottom as a bottom sheet. This is a well-established pattern (Google Material Design, Apple Human Interface Guidelines). shadcn/ui's `Drawer` component, based on Emil Kowalski's Vaul library, implements this idiomatically.

Advantages:

- Anchors the input at the thumb-reachable bottom.
- Keyboard slides up alongside without obscuring the input.
- Background remains partially visible, preserving spatial context.
- Dismissible by tapping outside or by downward swipe.

### 10.3 Swipe-to-Bid vs Tap-to-Bid

Whatnot uses swipe on mobile, click on web. The rationale: swipe is harder to mis-trigger than tap, which matters when the auction is moving in 30-second cycles and a wrong amount ($1,050 instead of $50) is a real and observed failure mode.

CritterBids Flash should adopt swipe-to-bid on mobile for the same reason. Timed auctions can use tap-to-bid since the pacing reduces mis-tap stakes.

### 10.4 Persistent Bottom Bar

On both the Flash session and the LDP, a persistent bottom bar pinned to the viewport makes the primary action always reachable regardless of scroll position. The bar contains the current high bid and the Bid button. Scrolling the page content reveals description, history, seller info; the bar stays put.

### 10.5 Safe Areas

iOS has a home indicator; Android has a gesture bar. The persistent bottom bar must respect these safe areas (CSS `env(safe-area-inset-bottom)`). Buttons placed right at `bottom: 0` are partially obscured or trigger the system gesture. Tailwind's `pb-safe` utility (via a plugin or custom) is the typical implementation.

### 10.6 Touch Target Sizing

Apple HIG says 44x44 pt minimum; Google Material says 48x48 dp minimum. WCAG 2.2 target size (minimum, AAA) is 24x24 pixels but aim for 48. For a primary auction CTA, larger is better; 56-64 px tall buttons are common on Whatnot and similar platforms.

### 10.7 Keyboard Management

When the user opens the custom bid input, the on-screen keyboard must not obscure the input or the submit button. The bottom-sheet pattern naturally solves this; a modal positioned in the middle of the screen does not.

### 10.8 Haptics

On mobile, subtle haptic feedback on bid submission and on bid confirmation (two distinct patterns) reinforces the feedback loop without being obnoxious. The Web Vibration API is inconsistent across browsers; this is a nice-to-have rather than a baseline feature.

---

## 11. Anti-Patterns and Dark Patterns to Avoid

Auction platforms have historically used manipulation tactics that inflate engagement at the cost of user trust. Because CritterBids is a reference architecture, it must not ship any of these, even accidentally. Some are industry-documented; some are direct anti-patterns observed in the research.

### 11.1 Fake Urgency

- Countdown timers that reset themselves after zero (documented on eBay's own platform in some edge-case scenarios; not intentional but a UX bug).
- "Sale ends in 5 minutes" banners with no actual deadline.
- Timers that silently extend without explaining why.

**CritterBids stance:** Countdown timers show actual server-authoritative end times. Extended-bidding rules are disclosed in the session info before the lot starts, and when an extension fires, a clear "timer extended" animation plays so the rule is visible, not hidden.

### 11.2 Fake Scarcity

- "Only 1 left at this price" when the inventory is unlimited.
- "5 people are watching this" when the number is fabricated.

**CritterBids stance:** Viewer counts, watcher counts, and bid counts must be actual server-side values. If the number is low, show the low number; do not inflate.

### 11.3 Fabricated Social Proof

- "John just bid!" notifications when John does not exist.
- Fake live-activity feeds.

**CritterBids stance:** Activity feeds show actual events from actual users. Anonymization of user handles is acceptable; fabrication is not.

### 11.4 Confirmshaming

- "No thanks, I don't want to win this item" as a decline button.
- Guilt-inducing copy on opt-out flows.

**CritterBids stance:** Opt-out copy is neutral. "Cancel" means cancel. "No thanks" is fine. "I don't want to save money" or "I don't want to win" is not fine.

### 11.5 Hidden Costs

- Buyer's premium disclosed only at checkout.
- Shipping cost revealed after the bid is already placed.
- Tax added as a surprise line item.

**CritterBids stance:** Buyer's premium, shipping, and applicable taxes are visible on the listing page, in the bid confirmation flow (if applicable), and on every step of checkout. The pre-win total is always calculable from the visible information.

### 11.6 Bid Sniping Manipulation

- Modal-with-timer UX that intentionally diverges from the listing-page timer to disadvantage bidders.
- Silently dropped late-bid submissions without explanation.

**CritterBids stance:** Timer is consistent across all views of the same auction. Late bids are clearly rejected with a reason ("Auction closed 0.4 seconds before bid received") if they arrive after server-side close.

### 11.7 Obstructed Cancellation

- Making it hard to unwatch a lot or cancel a max bid.
- Requiring phone calls or extended confirmations for account closure.

**CritterBids stance:** Watch and unwatch are symmetric actions. Max bids can be raised; whether they can be cancelled is a product decision (Whatnot does not allow mid-auction cancellation because of abuse vectors), but the rule is disclosed up front.

### 11.8 Auto-Bidding Without Consent

- Defaulting users into auto-bid without their explicit opt-in.

**CritterBids stance:** Max bid is an explicit, user-initiated action. It cannot be preselected or defaulted on.

### 11.9 Misleading Win Indicators

- "You are winning!" shown during the optimistic window before server confirmation.
- "Congrats, you won!" shown before auction close, then retracted.

**CritterBids stance:** Win claims are made only after server confirmation. During optimistic windows, the copy is "Bid submitted" or "Pending" rather than any claim about current standing.

---

## 12. Implications for CritterBids

Synthesizing the research into concrete guidance for when CritterBids begins UI work.

### 12.1 Patterns CritterBids Will Adopt

- Swipe-to-bid on mobile for Flash; tap-to-bid with modal confirmation on web and for custom bids.
- Server-synced countdown timers with aggressive re-sync in the final 30 seconds.
- Persistent bottom bid bar on mobile Flash and LDP views.
- Optimistic bid submission with `useOptimistic` + TanStack Query, bridged from SignalR events.
- Reconciliation-on-reconnect via projection snapshot + event replay.
- Hidden max bid (Whatnot's Secret Max Bid pattern) for Timed auctions if max-bid is supported.
- Visible connection-state indicator on live-session views.
- Upfront buyer's premium and shipping disclosure on every listing.
- Consistent attribute placement across product cards in feeds.

### 12.2 Patterns CritterBids Will Defer

- Live video streaming. Flash sessions may eventually include video but this is a separate product decision and requires a streaming infrastructure choice (WebRTC, HLS, LL-HLS, etc.) that is out of scope.
- Chat during live sessions. Nice-to-have but not required for the first conference demo.
- Recommendations and personalization. Post-MVP.
- Rich seller profiles with sale history graphs. Baseline seller card is sufficient initially.

### 12.3 Patterns CritterBids Will Not Adopt

- Any dark pattern from Section 11.
- Countdown timers in confirmation modals that can drift from the listing page (always render against the same server-authoritative time source).
- Fabricated metrics of any kind.
- Preselected max-bid checkboxes.

### 12.4 Alignment with the Event-Sourced Backend

Several UX concerns map naturally onto the event-sourced backend:

- Reconciliation on reconnect is a projection-replay from a snapshot version forward, which is what Marten projections already do.
- Bid history is an event stream that the frontend can request and render directly.
- "Who is winning" is derived state on a projection; the frontend reads it rather than computing it.
- Audit and transparency concerns (showing bid history honestly) are aided by the event log being the source of truth.

This alignment is part of why CritterBids is a showcase project: the UX stories *want* an event-sourced backend, and having one makes correctness comparatively easy.

---

## 13. Open Questions

Questions that should be resolved before or during the first UI milestone. Not for resolution in this research document.

1. **Flash session format details.** Does Flash have audio, video, text chat, or none of the above? Each has significant UI and backend implications. Pure audio (like a radio feed) is lightest; video is heaviest.
2. **Pre-bidding in Flash.** Can users submit a max bid before a Flash session starts (Whatnot's Secret Max Pre-Bid)? Or is Flash strictly in-session? Affects the Listings-to-Bidding handoff.
3. **Max-bid in Timed.** Does CritterBids support proxy / max bidding on Timed auctions? Adds complexity but is the norm on eBay and Artsy.
4. **Sudden-death vs extended-bidding.** Which variant does Flash use? Can the seller configure per-session?
5. **Reserve prices and starting bid semantics.** Does CritterBids have reserves? If so, is the reserve visible or hidden until met?
6. **Buyer's premium model.** Flat percentage? Tiered? Per-seller configurable? This affects the fee-disclosure UI pattern.
7. **Anonymous bidding.** Are bidder handles public, anonymized (eBay-style "a***r"), or fully private? Privacy preferences here are both a user-facing setting and a backend design concern.
8. **Ops dashboard scope.** Is `critterbids-ops` a separate Vite app at a subdomain, a separate route tree gated by role in the same app, or something else? Revisit in a frontend ADR after Operations BC has more shape.
9. **Notification channels.** Which of push (PWA), email, SMS does CritterBids support for outbid and win notifications? Each has infrastructure cost.
10. **Demo-first shortcuts.** For the conference-demo scenario specifically, are there allowable shortcuts (e.g., anonymous sessions rather than full registration) that do not apply to production-grade use?

---

## 14. Proposed Follow-Up Work

If this research is accepted, sensible next steps:

1. **Scope the Flash vs Timed UX divergence in a dedicated milestone doc.** Each format deserves its own lightweight design brief. The common shell is a product card, a listing detail, and a live session; the divergence is in pacing, chrome, and bid confirmation.
2. **Author `docs/skills/frontend/bidding-ux-patterns.md` once the frontend skills subtree exists.** Capture the specific component patterns from Section 8 as implementation conventions for LLM-assisted development.
3. **Produce low-fidelity wireframes for the three highest-stakes pages.** The Listing Detail Page (Timed), the Flash Session page, and the Ops Dashboard. This does not require a design tool; hand sketches or plain Figma frames suffice. The wireframes become acceptance criteria for the prototype spike in Milestone RF-3.
4. **Draft ADR 015 (or later): Bid submission UX pattern.** Specifies the swipe-to-bid, optimistic submission, reconciliation-on-reconnect, and connection-state-indicator patterns as formal conventions. Depends on the SignalR integration ADR (ADR 014) and on enough backend hub contracts existing to exercise the pattern.
5. **Identify component library primitives beyond shadcn defaults.** shadcn provides buttons, inputs, dialogs, drawers, toasts, tables. What it does not provide out of the box: auction-specific components like the countdown timer, the bid button with amount, the bid status pill, the connection-state indicator. These become custom components in the CritterBids repo and can be authored with shadcn's copy-into-project model in mind.

---

## 15. References

### Platform documentation and engineering blogs

- Whatnot Engineering, "Peeking Behind the Curtain of Secret Max Bid" (Medium, 2023). Intern write-up of the hidden max-bid feature with concrete UX decisions and reasoning. Key source for the bid-status-pill pattern, the "smekicks is winning!" copy model, and the engineering decision to educate users with pop-ups when bidding against a max bidder. https://medium.com/whatnot-engineering/peeking-behind-the-curtain-of-secret-max-bid-34abed6cbe70
- Whatnot Help Center, "Bidding in Auctions." Official UX documentation of swipe-to-bid, custom bid, max bid, pre-bid, and extended-bidding rules. https://help.whatnot.com/hc/en-us/articles/14932924544141-Bidding-in-auctions
- Whatnot Help Center, "Create and Manage Auctions." Documents the 2-minute extended bidding window for non-livestream auctions. https://help.whatnot.com/hc/en-us/articles/21232798243981-Create-and-manage-auctions
- Whatnot Engineering, "Navigating Android's UI Landscape" (Medium, 2022). Not directly auction-UX but shows the company's general frontend discipline; relevant for chat and live-session shared components.
- Artsy Engineering, "The Tech Behind Live Auction Integration" (2016). Canonical public reference for live-auction operator + bidder topology. Directly analogous to CritterBids' ops-vs-participant architecture. https://artsy.github.io/blog/2016/08/09/the-tech-behind-live-auction-integration/
- Artsy Blog, "How We Built the Leading Platform for Bidding Online." Discusses the deliberate decision to *not* add live video because existing tech introduced "lags and choppiness" that eroded bidder confidence. A useful counterpoint for anyone tempted to add video to CritterBids Flash prematurely. https://medium.com/artsy-blog/what-it-takes-to-provide-true-value-as-a-market-platform-840e8fde1422
- Artsy, "The Complete Guide to Bidding in Artsy Auctions." Documents the max-bid + live-bidding dual model for high-value auctions. https://www.artsy.net/article/artsy-specialist-complete-guide-bidding-artsy-auctions

### eBay timer and countdown UX

- eBay Community, multiple threads on countdown timer drift and mismatch between listing-page and bid-modal timers. Valuable as documented failure modes to avoid rather than patterns to replicate. Summary of observed problems: client-clock drift causing bid submissions in the apparent final seconds to be rejected; modal timer drifting from listing timer; countdown "jumping" forward after network hiccups. https://community.ebay.com/t5/Report-eBay-Technical-Issues/Ebays-auction-countdown-timer-out-of-sync-with-reality/td-p/34090217

### E-commerce UX and product-detail page research

- Nielsen Norman Group, "UX Guidelines for Ecommerce Product Pages." Research-based guidance on what a product-detail page must show and the progressive-disclosure principle. https://www.nngroup.com/articles/ecommerce-product-pages/
- Baymard Institute, "2 Key Design Principles for Product Listing Information (64% Get at Least 1 Wrong)." Evidence for consistent attribute placement across cards in a list. https://baymard.com/blog/list-item-design-ecommerce
- Shopify Partners, "The Top UX Elements to Optimize Your Clients' Product Page Design." Industry-practice guidance from designers who have worked on eBay, AO.com, and similar. https://www.shopify.com/partners/blog/61771331-the-top-ux-elements-to-optimize-your-clients-product-page-design
- Design4Users, "Product Page Design: Best Practices on UX for Ecommerce" (2026). Up-to-date guidance on inverted-pyramid information architecture.
- VWO, "eCommerce Product Page Best Practices in 2026."
- ConvertCart, "Product Page UX: 22 Data-Backed Secrets for High Conversions" (2026).

### Mobile UX and thumb-zone design

- Vignesh Kumar Muniraj, "The Thumb Zone Rule: How to Design Mobile Interfaces for Giant Screens" (Medium, 2025). Concise summary of green/yellow/red zones, bottom sheets, and FAB placement. https://medium.com/@vigneshkumarmuniraj011/the-thumb-zone-rule-how-to-design-mobile-interfaces-for-giant-screens-08e905f08bb4
- Parachute Design, "Mastering the Thumb Zone: Mobile UX & UI Design Guide" (2026). Detailed guidance on bottom navigation, sticky CTAs, tap-target sizing, safe areas, and one-handed testing.
- Heyflow, "Mastering the Thumb Zone: How Mobile-First Design Can Unlock More Conversions."
- Elise Warren, "Top vs. Bottom: Uncovering the Ideal Mobile CTA Position for Faster Decision-Making" (Medium, 2025). Useful counter-evidence that thumb-zone-down is not universally correct for every CTA type; hierarchy still matters for certain decision screens. Informs the Flash-vs-LDP split in Section 10.
- Plotline, "Best Examples of Mobile App Bottom Sheets."

### Real-time UI and optimistic updates

- React team, `useOptimistic` hook reference documentation. https://react.dev/reference/react/useOptimistic
- Epic React (Kent C. Dodds), "`useOptimistic` to Make Your App Feel Instant."
- OpenReplay, "How Optimistic Updates Make Apps Feel Faster" (2025). Cites 40% perceived-latency reduction from immediate visual feedback. Practical patterns for rollback and reconciliation.
- "React 19 `useOptimistic` Deep Dive" (dev.to, 2025). Practical rollback strategies including the "keep optimistic change visible but mark as failed" variant relevant to bid rejection UX.
- TkDodo (Dominik Dorfmeister), "Using WebSockets with React Query" (TanStack blog). Canonical reference for the SignalR-to-TanStack-Query cache bridge pattern referenced throughout this document.
- TanStack DB docs, Query Collection with WebSocket `writeBatch` patterns. Newer option for real-time collection mutations.

### Auction-specific bidding UX sources

- MoldStud, "WebSockets Integration for Real-Time Bidding in React Auction Systems." Debounce window guidance (100-200 ms) and exponential-backoff reconnection patterns.
- Novu blog, "Building a Real-Time Auction System with Socket.io and React.js."
- Various HiBid, Proxibid, iRostrum, Bidpath product pages documenting the feature baseline expected of white-label auction software.

### Dark patterns and ethical urgency

- Fyresite, "Dark Patterns: A New Scientific Look at UX Deception." Summary of the Princeton 2019 dark-patterns study, including the 1.2% of e-commerce sites with fake countdown timers.
- Finance Watch, "Dark patterns explained: how to spot and avoid deceptive UX" (2025). Summary of the EU regulatory position (Digital Services Act, Consumer Rights Directive) and TikTok's €345 million fine.
- LogRocket, "Dark Patterns in UX Design: What They Are and What to Do Instead." Catalog of cognitive biases exploited by dark patterns and the ethical alternatives.
- Zero To Mastery, "Beginner's Guide To Dark Patterns In UX Design."
- Arounda, "22 Dark Patterns Examples You Should Avoid" (2026). Cites the OECD report finding 75% of sites contain at least one dark pattern.
- Learningloop, "Dark Patterns. What it is, How it Works, Examples."

### Adjacent references

- React documentation on build tools and meta-frameworks (context for framework-posture decisions, covered more deeply in `frontend-stack-research.md`).
- `docs/research/frontend-stack-research.md` (companion doc in this repository).
- `docs/decisions/012-frontend-spa-vite.md` and `013-frontend-core-stack.md` (ADRs that the UI patterns in this document will be built on top of).
