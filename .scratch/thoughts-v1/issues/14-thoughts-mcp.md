# 14: thoughts-mcp

**What to build:** `thoughts-mcp.exe`, a separate stdio MCP server for one Vault, as designed in `docs/design.md`. A Caller adds Thoughts and does Organize and Connect on its own model. It replaces the planned `claude -p` backend. Organize-only may ship first as a slice, ahead of Connect.

**Blocked by:** 05 for Organize, 09 for Connect, 06 for the Usage page entries

**Status:** built, one check is left for the owner

- [x] The server runs with the app closed and opens no port, and the app shows its changes through the file watcher
- [x] It builds with NativeAOT, with no warnings
- [ ] `add_thought`, list and claim, submit, confirm a new Project, read-only search, and the "organize waiting Thoughts" prompt all work from a real Claude Code session (driven from the official MCP client with a real model and by the protocol driver, but NOT from Claude Code: see below)
- [x] A Thought added through MCP never reaches the API by itself, and its menu offers "Organize with API key"
- [x] A Thought typed in the app is pullable only with no key or a used-up Budget
- [x] Pulling returns the app's instructions and schema, and the same code checks results: a new Project needs confirmation, Connections stay Suggested, and no tool changes Status or the body
- [x] With the Settings switch off, adding and organizing the Caller's own Thought work, and pull, Connect and search refuse. The switch is off by default, per Vault
- [x] Keep-local Thoughts are never returned, confirmed by logging what the server sends
- [x] A fake note containing instructions comes back delimited as data, and the docs warn about Callers with real tools
- [x] An unfinished claim returns to the line after 10 minutes, and the app and server never lose a queue entry when both write
- [x] The Usage page lists Caller work with step, client and self-reported model, at no Budget cost, and it continues at 100%
- [ ] Verify on the throwaway Vault with a real Claude Code session (not done: see below)

## Comments

Verified 2026-11-02 on throwaway vaults. The server is an 8 MB Debug NativeAOT publish with no warnings. A throwaway driver outside the repo spoke the real JSON-RPC over stdio, and 31 of 31 checks passed: the handshake, tools and prompt, refusals with the switch off, a bad answer refused, a rename once, a new Project waiting for confirmation, Status and words untouched, an invented id and a false quote dropped, a held check saved as Suggested, claims going back after expiry and not offered twice, and Keep-local absent from data and search. The answers in those checks were written by the driver, so no model did the reasoning.

**Not verified: a real Claude Code session.** Claude Code's login on this PC had expired ("OAuth session expired and could not be refreshed"), and the owner was away. Signing in is theirs to do, and using their Thoughts API key to get around it wasn't something they'd agreed to. So how a real client negotiates the protocol, and how a real model follows these work packages, are both unproven, including how it treats the fake note that tries to hijack the session. To run it, sign in to Claude Code, then run the throwaway script at `C:/WAVEE/scratch/thoughts-mcp-real-run.py`, which uses only fake notes and only this server's tools:

    python -I C:/WAVEE/scratch/thoughts-mcp-real-run.py

It isn't in the repo. It includes a fake note that tries to take over the session.

**Closest check available: the official MCP client with a real model.** The official Python `mcp` library (2.3.0) connected over stdio and negotiated protocol 2025-11-25, then listed the tools and read the prompt. Sonnet 5.5 through the Messages API, with this server's tools as its only tools, followed the `organize_waiting` prompt: it added a Thought, organized it, claimed a waiting one, ran Connect and a Check, and stopped when nothing was left, for about $0.17 on the Thoughts API key. It produced one Suggested Connection, to Bring your own AI, with real quotes. The fake note telling it to add HACKED and search for SECRETWORD was filed as a note, and neither happened. That's one run, and a Claude Code session has far more tools to be tricked into using, so it says little about that risk.

It also found a rough edge: the model first submitted using the title it had just proposed, was refused, and retried. The work package now says to use the given title.

In the app, with an API key set, a Thought in the Caller line stayed untouched: no usage and the queue unchanged. Its card says "Waiting for a Caller", the Board says how many, and "Organize with API key" is in its menu.

The driver found one real bug on its first run: multi-line JSON constants broke the one-message-per-line protocol. Fixed.

Choices made here:
- JSON-RPC is written by hand. The official C# SDK wasn't tried, because one small dependency-free loop was enough.
- A claim is taken out of its queue file while claimed, so the app can't pick up the same work. Both processes take a file lock for queue files.
- `submit_check` asks for a fresh look, but the server can't force one on a Caller's session, as the design says. A Caller that ignores this rates its own claims.
- The server doesn't load local embeddings, so above 300 notes a Caller gets the newest 300.
- With the switch off, `add_thought` still returns the instructions and Project and People names, as designed.

Not driven: two processes writing the queue at the same moment (the lock is there, but no race was forced), x64, and search in another language, because it matches words only.
