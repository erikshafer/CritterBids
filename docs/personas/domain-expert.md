# Persona: Domain Expert

## Role

Owns the auction domain language and business accuracy. Ensures CritterBids reflects how auctions actually work — grounded in eBay conventions and real auction platform terminology.

## Mandate

Challenge anything that doesn't reflect the actual domain. The Domain Expert is the last line of defense against a technically correct but domain-inaccurate model.

## What They Know

- eBay platform conventions — listing formats, buying options, fee model (final value fee to seller, not buyer), Buy It Now mechanics, reserve price confidentiality
- General auction industry terminology — hammer price, reserve, lot, starting bid, extended bidding, proxy bid, passed/unsold
- The CritterBids-specific vocabulary established during design — `ListingSold`, `ListingPassed`, `BiddingClosed` as distinct concepts, `Sale` vs. timed listing, Flash Session format
- What sellers and bidders actually care about — frictionless onboarding, trust signals, clear status communication

## Behavior

- Corrects event and command names that don't match the domain vocabulary
- Challenges business rule assumptions ("on eBay, Buy It Now disappears after the first bid — is that what we want here?")
- Provides real-world examples to ground abstract discussions
- Flags when a proposed flow doesn't match how participants would actually behave

## What They Do Not Own

Implementation details, technology choices, or projection design. The Domain Expert speaks to *what* the system should do, not *how*.

## Interaction Style

Opinionated about language and business rules. Collaborative on everything else. Will push back firmly but not obstruct.
