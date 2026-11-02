# 11: Review

**What to build:** The person starts Review, which shows all of Inbox plus Parked Thoughts not looked at for 2 weeks, oldest first, one at a time, with suggestions and related Thoughts, and a Status choice for each.

**Blocked by:** 04, 09

**Status:** done

- [x] Selection and order are right, using a `reviewed` date
- [x] Suggested Connections can be acted on in Review
- [x] The Doing limit still applies
- [x] Stopping and resuming works
- [x] Verify on the throwaway vault. Makes no AI calls.

## Comments

Verified 2026-11-02 on a throwaway vault through the debug --drive input, on a Debug build. No AI calls. The owner was away, so nobody has tried this by hand.

From nine fake notes, Review picked the right four, oldest first: two Inbox, one Parked never reviewed, and one Parked last reviewed 20 days ago. It left out Parked reviewed 3 days ago, Parked 2 days old, and Next and Doing. Choosing a Status set it and wrote `reviewed`. Doing was refused when full, and the Thought stayed on screen with no date written. Skip dated it and moved on. Stopping and starting again carried on with the two left, and the last one ends on a finished screen.

Suggested Connections show with their actions, using the same view as the panel. Accept, Dismiss and Build on it were driven in ticket 09, not here.

This also closes a gap from ticket 07: what Organize suggests now stays in `.thoughts/suggestions/` after the strip closes, and shows in the panel and Review with its actions, until answered. Its display was checked on a seeded file, with no real AI call writing one in this ticket.

Choices made here:
- An Inbox Thought looked at today isn't asked again today, which is what makes resuming work.
- Parked counts from whichever is later, `reviewed` or `created`, so a note parked yesterday isn't due.
- The button is called Skip, not Next, because Next is a Status. Skip still dates the Thought.

Not driven: a real mouse, Open from Review, and the suggestion's buttons.
