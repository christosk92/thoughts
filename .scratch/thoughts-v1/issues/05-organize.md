# 05: AI backend, Organize and the capture strip

**What to build:** After saving, the AI suggests a Project, People and a title, and writes an English card, in the background. Results appear in the capture strip. The owner sets the API key through `dotnet user-secrets` in development and Credential Manager in the app, and never pastes it in chat. Until ticket 06 exists, checks stay small and the owner may set a spending limit on the API account.

**Blocked by:** 02

**Status:** done

- [x] The model backend interface has an API key implementation, with Sonnet 5.5 as the Organize default
- [x] Capture never waits: the strip fills in a few seconds later
- [x] Project goes to Unsorted when nothing fits, and a new Project needs the person's confirmation
- [x] A Keep-local Thought makes no AI call and is left out of other Thoughts' context, confirmed by logging what is sent
- [x] Cards are in `.thoughts/`, and the AI writes neither `status` nor the body
- [x] Note text in a prompt is treated as data: a fake note containing instructions doesn't change behaviour
- [x] A failed call leaves the Thought saved, and it retries later
- [x] Dutch, English and typos each get a sensible result on fake notes
- [x] Verify on the throwaway vault with the real AI and report the AI cost of the check.

## Comments

Verified 2026-11-02 on throwaway vaults with the real AI, on a Debug NativeAOT publish (26 MB, no warnings), through the debug --drive input. About 14 real calls in all, under 5 US cents by my estimate from list prices. Ticket 06 measures this properly.

The Anthropic C# SDK needs reflection-based JSON, which NativeAOT turns off, so the API key backend is plain HTTP with a JSON writer.

Added at the owner's request: a card shows where it is in the AI's line, in a footer slot reserved on every card (Organizing, Waiting to organize, Kept local, Couldn't organize with Retry, then the Project and a sparkle). A card keeps its identity through the AI's rename, and a filed card stays in its row for 1.1 s with an accent border before it moves. Screenshots cover each state; the hold and the failure were shot on a throwaway build with a longer hold and a forced failure.

"Couldn't organize" is remembered only until the app closes.

Not driven: a real mouse, Retry being clicked, the motion itself frame by frame (only stills), and the full Board height with a three-line title shrinking to one.
