# 15: The real-notes check

**What to build:** A plain script and procedure, run by the owner after a few dozen real Thoughts. It mixes real suggestions with unrelated pairs, Dutch-English pairs and typos, and the owner rates them blind. The results choose the models or drop Connections. See section 5 of the workflow research.

**Blocked by:** 09

**Status:** built and tried on a fake vault. The real run is the owner's

- [x] The script needs nothing in the repo beyond itself, with no test project
- [x] Keep-local Thoughts are excluded, and it states what it will send and its estimated cost before running
- [x] Ratings are blind, and the results are written as a short report
- [x] The report says what size of gap the data can support: about 30 pairs only shows gaps of about 30 points
- [x] Tried once on a fake vault by the builder. The real run is the owner's, on their real notes

## Comments

`tools/real-notes-check.py` needs only Python. Tried 2026-11-02 on a fake vault of 15 notes, with the real AI: Haiku and Sonnet once, about $0.15, then Sonnet again after a fix, about $0.19. `--assume-unrelated` was used to skip confirming decoys by hand, and a person's marking was stood in for by piped scores, so the scores in that report mean nothing.

It refused an output folder in a git repo, sent nothing on "n", and resumed after stopping. A Keep-local note, one with unreadable properties, and a Dropped one appeared in neither the plan nor the results. The report held only numbers. With 7 rated Connections, it said only a gap of about 65 points could be told from chance.

**The first run found something worth knowing.** My first copy of the prompt left out the app's examples, and Sonnet then wrote explanations for English notes in Spanish and Dutch. With the examples included, as the app sends them, that stopped: all five English pairs came back in English. So the examples are carrying that rule, and it's worth watching if the prompt changes. The script now warns if any line of its prompts stops matching the app's.

Also from those runs: decoys held twice out of 20 in the Sonnet run, for example Shorter wizard with Bring your own AI. The real run will show if that's a real problem, because those pairs were never confirmed unrelated by a person.

Choices made here:
- Candidates for a real pair are the closest by letter overlap, not by Organize, so this measures Connect and Check on their own.
- A typo pair has letters swapped by code, and Dutch-English pairs are found with a small word list.
- Each config sends its prompts without caching, so the estimate and cost are a little higher than the app's.
- The same claim from two configs is rated once.

Not tried: `plan`'s interactive confirming of decoys, and the Opus config. The owner's real run is still to come, after a few dozen real Thoughts.
