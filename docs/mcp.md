# thoughts-mcp

Lets a Claude Code session or another MCP client add Thoughts and do the reasoning on its own model, so the app can run with no API key. It's a separate program that talks over stdio, works with the app closed, and opens no port.

## Set it up

Build it:

```bash
dotnet publish src/ThoughtsMcp/ThoughtsMcp.csproj -c Release -r win-arm64
```

Add it to your client. For Claude Code:

```bash
claude mcp add thoughts -- "C:\path\to\thoughts-mcp.exe" --vault "C:\path\to\vault"
```

## What a Caller can do

| Tool | What it does | Needs the switch? |
|---|---|---|
| `add_thought` | Saves a Thought in the person's words and returns the work to organize it | No |
| `submit_organize` | Hands in that work | No |
| `confirm_project` | Files a Thought in a suggested Project, after the person said yes | No |
| `list_waiting`, `claim_next` | Finds other Thoughts waiting for a Caller | Yes |
| `submit_connect`, `submit_check` | Looks for Connections | Yes |
| `search` | Reads Thoughts by words in their titles, text and cards | Yes |

The server also offers an `organize_waiting` prompt, which is the easiest way to start.

**The switch** is "Let MCP Callers read other Thoughts" in the app's Settings. It's off by default, for each Vault. Turning it on sends note text and cards to whatever model that client uses.

## Who organizes what

- A Thought added through MCP never goes to the API by itself. Its card says "Waiting for a Caller", and its menu has "Organize with API key".
- A Thought typed in the app goes to the app's AI first. Callers can only pull it when there's no key or the Budget is used up.
- Nothing wakes a Caller. Run the prompt when you like, or schedule it yourself.

A Caller's answers go through the same checks as the app's own AI. New Projects need your confirmation, Connections stay Suggested until you accept them, quotes must really be in the notes, and no tool changes a Status or rewrites your words.

Keep-local Thoughts are never returned. If a Caller adds one with `keep_local`, its model has already seen that text once, and it's never sent again.

Caller work shows on the Usage page with the step, client and the model the Caller says it is. It costs nothing against the Budget, and continues at 100%.

A claimed Thought goes back in line after 10 minutes with no answer, so a dead session can't hold it.

## Prompt injection

**This reduces the risk but can't remove it.** A note can contain text aimed at the AI, and a clipped web page is the likely source. A Claude Code session has real tools such as a shell and files, so a note that fools the model could use them. Note text comes back marked as data, with instructions to treat it that way.

Run this in a session with few tools. For example:

```bash
claude -p "Use the organize_waiting steps from the thoughts server." --allowedTools "mcp__thoughts__*" --disallowedTools "Bash,Edit,Write"
```
