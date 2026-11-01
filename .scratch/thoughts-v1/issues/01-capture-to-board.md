# 01: Capture a Thought and see it on the Board

**What to build:** Type a Thought, press Enter, and it is saved at once as a note in the vault and appears on a simple Board. The vault location is a setting. This also creates the app project against the engine through `$(EngineRoot)`.

**Blocked by:** None (can start immediately)

**Status:** done

- [x] Enter saves a note in `Thoughts/` with `status: Inbox`, `captured`, `created` and the person's words as the body, in Obsidian's property style
- [x] "Saved to Inbox" shows at once and focus stays in the capture box
- [x] Thoughts in the vault show on the Board after a restart
- [x] Saving works with no network
- [x] The vault location can be changed in settings, and no check touches the real vault
- [x] Verify on the throwaway vault. Makes no AI calls.

## Comments

Verified 2026-11-02 by driving the real app on throwaway vaults in C:\WAVEE\scratch\thoughts-check, by keyboard. No AI calls. Saving involves no network code, so 'works with no network' holds by construction, not by a run with the network off. Not driven: mouse clicks, because the engine reads WM_POINTER and posted mouse messages don't reach it. Not built: a NativeAOT publish, so YamlDotNet under AOT is unproven.
