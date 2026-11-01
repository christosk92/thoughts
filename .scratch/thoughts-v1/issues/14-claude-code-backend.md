# 14: Claude Code backend

**What to build:** An optional backend using `claude -p` behind the same interface, for personal use. It isn't offered to other users.

**Blocked by:** 05

**Status:** ready-for-agent

- [ ] The locked-down invocation is proven: no tools, and no hooks, skills, `CLAUDE.md`, memory or MCP servers are loaded. If this can't be proven, the ticket stops and reports that
- [ ] Prompts and schemas are the same as the API key backend
- [ ] Failures, including login errors, behave like API failures
- [ ] Usage shows these as estimates, not a bill
- [ ] Settings warn to turn off the model-improvement setting before real notes
- [ ] Verify with fake notes, and report plan usage where visible
