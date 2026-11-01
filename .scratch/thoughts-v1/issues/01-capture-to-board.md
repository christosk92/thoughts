# 01: Capture a Thought and see it on the Board

**What to build:** Type a Thought, press Enter, and it is saved at once as a note in the vault and appears on a simple Board. The vault location is a setting. This also creates the app project against the engine through `$(EngineRoot)`.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] Enter saves a note in `Thoughts/` with `status: Inbox`, `captured`, `created` and the person's words as the body, in Obsidian's property style
- [ ] "Saved to Inbox" shows at once and focus stays in the capture box
- [ ] Thoughts in the vault show on the Board after a restart
- [ ] Saving works with no network
- [ ] The vault location can be changed in settings, and no check touches the real vault
- [ ] Verify on the throwaway vault. Makes no AI calls.
