# 08: Fan out

**What to build:** Pressing Fan out on a Thought shows suggested pieces of its body to tick. Ticked pieces become new Thoughts. For a Keep-local Thought the person picks the lines themselves, with no AI.

**Blocked by:** 07

**Status:** done

- [x] The AI only groups numbered segments, so it can't alter the words
- [x] New Thoughts inherit the Project, have `from`, and start in Parked
- [x] The original's body and Status are untouched
- [x] The panel shows a count such as "2 of 3 done" and never closes the original
- [x] A Keep-local Thought makes no AI call
- [x] Verify on the throwaway vault with the real AI and report the AI cost of the check.

## Comments

Verified 2026-11-02 on throwaway vaults with the real AI, through the debug --drive input, on a Debug build. A Debug NativeAOT publish has no warnings but wasn't driven. Five calls cost about $0.021 by the app's own figures.

On one mixed note, the AI grouped the venue lines together and left out the note's context line. A pasted instruction in the note was ignored. Ticking two pieces made two Thoughts in Parked with the Project and `from`, the original stayed byte for byte the same, and each new Thought was then organized normally, which renamed them and kept `from`. The count read "1 of 2 done" with one piece Done, one Parked and one Dropped.

A Keep-local note made no AI call, offered its lines, and its piece was checked on disk to stay Keep-local. A one-line note says there is nothing to split and makes no call.

Choices made here:
- Pieces start unticked.
- A piece of a Keep-local Thought is Keep-local too.
- Dropped pieces don't count in "2 of 3 done".
- Anything linked with `from` counts, including Keep as new work from ticket 07.
- With no key, the Budget used up, or a failed call, the person can pick lines instead.
- The list markers stay in a piece's body, because those are the person's words, but not in its name.

Also fixed: the capture strip could show the result of a different Thought than the one just captured.

Not driven: a real mouse, a failed AI call, no key, or the Budget used up during Fan out, and Pick lines on a note the AI grouped.
