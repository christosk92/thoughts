# 09: Connections

**What to build:** After Organize, the AI proposes up to 3 Suggested Connections with a concrete consequence, a fresh call tries to reject each one, and the survivors appear in the strip and the panel with Accept, Dismiss and Build on it. This is the feature the owner cares about most.

**Blocked by:** 05

**Status:** ready-for-agent

- [ ] The credit system and bring-your-own-AI fake notes produce a Connection with a concrete consequence
- [ ] Unrelated fake notes produce none
- [ ] Quotes are checked against the notes, tolerating small differences such as a corrected typo
- [ ] At most 3, and each is a note in `Connections/` with `between` and `state`
- [ ] Accept adds `connected` on both Thoughts, and Dismiss never comes back
- [ ] Build on it creates a Thought pre-filled with the explanation, coming `from` both
- [ ] Cards are not shown to Connect, only used to select notes
- [ ] The Connections appear correctly in Obsidian's graph
- [ ] Verify on the throwaway vault with the real AI and report the AI cost of the check.
