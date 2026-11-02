# 04: Panel with source and preview editing

**What to build:** Clicking a Thought opens the floating panel from the prototype, without moving the Board. The person edits the body with source and preview using FluentGpu's Markdown control, and it saves as they type.

**Blocked by:** 02. Also needs engine PR #62 and `feat/markdown` merged, with the app building against a main checkout

**Status:** done

- [x] The panel opens and closes with no layout jump on the Board
- [x] Body edits save as you type, and an external edit appears in the panel
- [x] Status can be set from the panel, respecting the Doing limit
- [x] `captured` stays unchanged however the body is edited
- [ ] "Open in Obsidian" opens that note (built, not clicked in a check, because it would launch the real Obsidian)
- [x] Text selection, multi-line editing and task toggles work by real input
- [x] Verify on the throwaway vault. Makes no AI calls.

## Comments

Verified 2026-11-02 on a throwaway vault through the debug --drive input: opening with no Board movement, task toggles, Status with the Doing limit, typing with autosave, closing with unsaved text, an outside edit appearing, [[links]] between Thoughts, a missing link's message, the link warning dialog, Escape, and keyboard opening. Dark and light looked at. `captured` and other properties stayed unchanged.

The owner asked for a WYSIWYG editor, designed separately. Since 2026-11-02 the panel uses it in place of source + preview: click and type, with a format bar and a larger dialog.

Not driven: Open in Obsidian, IME, pointer text selection in this panel (driven on the engine's own page).
