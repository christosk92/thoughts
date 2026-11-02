# 14: thoughts-mcp

**What to build:** `thoughts-mcp.exe`, a separate stdio MCP server for one Vault, as designed in `docs/design.md`. A Caller adds Thoughts and does Organize and Connect on its own model. It replaces the planned `claude -p` backend. Organize-only may ship first as a slice, ahead of Connect.

**Blocked by:** 05 for Organize, 09 for Connect, 06 for the Usage page entries

**Status:** ready-for-agent

- [ ] The server runs with the app closed and opens no port, and the app shows its changes through the file watcher
- [ ] It builds with NativeAOT, with no warnings
- [ ] `add_thought`, list and claim, submit, confirm a new Project, read-only search, and the "organize waiting Thoughts" prompt all work from a real Claude Code session
- [ ] A Thought added through MCP never reaches the API by itself, and its menu offers "Organize with API key"
- [ ] A Thought typed in the app is pullable only with no key or a used-up Budget
- [ ] Pulling returns the app's instructions and schema, and the same code checks results: a new Project needs confirmation, Connections stay Suggested, and no tool changes Status or the body
- [ ] With the Settings switch off, adding and organizing the Caller's own Thought work, and pull, Connect and search refuse. The switch is off by default, per Vault
- [ ] Keep-local Thoughts are never returned, confirmed by logging what the server sends
- [ ] A fake note containing instructions comes back delimited as data, and the docs warn about Callers with real tools
- [ ] An unfinished claim returns to the line after 10 minutes, and the app and server never lose a queue entry when both write
- [ ] The Usage page lists Caller work with step, client and self-reported model, at no Budget cost, and it continues at 100%
- [ ] Verify on the throwaway Vault with a real Claude Code session.
