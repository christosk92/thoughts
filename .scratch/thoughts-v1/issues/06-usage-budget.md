# 06: Usage page and Budget

**What to build:** Every AI call is recorded with step, model, tokens and cost, with no note text. A Usage page shows this month against the Budget. At 80% it warns, and at 100% AI work pauses while everything else keeps working.

**Blocked by:** 05

**Status:** done

- [x] The Usage page shows the month against Budget, by step and model, and each call
- [x] Figures match the usage the API returned
- [x] The log holds no note text
- [x] With a tiny Budget, the warning and the pause both happen, and capture and Board still work (Review is ticket 11, so it was not checked)
- [x] Thoughts that waited get their AI work after the Budget is raised
- [x] Verify on the throwaway vault with the real AI and report the AI cost of the check.

## Comments

Verified 2026-11-02 on a throwaway vault with the real AI, through the debug --drive input, with a Budget of $0.008. Four calls cost $0.0142 by the app's own figures, and the last ran on a Debug NativeAOT publish with no warnings. Tokens in the log matched the API's usage for every call compared, and no note text or title was in the log.

91% showed the warning. Going over paused the AI: a third capture saved, showed "Waiting to organize", and made no call. Raising the Budget on the Usage page organized it and emptied the queue.

Choices made here: the Budget defaults to $20, and a month is the local calendar month. The log is per Vault, while the Budget is per machine, so two Vaults each count only their own calls. A call already running can overshoot the Budget, as it did here ($0.0098 of $0.008), because the check comes before a call.

Prices are a table in code, from October 2026, so they need updating when list prices change.

Not driven: a real mouse, a saved Budget surviving a restart (check runs don't save settings), a new month ending a pause, and the light theme.
