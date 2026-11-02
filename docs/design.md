# Thoughts: design

Status: signed off by the owner on 2026-11-02. Terms are defined in [GLOSSARY.md](../GLOSSARY.md). Evidence is in `docs/research/`.

## Why

Ideas arrive faster than they can be acted on, and many get lost because nothing holds them until there's time to come back. Thoughts is a bucket to drop anything into, circle back on and then work on. AI organizes; the person decides what gets worked on.

## Goals

- Capture in seconds, and never depend on the network to save.
- Track the Status of every Thought, from small ("discuss with Sam") to large (a product feature).
- Surface relationships the person didn't know about, and say why they matter.
- Show cost at all times, and cap it.

## Non-goals for v1

Phone capture, share or send, notifications, importing from Teams or chats, custom Statuses, a Project-level Map, a background sweep of old Thoughts, hosted embeddings, built-in backup, screen reader roles in the Markdown control.

## Shape

- A PC-only desktop app on FluentGpu, in its own repo at `C:\WAVEE\thoughts`.
- The vault is the source of truth ([ADR 0001](adr/0001-vault-is-the-source-of-truth.md)). Its location is configurable, because this may become a product. The default is `vault/` in this repo, ignored by git. The owner adds it to their own backup before trusting the app with real thoughts.

## Files in the vault

Tested against Obsidian 1.14.4 and 1.14.5 ([results](research/2026-11-01-obsidian-links.md)). Use flat properties in Obsidian's own style: block lists, double-quoted links, LF, no comments.

```yaml
# Thoughts/Credit system.md (the body holds the person's words and is never written by the AI)
---
status: Parked
project: "[[Atlas]]"
person: "[[Sam]]"
from:
  - "[[Add AI to Atlas]]"
connected:
  - "[[Bring your own AI]]"
keep_local: false
captured: "credit system"
reviewed: 2026-11-01
created: 2026-11-01T23:30
---
```

```yaml
# Connections/<any stable name>.md (the body is the explanation)
---
between:
  - "[[Credit system]]"
  - "[[Bring your own AI]]"
state: suggested   # or accepted, dismissed
---
```

- `Projects/` and `People/` hold one note each. Only notes in `Thoughts/` count as Thoughts; one with no Status starts in Inbox and gets its missing properties added.
- `captured` never changes. The body is the person's to edit.
- A title is suggested once, in the Thought's language, and becomes the file name. Keep-local Thoughts use their first line. Only the person renames.
- `.thoughts/` (hidden, in the vault) holds cards, pending suggestions and the cost log. Obsidian ignores it: tested on 1.14.4, its files were not in `app.vault.getFiles()`.

The app must:
- Parse YAML properly, write canonical form and re-read after writing.
- Read the two Thoughts from `between`, never from a Connection's file name.
- Rewrite links for its own renames, and detect broken links from others.
- Ban `#` in file names and block duplicate names.
- Turn on `alwaysUpdateLinks` for this vault, with graph settings `-[state:accepted] -[state:dismissed]` and colour groups on `state` and `status`.
- Accept the risk that the last save wins when Obsidian edits the same note at once.

## Rules

- **Status:** Inbox, Parked, Next, Doing, Done, Dropped. Only the person changes it. At most 2 Thoughts in Doing across the whole Board.
- **AI writes** `project`, `person`, and suggestions. It never writes `status` or the body.
- **Links you remove stay removed:** a dismissed Connection never comes back.
- **Capture outcomes**, suggested by the AI and changeable:
  - New work that grew from a Thought: its own Thought with `from`.
  - More detail on the same work: added to that Thought's body (Build on).
  - Unrelated: a new Thought with no link.
- **Fan out** only when the person asks. The AI suggests pieces and the person ticks them. New Thoughts inherit the Project, link back with `from`, and start in Parked. The original's Status stays manual, and the panel shows "2 of 3 done".
- **Connections:** at most 3 per Thought, and only with a concrete consequence, otherwise none. Actions are Accept (adds `connected` on both Thoughts), Dismiss, and Build on it (a new Thought pre-filled with the explanation, coming `from` both).
- **Granularity:** Fine is Connections only and is the default. Coarse adds plain related links.

## Screens

- **Board** (prototype layout B): one row per Project, columns Inbox to Done, Dropped in a collapsed row, a Project filter, Review with its count.
- **Capture strip:** under the capture box. Shows "Saved to Inbox" at once, then the Project and suggestions a few seconds later, with actions. It fades at the next capture, and the suggestions stay in the panel and Review.
- **Panel:** a floating sheet with no border, a header tinted in the Project's colour, Status as a row of steps, suggestions, Connections, and source + preview Markdown editing using the FluentGpu control. "Open in Obsidian" is pinned at the bottom.
- **Map** (v1): opened from a Thought, a 3D space the person moves through. Drag orbits, the wheel zooms, Shift-drag pans, and arrow keys, plus, minus and Home also work. `from` links are solid branches, and Connections are dotted. Nodes show title and Status colour. Click opens the panel, and double-click recentres. A Thought with two parents draws the first as its branch and the second as dotted. Near nodes are larger, far ones fade, and labels that would overlap give way to nearer ones. It draws nothing while still.
- **Review:** all of Inbox, plus Parked not looked at for 2 weeks, oldest first, one at a time. The person starts it.
- **Usage page:** this month against Budget, by step and model, and every call, with no note text in the log.

## AI

A fixed chain, not an agent, with no tools given to the model ([research](research/2026-11-01-organizing-workflow.md)):

| # | Step | Who |
|---|---|---|
| 0 | Stop if Keep-local, and check again at send time | Code |
| 1 | Candidates: all titles (up to the cap), trigram search, graph links | Code |
| 2 | Organize: Project, People, Build on, title, an English card. Reads every card up to 300 notes; above that, local embeddings pick which 300 ([research](research/2026-11-02-embeddings-rag.md)) | Sonnet 5.5 |
| 3 | Connect: up to 3 Connections or none, with quotes from both notes, a mechanism and a consequence | Opus 5.5 |
| 4 | Check quotes against the notes, tolerating small differences | Code |
| 5 | Check: a fresh call tries to reject each claim | Opus 5.5 |
| 6 | Show suggestions, and write only what the person accepts | Person, then code |

- The chain runs in the background after saving, and at Build on, where the card refreshes too. Failed calls leave the Thought saved in Inbox and retry later.
- Models are configurable per step. Note text is data, never instructions.
- **Keep-local** Thoughts are never sent, not even as context for other Thoughts, and get no AI links.
- **Language:** cards are English with typos fixed. Titles and explanations follow the Thought's language, with a setting to override. The person's words are never corrected.
- **Embeddings:** `multilingual-e5-small` runs locally, on the NPU when present and the CPU otherwise. It ships in v1 and logs its ranks beside the AI's picks, with no effect on suggestions until 300 notes. Measured on fake text only.

## Backends and cost

- **API key** is the default. In development it comes from `dotnet user-secrets`; in the app, from Windows Credential Manager, set in settings.
- **Callers** can do the reasoning instead, on their own model, through `thoughts-mcp` (below). This replaced the planned `claude -p` backend, so the app never launches Claude Code or handles its login.
- **Budget:** a monthly amount. Warn at 80%. At 100% AI work pauses, and capture, Board and Review keep working.
- Cost isn't a limit for now. The research estimated about $0.035 per Thought on Haiku and Sonnet, which roughly doubles with Opus, plus up to $0.037 for reading cards. None of this is measured.

## thoughts-mcp

A Caller is a Claude Code session or other MCP client. It adds Thoughts and does the reasoning on its own model, so no API key is needed.

- **Process:** `thoughts-mcp.exe`, a separate stdio server pointed at a Vault. It works with the app closed and opens no port. The app sees its changes through the file watcher.
- **Who reasons:**
  - A Thought added through MCP never goes to the API by itself. While it waits, its card says so, and its menu offers "Organize with API key".
  - A Thought typed in the app goes to the app's AI first. Callers can pull it only when there is no key or the Budget is used up.
- **What starts it:** nothing wakes a Caller. The server offers an "organize waiting Thoughts" prompt, and the app shows how many are waiting. The person runs it or schedules it.
- **Tools:** `add_thought`, list and claim waiting work, submit a result, confirm a new Project, and read-only search over Thought text and cards. There are no Status changes, and no rewriting of the person's words.
- **Steps:** Organize and Connect, in that order. Pulling work returns the same instructions and JSON schema the app uses, and the same code checks every result, so New Projects need confirmation and Connections stay Suggested.
- **New Projects:** a Caller can suggest one. The Thought stays in Unsorted with the suggestion saved. The person confirms in the app, or in the Caller's chat, where the Caller confirms through a tool on their say-so.
- **Access:**

| Action | Needs the Settings switch? |
|---|---|
| Add a Thought | No |
| Organize a Thought the Caller just added | No. It gets Project and People names |
| Pull other Thoughts, run Connect, search | Yes. Off by default, per Vault |

- **Keep-local:** never returned by pulls, search or context. A Caller can add a Keep-local Thought with a flag, and the tool says its model has already seen that text once.
- **Note text is data:** it comes back clearly delimited, and the docs suggest a session with few tools. This reduces prompt injection but can't remove it, because a Caller can have real tools such as shell and files.
- **Usage page:** Caller work is listed with the step, client name and self-reported model. It costs nothing against the Budget and continues at 100%.
- **Claims:** a claimed Thought goes back in line after 10 minutes with no result, so a dead session can't hold it.

## Proving it

After a few dozen real Thoughts, run a blind manual check using a plain script: Connections mixed with unrelated pairs, Dutch-English pairs and typos. It chooses the models, or drops Connections. About 30 labelled pairs only shows gaps of about 30 points. Hosted embeddings may be compared later, only with the owner's agreement.

## Decided at sign-off

- The app is called Thoughts.
- No tests in this repo, the same rule as the fgpu workspace. Verify each ticket by running the real app with real AI, on a throwaway vault of fake Thoughts with a low Budget. The real-notes check is a plain script.

## Open

Left to the builder or to verify:
- Whether the official C# MCP SDK works under NativeAOT. If not, stdio JSON-RPC can be written by hand, as the API backend was.
- How the app and `thoughts-mcp` share `.thoughts/`, because the queue file was written for one process.
- How a Caller does the Check step, which needs a fresh call that the server can't force on a Caller's session.
- The Qualcomm licence, before the app is shipped to anyone.
- The FluentGpu Markdown control's second pass, on engine branch `feat/markdown`, not pushed. The app needs that branch and PR #62 merged, or built against them.
