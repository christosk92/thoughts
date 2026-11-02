# 03: Vault integrity and Obsidian sync

**What to build:** The vault stays sound when edited from either side. A change in Obsidian appears in the app, renames keep links working, and broken links get reported. See `docs/research/2026-11-01-obsidian-links.md`.

**Blocked by:** 01

**Status:** done

- [x] An edit made in Obsidian shows in the app without a restart
- [x] The app re-reads after each write, and writes canonical properties: no comments, flat properties only
- [x] The app's own renames rewrite every link in the vault, and Obsidian's are picked up
- [x] `#` and duplicate names are refused at creation, and broken links are reported
- [x] The app sets `alwaysUpdateLinks` and the graph filter and colour groups for its vault only, leaving the owner's other vaults alone
- [x] Tested and recorded: whether Obsidian ignores `.thoughts/`
- [x] Verify on the throwaway vault. Makes no AI calls.

## Comments

Verified 2026-11-02 with the app and an isolated Obsidian (1.14.4, updated to 1.14.5 during the run) on a throwaway vault and profile, inspected through DevTools. Obsidian ignores `.thoughts/`. With the app running, an edit and a rename made in Obsidian appeared on the Board without a restart, and Obsidian's rename updated links with no prompt. The app's own rename rewrote links in properties, in longer text, with a folder and with a heading, left code blocks alone, and Obsidian then reported only the deliberately broken link as unresolved. A taken name is refused on rename and numbered on capture. Existing .obsidian settings were kept.

Added: renaming a Thought from its menu, since the design makes that the person's job.

Not tested: writing settings while Obsidian is open (it may overwrite them), and an external edit racing a save, which the design accepts as last save wins. Not checked: the graph's colours by eye, on this build.
