# Thoughts

A PC desktop app on FluentGpu, over an Obsidian vault: drop in any thought, circle back on it, work on it. AI organizes; the person decides. Start with [docs/design.md](docs/design.md).

## Commands

The engine lives in a separate repo and is set with `$(EngineRoot)`. Always build into `C:\WAVEE\scratch`, because the default engine folder is one the owner runs from.

```powershell
dotnet build <project> -c Debug -nodeReuse:false --artifacts-path C:/WAVEE/scratch/art-thoughts -p:EngineRoot=<engine checkout>\
```

UI work: invoke the `fgpu` skill first. Look up engine APIs with `python -I C:/WAVEE/fgpu/tools/fg.py api Type.Member`.

## Hard rules

- **No tests.** No test projects, test files or harnesses. Verify with the seam below. The real-notes check is a plain script.
- **Never run against the real vault while verifying.** `vault/` holds the owner's private thoughts and is ignored by git. Never read it, commit it or send it anywhere.
- **Keep-local Thoughts are never sent to any AI**, not even as context for another Thought.
- **The AI never writes a Thought's body or `status`.**
- **Ask first** before pushing, adding a remote or any GitHub change.
- API keys never go in the vault, the repo or chat.

## Verifying a ticket

Run the real app with a throwaway vault of fake Thoughts under `C:\WAVEE\scratch`, and the real AI where the ticket uses AI, with a low Budget set. Drive it, look at the screenshots, check the note files on disk, and check the Usage figures against the calls made. Report what was verified and what wasn't, and the AI cost of the check. A ticket that doesn't use AI makes no AI calls.

## Agent skills

### Issue tracker

Tickets and the spec are local Markdown files under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
