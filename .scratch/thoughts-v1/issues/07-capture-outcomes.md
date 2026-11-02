# 07: Capture outcomes and Build on

**What to build:** A new Thought can be more detail on an existing one, new work grown from it, or unrelated. The AI suggests which, and the person can change it with one click.

**Blocked by:** 04, 05

**Status:** done

- [x] More detail is suggested as Building on the target, added to its body only when the person agrees, and can be undone
- [x] New work becomes its own Thought with `from` linking back
- [x] Unrelated gets no link
- [x] The AI's Build on suggestion quotes the target and usually suggests none
- [x] The card refreshes after a Build on
- [x] Verify on the throwaway vault with the real AI and report the AI cost of the check.

## Comments

Verified 2026-11-02 on throwaway vaults with the real AI, through the debug --drive input, on a Debug build. A Debug NativeAOT publish has no warnings but wasn't driven. Nine calls cost about $0.037 by the app's own figures.

Four captures, each with the right answer: a wizard detail got more_detail, a help article got grew_from, an unrelated note got none, and a Dutch note with typos got more_detail on an English note with a Dutch explanation. The Keep-local note never appeared in what was sent. That is a small sample, not a measure of precision.

Add as detail moved the words to the end of the target and removed the first note, and the target's card was refreshed. Undo restored the target byte for byte, brought the note back and removed the card the refresh made. Keep as new work wrote `from` as a block list. Unrelated writes nothing. The quote check drops a suggestion whose quote isn't in the target, but no real answer failed it, so that path hasn't run.

Choices made here: `from` is written only on a click, not automatically on a strong suggestion, because the design lists the links the AI may write and `from` isn't among them. Every Thought up to 300 is sent as title, Status, Project, card and a best-matching snippet of up to 200 characters.

Left out:
- Closing the strip loses the suggestion, until suggestions are kept for the panel and Review (tickets 09 and 11).
- If the AI suggests no relation, there is no way yet to pick a target by hand.
- Undo lasts only while the strip is open, and doesn't survive a restart.
- The panel doesn't show `from` yet.

Not driven: a real mouse, Undo after the target was edited, and Unrelated being clicked.
