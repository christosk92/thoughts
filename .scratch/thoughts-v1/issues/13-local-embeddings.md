# 13: Local embeddings

**What to build:** `multilingual-e5-small` runs locally on the NPU when present and the CPU otherwise, comparing cards. Up to 300 notes it logs its ranks beside the AI's picks with no effect on suggestions. Above 300 it picks which 300 cards the AI reads. See `docs/research/2026-11-02-embeddings-rag.md`.

**Blocked by:** 09

**Status:** ready-for-agent

- [ ] Works on the NPU here, and falls back to the CPU
- [ ] Below 300 notes, suggestions are unaffected and the shadow log fills
- [ ] With 300+ fake notes, it picks the 300 and Organize's cost stops growing
- [ ] Nothing leaves the PC at this step
- [ ] Works under NativeAOT, or the gap is reported
- [ ] Verify on the throwaway vault. Makes no AI calls. Except the 300+ check, which uses the real AI.
