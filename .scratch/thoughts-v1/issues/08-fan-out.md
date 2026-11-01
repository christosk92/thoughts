# 08: Fan out

**What to build:** Pressing Fan out on a Thought shows suggested pieces of its body to tick. Ticked pieces become new Thoughts. For a Keep-local Thought the person picks the lines themselves, with no AI.

**Blocked by:** 07

**Status:** ready-for-agent

- [ ] The AI only groups numbered segments, so it can't alter the words
- [ ] New Thoughts inherit the Project, have `from`, and start in Parked
- [ ] The original's body and Status are untouched
- [ ] The panel shows a count such as "2 of 3 done" and never closes the original
- [ ] A Keep-local Thought makes no AI call
- [ ] Verify on the throwaway vault with the real AI and report the AI cost of the check.
