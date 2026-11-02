# 09: Connections

**What to build:** After Organize, the AI proposes up to 3 Suggested Connections with a concrete consequence, a fresh call tries to reject each one, and the survivors appear in the strip and the panel with Accept, Dismiss and Build on it. This is the feature the owner cares about most.

**Blocked by:** 05

**Status:** done

- [x] The credit system and bring-your-own-AI fake notes produce a Connection with a concrete consequence
- [x] Unrelated fake notes produce none
- [x] Quotes are checked against the notes, tolerating small differences such as a corrected typo
- [x] At most 3, and each is a note in `Connections/` with `between` and `state`
- [x] Accept adds `connected` on both Thoughts, and Dismiss never comes back
- [x] Build on it creates a Thought pre-filled with the explanation, coming `from` both
- [x] Cards are not shown to Connect, only used to select notes
- [ ] The Connections appear correctly in Obsidian's graph (the notes match the format tested in `docs/research/2026-11-01-obsidian-links.md`, but Obsidian wasn't opened on them)
- [x] Verify on the throwaway vault with the real AI and report the AI cost of the check.

## Comments

Verified 2026-11-02 on throwaway vaults with the real AI, through the debug --drive input, on a Debug build. A Debug NativeAOT publish has no warnings but wasn't driven. About 17 real calls cost about $0.09, added up from the app's figures with a few estimated because their logs were overwritten by later runs.

Real AI results, on fake notes:
- Credit system and Bring your own AI: a Connection saying every AI call spends credits, but a customer's own key costs nothing, so the rule must change before pricing is final. Check held.
- An English note on a 10,000 sends an hour cap and a Dutch note on 40,000 readers at 7:00: a Connection saying it takes at least four hours. Tried both ways round, and the explanation followed the new Thought's language.
- Tired after lunch: no nearby notes, so no Connect call.
- A venue for the launch party against a launch blog post: Organize picked it as nearby, and Connect answered none.
- Keep-local text was never sent.

These few pairs are a small sample, and Check never rejected anything here, so its rejecting path hasn't been seen on a real answer. The proper measure is ticket 15.

Quote check: a throwaway driver outside the repo passed on exact, spacing and case differences, a corrected typo in English and in Dutch, and wrong quotes.

Accept set `state` and added `connected` on both Thoughts. Dismiss set `state`, and a queued Connect for a dismissed pair made no call. Build on it made a Thought with the claim as its body, `from` both, in Inbox, opened it, and accepted the Connection.

Found by looking at the screen and fixed: a Thought made with Build on it got a Suggested Connection back to its own source. Connect now skips notes linked by `from`, in both directions, confirmed with no call.

Choices made here:
- Organize picks up to 8 nearby notes from every card, so Connect can find notes in other words or languages. Connect itself never sees a card.
- Connect runs on Opus 5.5 at high effort, and Check on Opus 5.5 at medium. A Build on runs it again, for the target.
- A Connection's file name is a timestamp, because a Thought's rename mustn't leave it stale.
- Build on it counts as accepting the Connection.
- Removing a note with Add as detail deletes its open suggestions.

Left out:
- Granularity is ticket 12, so Coarse doesn't exist yet.
- Accepted Connections have no way to be removed from the app.
- Review shows them in ticket 11.

Not driven: a real mouse, Obsidian's graph, Open on a Connection's link, a Connection to a note renamed later, and the note changing during a Connect call.
