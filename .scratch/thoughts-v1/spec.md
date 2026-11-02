# Thoughts v1

Status: ready-for-agent

The signed-off design is `docs/design.md`, and it wins on detail. Terms are in `GLOSSARY.md`. Evidence is in `docs/research/`.

## Problem Statement

I have more ideas than I can act on, and they end up scattered. I lose track of what each idea's status is, and I don't see how ideas relate, even when an old one changes what a new one means.

## Solution

One place on my PC to drop any thought in a few seconds, knowing it's safe. The AI organizes it, says how it relates to my past Thoughts, and suggests what to do, while I decide what gets worked on. A Board shows every Thought's Status by Project, a panel lets me build on a Thought, a Map shows how an idea grew, and Review makes me come back to what I parked. It all lives as plain notes in an Obsidian vault.

## User Stories

### Capture

1. As the owner, I want to type a Thought and press Enter, so that it's saved within a moment and I can stop holding it in my head.
2. As the owner, I want saving to work with no network or AI, so that I can always trust the bucket.
3. As the owner, I want to see "Saved to Inbox" straight away, so that I know it's safe before anything else happens.
4. As the owner, I want what the AI found to appear under the capture box a few seconds later, so that suggestions are clear at creation without opening anything.
5. As the owner, I want to keep typing while the AI works, so that capture never makes me wait.
6. As the owner, I want the suggestions strip to fade at my next capture while they stay in the panel and Review, so that ignoring it costs nothing.
7. As the owner, I want to see nothing but "Saved to Inbox" when the AI has nothing worthwhile, so that I'm not given filler.
8. As the owner, I want to mark a Thought Keep local, so that it never reaches any AI.
9. As the owner, I want work and personal Thoughts in one inbox, so that I don't have to sort before capturing.

### Organizing

10. As the owner, I want the AI to suggest a Project from my list, so that I don't have to file things myself.
11. As the owner, I want a Thought to go to Unsorted when no Project fits, so that wrong filing doesn't hide it.
12. As the owner, I want the AI to propose a new Project only when a Thought clearly starts one, and I confirm it, so that my list stays mine.
13. As the owner, I want to edit my list of Projects, so that it matches my work.
14. As the owner, I want the AI to link a Person when a Thought is about or for them, so that a Person's note gathers what I want to raise with them.
15. As the owner, I want the AI to suggest a short title once, in the Thought's language, so that the file name stays stable as the body grows.
16. As the owner, I want to write in Dutch or English, with typos, so that the AI still finds what relates.
17. As the owner, I want explanations in the Thought's language, with a setting to override, so that I read what I wrote in.
18. As the owner, I want a failed AI call to retry later without losing anything, so that outages don't matter.

### Status and the Board

19. As the owner, I want one Board with a row per Project and a column per Status, so that I see everything competing for my time.
20. As the owner, I want to filter the Board by Project, so that I can focus.
21. As the owner, I want only me to change a Status, so that the AI never decides what I work on.
22. As the owner, I want at most 2 Thoughts in Doing, so that I finish things.
23. As the owner, I want Dropped Thoughts in a collapsed row, so that they're out of the way but findable.
24. As the owner, I want a clear message when Doing is full, so that I know to move something out first.

### Panel, Build on and Fan out

25. As the owner, I want to open a Thought in a panel without losing my place on the Board, so that drilling in has no layout jump.
26. As the owner, I want to edit a Thought's body with source and preview, so that I can add detail after working on it.
27. As the owner, I want my first words kept unchanged, so that I can always see what I first thought.
28. As the owner, I want more detail I capture to be suggested as Building on the right Thought, with one click to undo, so that fragments don't fill the Board.
29. As the owner, I want new work that grew from a Thought to become its own Thought linked back to it, so that "credit system" gets tracked and I still see where it came from.
30. As the owner, I want to press Fan out and tick what becomes new Thoughts, so that it only adds to my work list when I ask.
31. As the owner, I want fanned-out Thoughts to inherit the Project and start in Parked, so that they don't go through Inbox again.
32. As the owner, I want a Thought's panel to show "2 of 3 done" without closing it for me, so that finishing the pieces doesn't decide it's finished.
33. As the owner, I want "Open in Obsidian" in the panel, so that I can use the rich editor.

### Connections

34. As the owner, I want the AI to suggest how a new Thought relates to a past one, with a sentence stating a concrete consequence, so that I learn what I hadn't noticed.
35. As the owner, I want at most 3 suggestions, or none, so that I keep reading them.
36. As the owner, I want every suggestion checked against what the notes really say, so that invented links are rare.
37. As the owner, I want to Accept, Dismiss or Build on a Suggested Connection, so that a surprising link can become work.
38. As the owner, I want a dismissed Connection never to come back, so that I don't repeat myself.
39. As the owner, I want a Granularity setting, Fine or Coarse, so that I can also see plain related links when I want them.
40. As the owner, I want Connections to find related notes in other words or languages, so that the app beats what keyword search could.

### Map

41. As the owner, I want a Map of how one Thought grew, so that I can see what's still open in an idea.
42. As the owner, I want Status colours on Map nodes and Connections as dotted lines, so that I can read it at a glance.
43. As the owner, I want clicking a node to open its panel and double-clicking to recentre, so that I can move around an idea.

### Review

44. As the owner, I want a Review I start myself, showing Inbox and Parked Thoughts I haven't looked at for 2 weeks, oldest first, so that circling back is a habit and not a growing pile.
45. As the owner, I want to see suggestions and related Thoughts during Review, so that I decide with context.

### Vault and Obsidian

46. As the owner, I want every Thought as a plain note, so that my data stays readable without the app.
47. As the owner, I want a change in Obsidian to appear in the app and the other way round, so that I can use either.
48. As the owner, I want relationships in Obsidian's graph, with suggestions coloured, so that I can explore them there too.
49. As the owner, I want renames not to break links, and broken links reported, so that the vault stays sound.
50. As the owner, I want to set where the vault lives, so that this can work for other people.

### Cost and backends

51. As the owner, I want a Usage page showing this month against Budget, by step and model, and each call, so that I always know what the AI costs.
52. As the owner, I want a warning at 80% and AI paused at 100%, with capture still working, so that spending can't run away.
53. As the owner, I want models configurable per step, so that I can change them when I learn more.
54. As the owner, I want to use an API key by default, kept out of the vault and repo, so that my notes aren't used for training without my permission.
55. As the owner, I want a Caller such as Claude Code to add Thoughts and do the reasoning through MCP, so that I can use my subscription and run with no API key.
56. As the owner, I want a check on my real notes after a few dozen Thoughts, so that I know whether Connections are insight or plausible sentences.

## Implementation Decisions

Modules, kept deep and small, with the app's UI depending only on their interfaces:

- **Vault:** reads, watches and writes notes in Obsidian's own property style. Owns the note formats in the design, canonical writing and re-reading after a write, link rewriting for its own renames, broken-link detection, file-name rules, and the setup of `alwaysUpdateLinks` and graph settings for the vault.
- **Board state:** the signals the UI reads. Owns Status rules, the Doing limit, Review selection and "2 of 3 done".
- **Organizer:** the fixed chain from the design, with no tools given to the model. Owns candidates, prompts, schemas, validation including tolerant quote checks, Keep-local stops, retries and the `.thoughts/` store.
- **Model backend:** one interface, implemented by the API key. Callers reach the same prompts and validation through `thoughts-mcp` (`docs/design.md`). Takes a prompt and a schema, and returns structured output plus usage.
- **Embeddings:** `multilingual-e5-small` locally through ONNX Runtime, NPU when present and CPU otherwise. Logs ranks in shadow up to 300 notes, and picks the 300 above that.
- **Usage:** records every call without note text, enforces the Budget, and feeds the Usage page.
- **Screens:** Board (prototype layout B), capture strip, panel, Map, Review, Usage, Settings, on FluentGpu. The prototype is on the local branch `prototype/board` and is to be rewritten, not promoted.

Other decisions:

- Default models: Sonnet 5.5 for Organize, Opus 5.5 for Connect and Check.
- Draft prompts and schemas are in `docs/research/2026-11-01-organizing-workflow.md`, written for a Haiku and Sonnet chain, so re-read them for the chosen models.
- The app builds against an engine checkout through `$(EngineRoot)`. It needs PR #62 and the engine branch `feat/markdown`, neither of them merged yet.

## Testing Decisions

No tests, by the owner's decision. The one seam is the whole app: run it on a throwaway vault of fake Thoughts, with the real AI where the ticket uses AI and a low Budget, then drive it and check screenshots, note files on disk and Usage figures. Check external behaviour only. Report what was and wasn't verified, and the AI cost of each check. Real AI gives varying results, so check the shape and rules of a suggestion, and treat quality as the job of the real-notes check.

## Out of Scope

Phone capture, share or send, notifications, importing from Teams or chats, custom Statuses, a Project-level Map, a background sweep of old Thoughts, hosted embeddings, built-in backup, screen reader roles, shipping to other people.

## Further Notes

To verify while building: whether Obsidian ignores `.thoughts/`, and the Qualcomm licence before any shipping. Real-AI checks need an API key, which the owner sets through `dotnet user-secrets` and never pastes in chat.
