# Embeddings and retrieval for Connections

Research date: 2026-11-02. This covers how to compare cards and pick candidates (step 3 of the chain), which embedding model to use, and how to measure and tune it. It updates the embedding parts of `2026-11-01-organizing-workflow.md`, which I did not edit. No file in the vault was read, and every web search used generic terms. No paid API calls were made.

**Evidence labels**

| Label | Meaning |
|---|---|
| **[Measured here]** | Run on this PC, 2026-11-02, using fake text only |
| **[Smoke test]** | Run on 60 fake notes and cards that **I wrote by hand**. It catches design mistakes, but isn't evidence about real notes |
| **[Source]** | A linked source says so |
| **[Inference]** | My judgement |

---

## Recommendation for v1

**Compare cards, not note text. Embed locally with `multilingual-e5-small`, on the NPU when it's there and on the CPU otherwise. Let the AI read every card while the vault has up to 300 notes, and above that let local embeddings choose which 300 it reads.**

```text
New Thought
  → Organize call (Sonnet 5.5): Project, Build on, new card, and up to 8 candidates chosen from the cards it was shown
  → Add the top few local matches the AI didn't pick
  → Connect and Check (Opus 5.5)
```

| Vault size | Cards the AI reads | Added cost per capture | What the local model does |
|---|---|---|---|
| Up to 300 notes | All | $0.007 at 50 notes, $0.037 at 300 | Runs in shadow, and adds a few candidates |
| Above 300 notes | The 300 nearest, picked locally | Stays at about $0.037 | Also does the narrowing |

**This replaces the 1,500-note threshold.** There is one code path and no jump in behaviour, and the 300 is a single setting. The 300 itself is a starting guess.

**Hosted embeddings are not the v1 default.** They are cheap and may be better, but nothing I found shows they'd help here, because cards are short English text. They also add a second company that receives text from every non-Keep-local note. Section 2 states that tradeoff for you to decide, and section 4 shows how to test one.

**Why this order (a smoke-test result, so a reason to test it and not proof):**

| Finding on 60 fake notes | Result |
|---|---|
| Surprising pairs found in the top 5 using note text (e5), among the 31 notes with cards | 42% |
| Same pairs and notes using English cards (e5) | 96% |
| The bring-your-own-AI and credit-system pair, using note text | Rank 36 of 59 |
| The same pair, using cards | Rank 1 of 30 |

**The main caution:** I wrote those cards myself, knowing which pairs should match. Real cards from a model may score far lower.

**Interim default until real data exists:** ship the table above. Log what the AI chose beside what the local model ranked, and don't tune anything. Section 4 says when to look.

---

## 1. This PC's NPU

### Hardware [Measured here]

| Item | Value |
|---|---|
| PC | ARM64 laptop, 32 GB RAM |
| CPU | Snapdragon X Elite X1E80100, 12 cores, ARM64 |
| NPU | Qualcomm Hexagon, listed as `ComputeAccelerator`, driver 30.0.222.0 |
| GPU | Qualcomm Adreno X1-85, driver 31.0.148.0 |
| OS | Windows build 26340 |
| Tools | .NET SDK 10.0.401 (native ARM64). The default Python is x64 under emulation, so it can't load the ARM64 QNN wheel |

The driver is above the [30.0.140.0 minimum Windows ML lists for this chip](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/supported-execution-providers) [Source].

### Can it run from .NET under NativeAOT? Yes [Measured here]

I built a scratch NativeAOT console (a 4.6 MB `embed.exe`, ARM64) that binds the ONNX Runtime C API with function pointers, the way Wavee does in [`AiLyrics.Ort.cs`](../../../fgpu/WaveeMusic/src/apps/Wavee/AiLyrics/AiLyrics.Ort.cs). It loaded ONNX Runtime 1.30.0 and the QNN 2.6.0 plugin, found the NPU, and ran `multilingual-e5-small` with CPU fallback disabled.

| Check | Result |
|---|---|
| Vectors, NativeAOT on NPU versus Python on CPU | Cosine 0.99998 on all 5 test strings |
| .NET tokenizer (`Microsoft.ML.Tokenizers`, SentencePiece) versus the Python reference | **Identical IDs on 105 of 105 strings**, including Dutch diacritics, emoji, Japanese, Russian and an empty string |
| Tokenizer time | 0.04 ms p50, on 5 short strings |
| Inference | 3.67 ms p50, 4.08 ms p95 |

**One detail code must handle:** the model's IDs are the SentencePiece ID plus 1, with `<s>`=0 and `</s>`=2. The 105-string check confirms this.

### Latency: NPU versus CPU [Measured here]

Fixed-length rows use 380 never-repeated random inputs on the NPU, and CPU fallback was off. p50 in milliseconds:

| Model | Input | NPU (QNN) | GPU (QNN) | CPU (all cores) |
|---|---|---|---|---|
| e5-small | 64 tokens | **1.39** | | |
| e5-small | 128 tokens | **2.59** | 15.3 | 63.6 |
| e5-small | 256 tokens | 6.45 | | |
| e5-small | 512 tokens | 18.3 | | |
| e5-small | A real short note (4 to 19 tokens), unpadded | n/a, fixed shape only | | **8.6** (p95 31) |
| e5-small | A real long note (107 tokens), unpadded | n/a | | 51.7 |
| all-MiniLM-L6-v2 | 128 tokens | 1.58 | 8.7 | 23.5 |

**How I checked it's really the NPU:** fallback was disabled, so an unsupported graph would have failed. Latency scales with length as attention should. Vectors differ very slightly from the CPU's (cosine at least 0.9998), which fits the fp16 path. There's no NPU counter set on this build, so I have no direct utilisation reading.

**Power modes made no measurable difference to latency:** all were about 2.6 ms for e5 at 128 tokens, likely because the task is too small. All tests ran on mains power.

**The result contradicts a line in the docs.** The [ONNX Runtime QNN page](https://onnxruntime.ai/docs/execution-providers/QNN-ExecutionProvider.html) says the HTP backend supports only quantized models, yet also documents `enable_htp_fp16_precision`, default on. Both models ran as ordinary float32 files, with no conversion. **No quantization was needed.**

### Other costs [Measured here]

| Item | NPU | CPU |
|---|---|---|
| First load | 4 to 7 s to compile, peaks around 2,100 MB private memory in the Python test | 0.7 s |
| Later loads | **0.33 s** from a 225 MB compiled file | 0.7 s |
| Memory while loaded | No visible growth in private memory | **About 460 MB** private |
| Input shape | Fixed at compile time; each extra length needs its own compile | Any length |
| Process CPU during a call | About one full core | All cores |
| 5,000 notes full rebuild (arithmetic) | 5,000 × 2.6 ms = **13 s** | 5,000 × 12.4 ms mean = **1 minute** for short notes, up to 5,000 × 64 ms = 5 minutes for 128-token text |

**Is the NPU worth it?** For latency, no: a person can't tell 3 ms from 9 ms. Its real advantages are lower memory and faster rebuilds.

**Battery use is unmeasured.** One core stays busy during each call, so the power saving may be smaller than expected. I found no way to measure this on mains power.

### Which route [Source, then Inference]

| Route | Works for this NPU? | Status | Verdict |
|---|---|---|---|
| **ONNX Runtime with the QNN plugin**, via function pointers | **Yes, measured** | Wavee's AI lyrics already uses this approach, downloading the [`onnxruntime` and `onnxruntime-qnn` wheels](../../../fgpu/WaveeMusic/src/apps/Wavee/AiLyrics/AiLyrics.PackManifest.cs) at the user's request | **Use it** |
| **Windows ML** | Should, per [its docs](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/supported-execution-providers). Not tested | [Windows-maintained ONNX Runtime, with execution providers delivered by Windows](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/overview). Avoids shipping Qualcomm's files, and would reach Intel and AMD NPUs | Worth revisiting for a generic product. It needs a Windows App SDK package, which the [engine's charter excludes](../../../fgpu/WaveeMusic/docs/plans/wavee/ai-lyrics-sync-implementation.md) |
| **DirectML** | No NPU route to rely on | [Maintenance mode, no new features](https://github.com/microsoft/DirectML) | No |
| **QNN on the GPU** | Yes, measured, and slower than the NPU | | No |
| **Managed `Microsoft.ML.OnnxRuntime` wrappers** | Not tested under NativeAOT | | Not needed |

**Gating:** use [`ComputeAdapters.Enumerate(ComputeAdapterKind.Npu)`](../../../fgpu/fluent-gpu/src/FluentGpu.WindowsApi/Devices/ComputeAdapters.cs) and its `Vendor`, checked with `fg.py api`. Don't gate on ONNX Runtime's vendor string: it came back empty here too, which matches [Wavee's notes](../../../fgpu/WaveeMusic/docs/plans/wavee/ai-lyrics-sync-implementation.md).

**Things to handle:**

- Compile once and cache, as Wavee does. Compile off the UI thread, because of the 2 GB peak.
- Recreate the session on [`ENGINE_ERROR` after an HTP restart](https://onnxruntime.ai/docs/execution-providers/QNN-ExecutionProvider.html) [Source].
- Keep CPU working on every PC. Other users won't have this chip, and CPU vectors match NPU vectors closely enough to share one index (cosine at least 0.9998). Record the model and version with the vectors.
- Wavee's plan flags the [Qualcomm library licence for redistribution as still to verify](../../../fgpu/WaveeMusic/docs/plans/wavee/ai-lyrics-sync-implementation.md). The same applies here.

**What I left behind:** about 2.1 GB in `C:\WAVEE\scratch\embeddings`, including ARM64 Python, the wheels, two models and a NuGet cache. Nothing was installed system-wide, and it's safe to delete.

---

## 2. Hosted options versus local

**Keep-local notes are ruled out for every hosted option, always.** They have no cards either, since cards come from an AI call. A local model is the only way they could ever be matched.

Prices are per million tokens. "Full index" is 5,000 notes × 260 tokens (200 of text plus 60 of card) = 1.3M tokens, so all of these are small.

| | Dimensions | Price | Full index | Who receives the text | Training on your text | Retention | Key setup |
|---|---|---|---|---|---|---|---|
| **Local `multilingual-e5-small`** | 384 | Free | $0 | **Nobody** | None | Stays on your PC | None; a 470 MB model |
| **Voyage `voyage-4-lite`** | [1024, or 256, 512, 2048](https://www.mongodb.com/docs/voyageai/models/text-embeddings/) | [$0.02](https://www.mongodb.com/docs/voyageai/management/billing/) | $0.03 | **MongoDB** | [Possible. An opt-out toggle exists, but **needs a payment method on file**](https://www.mongodb.com/docs/atlas/tutorial/manage-organization-settings/). Default not stated | Not stated for Atlas; [payloads kept for logging](https://www.mongodb.com/company/blog/product-release-bulletin/europe-data-residency-for-atlas-embedding-reranking) are mentioned | Atlas account, 200M free tokens |
| **Voyage `voyage-4`** | Same | $0.06 | $0.08 | Same | Same | Same | Same |
| **Voyage `voyage-4-large`** | Same | $0.12 | $0.16 | Same | Same | Same | Same |
| **OpenAI `text-embedding-3-large`** | [3072, can be shortened](https://developers.openai.com/api/docs/guides/embeddings) | [$0.13](https://developers.openai.com/api/docs/models/text-embedding-3-large) | $0.17 | **OpenAI** | [No, by default](https://developers.openai.com/api/docs/guides/your-data) | [Up to 30 days for abuse monitoring; ZDR eligible](https://developers.openai.com/api/docs/guides/your-data) | OpenAI account |
| **Cohere Embed 5 Fast / Pro** | [2048 down to 256](https://cohere.com/blog/embed-5) | $0.08 / $0.12 | $0.10 / $0.16 | **Cohere** | Not confirmed for these endpoints | [30 days for logged prompts](https://cohere.com/enterprise-data-commitments) | Cohere account |
| **Google `gemini-embedding-2`** | [128 to 3072](https://ai.google.dev/gemini-api/docs/pricing) | $0.20 | $0.26 | **Google** | [Not on paid; yes on unpaid, with human review. In the EU, paid terms apply to all use](https://ai.google.dev/gemini-api/terms) | [Kept for a limited period](https://ai.google.dev/gemini-api/terms), no number given | Google account |

**Quality for this use:**

| Question | What I found | Label |
|---|---|---|
| Is one hosted model better overall? | Voyage reports [`voyage-4-large` ahead of Gemini 001 by 3.87%, Cohere v4 by 8.20% and OpenAI 3-large by 14.05%](https://blog.voyageai.com/2026/01/15/voyage-4/) across 29 datasets. Cohere reports [itself ahead of Voyage, and OpenAI lowest in every language row](https://cohere.com/blog/embed-5) | [Source], **vendor-run and conflicting** |
| Hosted versus local, independently | One study: [Google's hosted model led on English, but was almost tied with `multilingual-e5-large` on short Italian passages (0.282 versus 0.279)](https://arxiv.org/html/2605.23618v1), at 7 to 14 times the latency | [Source], one study, no Dutch |
| **Dutch, local** | MTEB-NL retrieval score: [e5-small 53.1, E5-NL small 56.0, e5-large 59.1, bge-m3 60.0, paraphrase-MiniLM 37.7](https://arxiv.org/html/2509.12340v1). Partly translated data | [Source] |
| **Dutch, hosted** | **Nothing found.** Cohere's table covers ten other languages, and the part of MTEB-NL that I read lists no hosted models | Gap |
| **Dutch, BM25** | [BEIR-NL finds BM25 still competitive, beaten only by larger dense models](https://arxiv.org/pdf/2412.08329) | [Source] |
| **Mixed Dutch and English, any model** | **No benchmark found.** Smoke test: e5-small found the translated note first in 24 of 24 tries | [Smoke test] |
| **Typos, any current model** | **No measurement found.** Older dense models lost a lot: [R@50 fell from 73 to 46 on MS MARCO](https://arxiv.org/html/2205.02303). Smoke test below | [Source], older models |

**Smoke-test results on typos** (a typo in about 40% of words of four letters or more):

| Task | Trigram | e5-small | all-MiniLM (English) |
|---|---|---|---|
| Find the original note, recall@1 | **1.00** | 0.93 | 0.95 |
| Find the translated note, recall@5 | 0.29 | **1.00** | 0.25 |

So trigram catches typos within a language, and e5 catches meaning across languages. They do different jobs.

**The privacy tradeoff, plainly.** Local embeddings send nothing. A hosted model sends every non-Keep-local note's text, or at least its card, to a second company, in addition to Anthropic. Cards are short, but they say what a note assumes and affects, so they are still personal content. If you add Voyage, add a payment method and turn the training toggle off **before the first call**.

**My view:** cost isn't the argument. Hosted models might well win on real data, but the first-order gain here seems to come from comparing cards rather than from choosing a better embedder. So test one hosted model before adding a second company. Your decision.

**Local model alternatives** [Source, plus Hugging Face metadata]:

| Model | Licence | Size | Why or why not |
|---|---|---|---|
| **`multilingual-e5-small`** | MIT | 470 MB ONNX | **Default.** Multilingual, includes Dutch, and measured here |
| `e5-small-trm-nl` | [MIT](https://huggingface.co/clips/e5-small-trm-nl) | 82 MB, no ONNX listed | Better Dutch score and much smaller, but needs conversion, and its English quality is unmeasured. Worth a test |
| `all-MiniLM-L6-v2` | Apache 2.0 | 90 MB | English only. Matches e5 on English cards in the smoke test, but failed on Dutch |
| `voyage-4-nano` | [Apache 2.0](https://huggingface.co/voyageai/voyage-4-nano) | GGUF, no ONNX listed | Would need conversion, and is untested |
| `bge-m3` | MIT | 2.27 GB | Best Dutch score here, too large for this |

---

## 3. What gets embedded, and how candidates are chosen

### Note text, cards or both

**Smoke-test results, gold note in the top 5 (n=24, confidence intervals are wide):**

| Method | Translated pairs | Surprising pairs |
|---|---|---|
| Trigram on note text | 0.46 | 0.08 |
| all-MiniLM on note text | 0.17 | 0.17 |
| **e5-small on note text** | **1.00** | 0.21 |
| **e5-small on English cards**, `query:` new card against `passage:` others | n/a | **0.96** |
| all-MiniLM on English cards | n/a | 0.96 |

*The last two rows search only the 31 notes with cards, where random picks would score about 0.17. On those same 31 notes, e5 on note text scores 0.42 and trigram 0.17.*

| Question | Answer | Label |
|---|---|---|
| Embed text, cards or both? | **Cards for Connections.** Note text for Coarse related links and Keep-local matching, when those need it | [Smoke test], [Inference] |
| Do English cards reduce the need for a multilingual model? | **For cards, yes:** an English-only model did as well. **For note text, no:** it failed on Dutch. One multilingual model covers both, so use that | [Smoke test] |
| Does asymmetric matching work (`affects` against `assumes` and `needs`)? | **Not better than whole-card matching in this test.** Top 5, same prefixes on both sides: e5 0.58 versus 0.88; MiniLM 0.92 versus 0.96. Combining it with whole-card on MiniLM reached 1.00, though that may be noise at n=24. Leave it out of v1, and test it with real cards | [Smoke test] |

**Why asymmetric matching may still matter later:** it is the only method that encodes the idea behind the owner's example. Short phrases give an embedding model less to work with, which may explain e5's weak result, and whole cards may be doing well only because I wrote them in one voice.

### Rank fusion: don't merge, take a union

**Equal rank fusion with trigram made results worse.** For translated pairs, RRF placed the gold note in the top 10 only 62% of the time, against 100% for e5 alone. A list that's mostly noise for a query drags the good list down. [Bruch et al.](https://arxiv.org/abs/2210.11934) report that RRF is [sensitive to its parameters and beaten by convex combination](https://arxiv.org/abs/2210.11934) [Source].

**For a candidate list that an AI then reads, a union fits better** [Inference, with Smoke test support]. Take each method's top few and remove duplicates. Then no single method can push out another's find:

| Candidate set of 10 | Translated | Surprising |
|---|---|---|
| e5 alone | 1.00 | 0.38 |
| RRF of trigram and e5 | 0.62 | 0.25 |
| Union: e5 7, trigram 3 | 1.00 | 0.29 |

Note this is on note text. For surprising pairs, cards are what matter: cards top 8 scored 1.00 in the test.

### How step 3 should work

| Stage | What it does | Size |
|---|---|---|
| 1. Local narrowing, only above 300 notes | Cards by embedding, with `query:` for the new card and `passage:` for the rest | 300 |
| 2. The AI reads the cards | Inside the Organize call, and it picks candidates | Up to 8 |
| 3. Safety net | Add local top matches the AI didn't pick | Up to 3 |
| 4. Connect and Check | Reads full notes, and may abstain | At most 3 shown |

**Should reading cards be a separate call?** I'd put it in the Organize call [Inference]. That call already runs, and Project and Build on benefit from the same view. No extra call means no extra latency. Test this against a separate call if quality suffers.

**Reading all cards is not automatically better.** No source tests picking links from a long list of cards. Anthropic reports that [long-context prompts do better with the data first and the question at the end](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices) [Source], so order the prompt that way. Attention over 1,500 short cards is unmeasured, which is why I'd cap at 300 until measured.

**Cost of reading cards inside Organize** (assumed 60 tokens per card, 100 extra output tokens) [Inference]:

| Cards read | Extra tokens | Sonnet 5.5 | Per year (6 runs a day) | Haiku 5.5 instead |
|---|---|---|---|---|
| 50 | 3,000 | $0.006 + $0.001 = **$0.007** | $15 | $0.0004 |
| 300 | 18,000 | $0.036 + $0.001 = **$0.037** | $81 | $0.002 |
| 1,500 | 90,000 | $0.180 + $0.001 = **$0.181** | $396 | $0.009 |
| 5,000 | 300,000 | $0.600 + $0.001 = **$0.601** | $1,316 | Over Haiku's 100K line: $0.15 |

**The same limit applies to the first search.** "All titles" at 20 tokens each costs 5,000 × 20 × $2/M = $0.20 per capture on Sonnet. Keep the visible cost and cap, and narrow titles the same way when the cap is reached.

### Reranking

**No separate reranker for v1** [Inference]. Connect already judges every candidate with a stronger model and may abstain, so a reranker would be judging the same list first.

| Option | Cost | Note |
|---|---|---|
| [Voyage `rerank-3`](https://www.mongodb.com/docs/voyageai/models/rerankers/) | [$0.05/M](https://www.mongodb.com/docs/voyageai/management/billing/); a tiny fraction of a cent per capture | Sends text to a second company; its [multilingual support isn't stated for this model](https://www.mongodb.com/docs/voyageai/models/rerankers/) |
| [Cohere Rerank](https://docs.cohere.com/changelog/rerank-v4.0) | [Priced per search](https://docs.cohere.com/docs/how-does-cohere-pricing-work), current rate not retrieved | Same concern |
| **Claude, in the existing calls** | Already paid for | **Use this** |

[BEIR-NL found BM25 plus a reranker on par with the best dense models](https://arxiv.org/pdf/2412.08329) on Dutch [Source]. So a reranker could matter if retrieval recall is good but reading capacity is the limit, which isn't this case at 300 cards.

---

## 4. Measuring with real data

**Run this when at least 30 labelled pairs exist, likely after a few dozen Thoughts and some weeks of accepted and dismissed suggestions.** It needs no test project: it's one plain script, kept outside the repo until the repo's policy is decided, because its data holds personal notes.

### Building the labelled set

| Source | Label | Note |
|---|---|---|
| Accepted Connections | Positive | Biased toward what the system already shows |
| Links the person made by hand | Positive | Free of that bias |
| Dismissed suggestions marked "Forced" | Negative | |
| Dismissed for other reasons | Skip | "Knew it" isn't a retrieval failure |
| **Pooled review:** the top 5 from each method, not yet labelled, shown once, shuffled, and blind to method | Either | This is how [TREC-style evaluation copes with incomplete labels](https://dl.acm.org/doi/10.1145/1008992.1009000). **It's the most important step**, since it finds pairs that no shipped method surfaced. About 10 to 15 minutes |
| Decoy pairs the person confirms are unrelated | Negative | Tests false matches |

**Mark which positives are surprising.** A surprising pair is one where the note-text rank is worse than 10. That's the class this whole design exists for, so report it separately.

**Time order matters.** When testing a pair, the search may see only notes older than the newer note, as it would have at capture.

### Language and typo variants

| Variant | How | Tests |
|---|---|---|
| **Natural Dutch-English pairs** | From the labelled set | Real mixed use. **Weight this over the rest** |
| **Translated copies** | The person translates about 10 Thoughts, or Claude does, on non-Keep-local notes | Cross-language recall |
| **Typos** | Seeded script: 40% of words of four letters or more get a swap, deletion, substitution or doubled letter | Robustness |
| **Real typos** | Keep any typed by the person | The generator is only a stand-in |

### Methods to compare

| Method | API needed | What it answers |
|---|---|---|
| Trigram on text | No | The baseline |
| e5-small on text | No | Does text ever beat cards? |
| **e5-small on cards** | No | The recommended narrowing |
| e5-small on cards plus asymmetric lane | No | Does asymmetry add anything real? |
| **AI reading all cards** | Claude | Is reading better than embeddings, and at what size? |
| **One hosted model on cards**: `voyage-4` with training turned off, or `text-embedding-3-large` | A second company | Is a hosted model worth it? **Needs your approval first** |

### Metrics

| Metric | Why |
|---|---|
| **Recall@8 and @15 of surprising pairs** | The main number |
| Recall@8 and @15 of all known-good pairs | General health |
| Recall@8 by language pairing (nl-nl, en-en, nl-en) | Finds language gaps |
| Recall@8, clean versus typo | Typo robustness |
| **Whether accepted surprising pairs survive to Connect** | End-to-end check, using recorded candidates |
| Decoy rate | Candidates shouldn't include known-unrelated notes in the top ranks |
| Latency p50 and p95; cost per capture | The owner wants these visible |

### What size is enough

Simulated power of a paired comparison (exact sign test on pairs where the methods differ, two-sided at 0.05, moderate correlation between methods, 3,000 runs) [Measured here, on simulation, not notes]:

| True recall | n=20 | n=30 | n=50 | n=100 | n=200 |
|---|---|---|---|---|---|
| 0.6 versus 0.9 | 0.55 | **0.85** | 0.98 | 1.00 | 1.00 |
| 0.6 versus 0.8 | 0.19 | 0.37 | 0.65 | **0.95** | 1.00 |
| 0.7 versus 0.8 | 0.04 | 0.09 | 0.18 | 0.41 | 0.75 |
| 0.8 versus 0.9 | 0.03 | 0.09 | 0.27 | 0.58 | **0.90** |

| Labelled pairs | What you can trust |
|---|---|
| **About 30** | Only large gaps of 30 points or so. Enough to reject a method that's clearly failing |
| **About 100** | Gaps of 20 points |
| **About 200 or more** | Gaps of 10 points |

Even a single recall figure stays loose: 0.8 measured on 30 pairs has a 95% interval of [0.63, 0.90].

**Three cautions:**

- Pairs that share a Thought aren't independent. Count by Thought when you can, so these figures are optimistic.
- At the owner's pace, 200 pairs may take a long time. So early on, decide only on large gaps, and otherwise take the simpler or more private option.
- This agrees in spirit with [TREC work on topic-set size](https://www.nist.gov/publications/effect-topic-set-size-retrieval-experiment-error), where small sets give unreliable conclusions, and [Anthropic's advice that early large effects show up in small samples](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents) [Source].

### Script interface (draft, not built)

```text
retrieval-eval  build-labels  --vault <path> --decisions <.thoughts/...> --out <dir>
retrieval-eval  add-variants  --labels <file> --typos 0.4 --seed 7
retrieval-eval  run           --labels <file> --method trigram-text|e5-text|e5-cards|e5-cards-asym
                              [--device npu|cpu] [--k 8,15] --out <dir>
retrieval-eval  run           --method ai-all-cards --model <id> --budget-usd 2.00 --confirm-paid
retrieval-eval  run           --method hosted --provider voyage|openai --model <id>
                              --budget-usd 1.00 --confirm-paid --confirm-second-company
retrieval-eval  pool          --runs <dir> --top 5 --out review.csv     # shuffled, method hidden
retrieval-eval  score         --labels <file> --runs <dir> --by surprising,language,typo
                              --paired-against e5-cards                  # sign test, by Thought
```

**Rules for the script:**

- Keep-local notes are filtered through the same function the app uses. A run stops if one appears.
- Paid methods print the token count, cost, and which company receives text, then need their confirm flags.
- Output contains IDs and ranks, not note text, apart from `review.csv`, which stays local.
- No test framework, and no `dotnet test`.

### Free shadow data, starting at v1

At each capture, log the local rank of every card the AI picked, and whether the person accepted it, without any note text. If what the AI picks and the person accepts is always in the local top 20, embeddings could safely narrow much further. If it isn't, the AI is finding what embeddings can't, and that is the 300 cap's justification.

---

## 5. Fine-tuning

**At a few dozen notes, tune almost nothing.** Anything you fit will mostly fit noise. Get the fixed choices right, then measure.

### Order

| # | Knob | Do | Basis |
|---|---|---|---|
| 1 | **Labelled set and metric** | Build them before changing anything | [Inference] |
| 2 | **What gets embedded** | Cards for Connections. Test with real model cards first, since it's the largest effect seen | [Smoke test] |
| 3 | **Prefixes** | **Set once, not a tuning knob.** [The model card requires `query:` or `passage:`](https://huggingface.co/intfloat/multilingual-e5-small). `query:` for the new card, `passage:` for stored ones. In the smoke test this raised card recall@5 from 0.88 to 0.96, and translation recall@1 from 0.92 to 1.00 | [Source], [Smoke test] |
| 4 | **Card wording** | Test the prompt for card vocabulary before touching retrieval. Bad cards can't be fixed downstream | [Inference] |
| 5 | **Size of each list** | Top 8 from the AI, up to 3 added locally. Change only when recall@8 misses pairs that recall@15 finds | [Inference] |
| 6 | **Cards the AI reads** | The 300 cap. Raise it if shadow data shows accepted picks ranked below it locally | [Inference] |
| 7 | **Embedding model** | e5-small first. Then try the hosted model, and E5-NL if Dutch recall lags | [Inference] |
| 8 | **Asymmetric lane** | Add only if it finds accepted pairs that whole cards miss | [Smoke test] |

### Leave alone at this scale

| Knob | Why |
|---|---|
| **Score floors on cosine** | **Don't use absolute floors.** In the smoke test e5's cosines for true and false card pairs overlapped: true pairs averaged 0.89 (lowest 0.859), and unrelated averaged 0.835 (95th percentile 0.877). Use ranks, then let the AI abstain. The earlier document's "floor" should be read this way. Skip the call only when a card is empty |
| **RRF constant `k`** | Not used, since lists are combined by union. [It's a sensitive setting](https://arxiv.org/abs/2210.11934) that can't be tuned on few queries |
| **FTS5 column weights** | Titles and short bodies are nearly the same text |
| **Chunking** | Leave at one unit up to about 400 tokens. Cards don't need chunks. Chunk long bodies by headings only when they exist. NPU compile length is fixed at 128 (cards fit), so long text needs a chunk anyway |
| **Vector dimensions, quantization, ANN index** | A flat scan of 5,000 × 384 takes 0.53 ms in Python here [Measured here], so an index solves nothing. Keep float32 |
| **Separate reranker** | Connect does this job |
| **HTP performance mode** | No measurable effect on latency here |
| **NPU sequence length** | 128 is enough. Short inputs save little, at 1.4 versus 2.6 ms |

### Accept and dismiss as feedback

| Use | When | Risk |
|---|---|---|
| **Suppress a dismissed pair** | From day one | None; it's an exact rule |
| **Add accepted pairs to the labelled set** | From day one | Biased, so balance with pooled review |
| **Show recall per method to the owner** | After 30 pairs | Can mislead if read too closely |
| **Boost a Project or Person pair by acceptance rate** | Not before about 200 decisions | Popular notes attract more links |
| **Learned ranking weights or fine-tuned embeddings** | Not at this scale | Would memorise the person's few examples, and hides the surprising pairs, which by nature look unlike past acceptances |

**The trap in feedback:** training on acceptances teaches the system to repeat what's already been accepted. Surprising links are what the owner wants, so feedback should stay a filter, not a ranking signal.

### What overfits a few dozen notes

- Any floor, weight or list size chosen by looking at the same pairs you then report on.
- Swapping models because one or two pairs moved.
- Rewriting the card prompt until a known example such as the credit-system pair ranks first. It becomes a fixture and says nothing about the next pair.
- Re-rating Connections you've already seen, since they [stop being new once seen](2026-11-01-organizing-workflow.md).
- Choosing hosted over local on a gap below 30 points at 30 pairs.

**A simple guard:** keep about a third of labelled pairs untouched, and look at them only to confirm a decision. With fewer than 30 pairs, make no tuning decisions at all.

---

## What would change the recommendation

| If | Then |
|---|---|
| Real cards score far below the smoke test on surprising pairs | Fix the card prompt first. If that fails, rely on the AI reading cards, and raise the cap despite the cost |
| AI reading all cards doesn't beat local top 8 by a clear margin | Drop reading cards, saving $0.04 per capture, and use local top 8 alone |
| Accepted AI picks often rank below 300 locally | Raise the cap, or test a hosted embedder |
| A hosted model beats e5 by 20 points or more on 100 pairs, and you accept the second company | Use it for cards, keeping the local model for Keep-local and offline |
| Dutch notes score clearly lower than English | Try E5-NL or a larger e5, which needs a new NPU test |
| Battery testing shows the NPU costs more than CPU | Use CPU, with lazy loading |
| The licence check blocks downloading the QNN files | Use Windows ML, or CPU only |

## Could not verify

- **How well this works on real notes.** All quality results are from 60 fake notes, with cards I wrote. **The biggest risk is that card matching looks much better here than it will be**, because I wrote both sides knowing the answer.
- **AI reading all cards, and every hosted model.** Not run, since no paid calls were allowed. There are no quality numbers for them on this task.
- **Dutch or mixed-language quality for hosted models.** I found no independent data.
- **Typo robustness of current models.** Published numbers are for older models.
- **NPU power use.** Not measured; tested on mains.
- **Direct proof of NPU execution.** There's no counter on this build. The evidence is disabled fallback, latency and the small numeric difference.
- **Other NPUs.** Intel and AMD via Windows ML, and Windows ML itself, weren't tested.
- **Memory figures** come from a Python process with a large baseline, so treat the changes as approximate.
- **Tokenizer timing and match** cover 5 and 105 strings. Test more before relying on them, especially very long text.
- **Voyage retention and default training setting.** The [older Voyage FAQ](https://docs.voyageai.com/docs/faq) describes opting out as giving zero-day retention, but I couldn't confirm that applies to the Atlas API. Ask MongoDB in writing if it matters.
- **Cohere's data terms for these endpoints**, and its current rerank price.
- **Papers.** I read abstracts or partial text for most of them. BEIR-NL's figures weren't extracted, only its conclusions. The topic-set paper was seen as a summary.
- **Token estimates:** 60 per card is an assumption.
- **Test artefact:** an early benchmark run overlapped with a stray process, and an overflow affected early power figures. I discarded those results, and everything above comes from clean reruns.

## Sources

**This workspace**

- [`AiLyrics.Ort.cs`](../../../fgpu/WaveeMusic/src/apps/Wavee/AiLyrics/AiLyrics.Ort.cs), [`AiLyrics.PackManifest.cs`](../../../fgpu/WaveeMusic/src/apps/Wavee/AiLyrics/AiLyrics.PackManifest.cs), [`ai-lyrics-sync-implementation.md`](../../../fgpu/WaveeMusic/docs/plans/wavee/ai-lyrics-sync-implementation.md)
- [`ComputeAdapters.cs`](../../../fgpu/fluent-gpu/src/FluentGpu.WindowsApi/Devices/ComputeAdapters.cs)
- Scripts and raw results: `C:\WAVEE\scratch\embeddings\scripts`, `C:\WAVEE\scratch\embeddings\runs`, `C:\WAVEE\scratch\embeddings\aot`

**Runtime**

- [ONNX Runtime QNN execution provider](https://onnxruntime.ai/docs/execution-providers/QNN-ExecutionProvider.html)
- [What is Windows ML?](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/overview), [Windows ML execution providers](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/supported-execution-providers)
- [DirectML repository](https://github.com/microsoft/DirectML)

**Hosted models**

- Voyage: [text models](https://www.mongodb.com/docs/voyageai/models/text-embeddings/), [billing](https://www.mongodb.com/docs/voyageai/management/billing/), [rerankers](https://www.mongodb.com/docs/voyageai/models/rerankers/), [training data](https://www.mongodb.com/docs/voyageai/management/model-training-data/), [organization settings](https://www.mongodb.com/docs/atlas/tutorial/manage-organization-settings/), [Voyage 4 post](https://blog.voyageai.com/2026/01/15/voyage-4/), [EU residency post](https://www.mongodb.com/company/blog/product-release-bulletin/europe-data-residency-for-atlas-embedding-reranking), [FAQ](https://docs.voyageai.com/docs/faq)
- OpenAI: [model page](https://developers.openai.com/api/docs/models/text-embedding-3-large), [embeddings guide](https://developers.openai.com/api/docs/guides/embeddings), [data controls](https://developers.openai.com/api/docs/guides/your-data)
- Cohere: [Embed 5](https://cohere.com/blog/embed-5), [pricing notes](https://docs.cohere.com/docs/how-does-cohere-pricing-work), [data commitments](https://cohere.com/enterprise-data-commitments), [Rerank 4](https://docs.cohere.com/changelog/rerank-v4.0)
- Google: [pricing](https://ai.google.dev/gemini-api/docs/pricing), [terms](https://ai.google.dev/gemini-api/terms)
- Anthropic: [Embeddings](https://platform.claude.com/docs/en/build-with-claude/embeddings), [Prompting best practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices), [Demystifying evals](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents)

**Local models**

- [multilingual-e5-small](https://huggingface.co/intfloat/multilingual-e5-small), [all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2), [e5-small-trm-nl](https://huggingface.co/clips/e5-small-trm-nl), [voyage-4-nano](https://huggingface.co/voyageai/voyage-4-nano), [bge-m3](https://huggingface.co/BAAI/bge-m3)

**Papers**

- [MTEB-NL and E5-NL](https://arxiv.org/html/2509.12340v1)
- [BEIR-NL](https://arxiv.org/pdf/2412.08329)
- [Benchmarking Google Embeddings 2 against open-source models](https://arxiv.org/html/2605.23618v1)
- [Robustness of dual encoders against misspellings](https://arxiv.org/html/2205.02303)
- [An analysis of fusion functions for hybrid retrieval](https://arxiv.org/abs/2210.11934)
- [Retrieval evaluation with incomplete information](https://dl.acm.org/doi/10.1145/1008992.1009000)
- [The effect of topic set size on retrieval experiment error](https://www.nist.gov/publications/effect-topic-set-size-retrieval-experiment-error)
