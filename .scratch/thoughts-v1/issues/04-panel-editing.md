# 04: Panel with source and preview editing

**What to build:** Clicking a Thought opens the floating panel from the prototype, without moving the Board. The person edits the body with source and preview using FluentGpu's Markdown control, and it saves as they type.

**Blocked by:** 02. Also needs engine PR #62 and `feat/markdown` merged, with the app building against a main checkout

**Status:** ready-for-agent

- [ ] The panel opens and closes with no layout jump on the Board
- [ ] Body edits save as you type, and an external edit appears in the panel
- [ ] Status can be set from the panel, respecting the Doing limit
- [ ] `captured` stays unchanged however the body is edited
- [ ] "Open in Obsidian" opens that note
- [ ] Text selection, multi-line editing and task toggles work by real input
- [ ] Verify on the throwaway vault. Makes no AI calls.
