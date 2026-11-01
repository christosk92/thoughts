# 02: Board rows, Statuses and the Doing limit

**What to build:** The Board is prototype layout B: one row per Project, one column per Status, Dropped in a collapsed row, and a Project filter. The person changes a Status and it is saved to the note.

**Blocked by:** 01

**Status:** done

- [x] Rows come from `Projects/`, plus Unsorted, and the person can add, rename and remove Projects
- [x] A Status change is written to the note and survives a restart
- [x] A third Thought can't enter Doing, and a clear message says why
- [x] Dropped Thoughts are in a collapsed row and can be opened
- [x] The filter shows one Project
- [x] Verify on the throwaway vault. Makes no AI calls.

## Comments

Added at the owner's request: drag and drop. Dragging a card onto a cell sets its Status and Project, onto a row label only its Project, onto Dropped drops it. Doing refuses with a reason when full.

Verified 2026-11-02 on throwaway vaults through the debug --drive input, which uses the real input path: drags, a refused drop, menus, filter, Dropped, and Project add, rename and remove, with notes checked on disk. A note's other properties, comments and body survive a change. Writing converts a note's CRLF to LF. Not checked: the motion itself by trace (still screenshots only), hover states, and the real mouse by hand.
