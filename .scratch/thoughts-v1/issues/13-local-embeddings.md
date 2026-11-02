# 13: Local embeddings

**What to build:** `multilingual-e5-small` runs locally on the NPU when present and the CPU otherwise, comparing cards. Up to 300 notes it logs its ranks beside the AI's picks with no effect on suggestions. Above 300 it picks which 300 cards the AI reads. See `docs/research/2026-11-02-embeddings-rag.md`.

**Blocked by:** 09

**Status:** done

- [x] Works on the NPU here, and falls back to the CPU
- [x] Below 300 notes, suggestions are unaffected and the shadow log fills
- [x] With 300+ fake notes, it picks the 300 and Organize's cost stops growing
- [x] Nothing leaves the PC at this step
- [x] Works under NativeAOT, or the gap is reported
- [x] Verify on the throwaway vault. Makes no AI calls. Except the 300+ check, which uses the real AI.

## Comments

Verified 2026-11-02 on throwaway vaults through the debug --drive input, on a Debug build and on a Debug NativeAOT publish (30 MB, no warnings). About 12 real calls cost about $0.19 by the app's own figures, most of it the two 300-card captures. The figure for the first captures is added up by hand, because later runs replaced their logs.

| Check | Result |
|---|---|
| Hexagon NPU | Ran with CPU fallback off, and the shadow log says `npu` |
| CPU, via a debug flag | Shadow log says `cpu` |
| NativeAOT | Both of the above on the published exe |
| 320 notes | 300 cards sent, 29,844 input tokens |
| 450 notes | 300 cards sent, 29,999 input tokens, so the cost stops growing |
| An old relevant note, older than all 450 | In the 300 both times, which newest-first would have lost |
| Keep-local | Not in the index or the log |

Below 300 notes all cards still go to the AI, and the shadow log only gets numbers: when, device, how many cards, and the local rank of each note the AI picked. No text or titles.

`tools/get-embeddings.ps1` was run for real into a throwaway folder. It downloads from pypi.org and huggingface.co, and checks the wheels against PyPI's checksums. The model itself isn't checked against a hash.

Choices made here:
- Everything is optional. Without the download, the app works as before, and above 300 notes it sends the newest 300.
- The Narrow step queries with the new note's own text, because its card doesn't exist until Organize has run. The research assumed the card. The shadow log does use the card.
- A session is opened only while in use, so nothing stays in memory between captures.
- This follows the ticket, so local matches are not added to Connect's candidates, though the research suggested it.

Left out: the research also wanted the log to say whether the person accepted each pick. Those two facts aren't joined yet, because the log holds no titles.

Not checked: the first run on a vault with thousands of notes on the CPU, which blocks that first capture's AI work while it builds (the research estimates a minute for 5,000), x64 PCs, and the Qualcomm licence, which still needs reading before the app ships to anyone.
