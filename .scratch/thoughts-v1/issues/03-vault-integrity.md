# 03: Vault integrity and Obsidian sync

**What to build:** The vault stays sound when edited from either side. A change in Obsidian appears in the app, renames keep links working, and broken links get reported. See `docs/research/2026-11-01-obsidian-links.md`.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] An edit made in Obsidian shows in the app without a restart
- [ ] The app re-reads after each write, and writes canonical properties: no comments, flat properties only
- [ ] The app's own renames rewrite every link in the vault, and Obsidian's are picked up
- [ ] `#` and duplicate names are refused at creation, and broken links are reported
- [ ] The app sets `alwaysUpdateLinks` and the graph filter and colour groups for its vault only, leaving the owner's other vaults alone
- [ ] Tested and recorded: whether Obsidian ignores `.thoughts/`
- [ ] Verify on the throwaway vault. Makes no AI calls.
