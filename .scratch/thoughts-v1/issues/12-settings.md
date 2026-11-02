# 12: Settings: Granularity, language and models

**What to build:** Settings for Granularity (Fine or Coarse), language override and the model per step.

**Blocked by:** 09

**Status:** done

- [x] Fine is the default and suggests only Connections
- [x] Coarse also suggests plain related links, stored so they appear in the graph
- [x] Language override changes titles and explanations, while cards stay English
- [x] A model change takes effect on the next call, and Usage shows it
- [x] Verify on the throwaway vault with the real AI and report the AI cost of the check.

## Comments

Verified 2026-11-02 on a throwaway vault with the real AI, through the debug --drive input, on a Debug build. Four calls cost about $0.026 by the app's own figures.

With Coarse, Dutch and Haiku for Organize set, an English note was titled "Creditsysteem voor AI-aanroepen", with Dutch explanations and an English card. Usage logged Haiku for Organize, Opus for Connect and Sonnet for Check, which were the settings. Bring your own AI got a real Connection, Check held.

The pricing page claim got "obvious" from Check, the first real rejection seen, so it didn't become a Connection. Coarse then wrote it as a plain related link: a note in `Connections/` with `kind: related` and no sentence.

Choosing Coarse in Settings saved it to the settings file, checked in a run with no `--vault`, which also confirms that settings do save. Fine is the default, and it's the path every earlier ticket ran.

Choices made here:
- The language override is appended to the prompt, so anyone not using it keeps the cached prompt.
- A related link can be accepted or dismissed, but has no Build on it, because it has no sentence.
- Refresh card uses the Organize model.
- Models are a fixed list of four, not free text. A model the price table doesn't know is charged as Opus.

Not driven: a real mouse, a change to a model made in the Settings page followed by a call (set through the settings file here), and how a related link looks in Obsidian's graph.
