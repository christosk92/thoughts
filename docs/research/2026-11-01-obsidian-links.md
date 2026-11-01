# Obsidian: links in properties and one note per relationship

Tested 2026-11-01 on Windows 11. Marks: **TESTED** (run here), **DOCUMENTED** (official help or API docs), **UNVERIFIED** (not run and no official source).

## Answer

- Links in properties count as real links for backlinks, resolved links and the graph, including links nested in objects.
- A Connection note appears as a node between its two Thoughts.
- Hiding Connection notes removes the line between the Thoughts. Any Connection that must still show as a line after filtering needs a direct link.
- Renames do update property links, but Obsidian asks first by default. It also rewrites the referring note's whole YAML.

## Test setup

| Item | Value |
|---|---|
| Versions | Installed 1.14.4 ran the main tests. Its updater then fetched `obsidian-1.14.5.asar` into the scratch profile, and later launches ran 1.14.5. |
| Run on 1.14.4 | Everything not listed in the next two rows |
| Rechecked on 1.14.5 | All Q1 link shapes, a nested rename |
| Run only on 1.14.5 | Seeded `app.json` and `graph.json`, an external rename while closed, the canonical round trip, awkward file names, quoted and partial values in colour queries, the recommended filter |
| Isolation | `--user-data-dir=C:\WAVEE\scratch\obsidian-test\profile`, with an `obsidian.json` listing only the test vault. The isolated profile worked. `%APPDATA%\obsidian\obsidian.json` had the same hash before and after. |
| Inspection | Chrome DevTools Protocol on port 9333. Graph edges were read from `view.renderer.links`, and screenshots were checked by eye. |
| Limits | No community plugins. Each timing test (Q5 external edits) ran once. The test vault was opened through `obsidian.json`, not created in the app's vault picker. |

## Q1: which property links count

"Backlinks" means `getBacklinksForFile`. "Graph" means an edge in the renderer. All TESTED.

| Form | `frontmatterLinks` key | Resolved | Backlinks | Graph |
|---|---|---|---|---|
| `project: "[[X]]"` | `project` | Yes | Yes | Yes |
| `from: ["[[A]]", "[[B]]"]` | `from.0`, `from.1` | Yes | Yes | Yes |
| Block list `- "[[X]]"` | `from.0` | Yes | Yes | Yes |
| `links: [{target: "[[X]]", why: "..."}]` | `links.0.target` | Yes | Yes | Yes |
| `meta: {owner: "[[X]]"}` in block form | `meta.owner` | Yes | Yes | Yes |
| `a: {b: [{c: "[[X]]"}]}` in block form | `a.b.0.c` | Yes | Yes | Yes |
| `"[[X\|Shown]]"`, `"[[X#Part]]"` | `project` | Yes | Yes | Yes |
| `project: [[X]]` (no quotes) | none, parsed as a nested list | No | No | No |
| `note: "see [[X]] here"` (link inside text) | none | No | No | No |
| `"[Shown](X%20Y.md)"` (encoded Markdown link) | yes | Yes | not checked | not checked |
| `"[Shown](X Y.md)"` (raw space) | yes, but the target is cut to `X` | No | No | Wrong node `X` |

- The backlinks pane shows property links as `from: [[A]] [[B]]`, with the matching link highlighted (TESTED, see screenshot).
- Nested links work as links but display badly. The Properties panel shows `[{"target":"[[X]]",...` as orange JSON text, with no clickable link (TESTED, see screenshot).
- Official help agrees on the display: nested properties are [listed as not supported, with source mode recommended for viewing them](https://obsidian.md/help/properties) (DOCUMENTED).
- The help also says [links in text properties must be quoted](https://obsidian.md/help/properties) and [links in lists must be quoted](https://obsidian.md/help/properties) (DOCUMENTED). Testing agrees.
- The changelog says [properties can contain internal links and backlink entries now show them](https://obsidian.md/changelog/2023-08-31-desktop-v1.4.5/) (DOCUMENTED). It does not mention the graph. No official page I found says the graph includes property links, so that part rests on testing.

**Sources that disagree with testing.** Both are old and were not reproduced on these versions:

- A [2022 thread](https://forum.obsidian.md/t/how-to-specify-links-within-frontmatter/39319) says frontmatter links are missing from the graph and do not update on rename.
- A [feature request last active in January 2024](https://forum.obsidian.md/t/frontmatter-link-in-the-graph/39851) makes the graph claim too. Its last post reports that quoting fixed it, which matches the unquoted row above.

## Q2: a Connection note in the graph

All TESTED. The test had two pairs:

- Alpha and Beta linked only by a Connection.
- Gamma and Delta linked by a Connection and by a direct `connected` property on each.

| Graph filter | Connection nodes | Alpha-Beta | Gamma-Delta | Side effect |
|---|---|---|---|---|
| None | Shown, each with an edge to both Thoughts | Through the Connection node | Through the node, plus direct edges | The direct and indirect lines form a triangle |
| `-path:Connections` | Hidden | **No line** | Direct line kept | Also hid the Thought `Connections matter` |
| `-path:"Connections/"` | Hidden | **No line** | Direct line kept | Thought kept |
| `-[between]` | Hidden | **No line** | Direct line kept | Thought kept |
| `-[state:suggested]` | Only suggested ones hidden | **No line** | Connection node and direct lines kept | Thought kept |
| `-[state:accepted] -[state:rejected]` (1.14.5) | Suggested ones shown, others hidden | Through the suggested node | Direct line only | The recommended setup |

- So yes, accepted Connections must write a direct link if they are hidden. Filtering removes the node and its edges, and Obsidian does not draw a replacement line between the Thoughts.
- With Orphans off and Connections hidden, Alpha and Beta disappear entirely (TESTED).
- The help says [filters control which nodes show](https://obsidian.md/help/plugins/graph), but [it does not say what happens to links](https://obsidian.md/help/plugins/graph) (DOCUMENTED, silent on this point).
- **Path filters match any part of the path, ignoring case.** In testing, `-path:Ren` also hid `Thoughts/Beta renamed.md` (TESTED). The help describes [`path:` as finding text in a file's path](https://obsidian.md/help/plugins/search) (DOCUMENTED).
- **Local graph.** It keeps its own filter, which was empty even with a filter on the global graph, so Connection nodes still appear there. From Alpha, Beta only appears at depth 2 (TESTED). A [June 2026 feature request](https://forum.obsidian.md/t/an-option-to-disable-frontmatter-links-to-show-up-in-graph-view/114940) reports the same clutter from property links in the local graph and asks for a toggle.
- **Backlinks are unaffected by graph filters.** A Thought lists both the `from` note and the Connection note under Linked mentions (TESTED).

## Q3: colouring nodes by property

Yes (TESTED). Checked through each node's colour value and by eye. Colour groups use the same [search syntax as `[property:value]`](https://obsidian.md/help/plugins/search) (DOCUMENTED).

| Query | Result |
|---|---|
| `["state":suggested]`, `[state:suggested]`, `["state":"suggested"]` | The two suggested Connections |
| `["status":Doing]`, `[status:Doing]`, `[status:doing]`, `["status":"Doing"]` | All Doing Thoughts, so matching ignores case |
| `[state:sugg]` | Also matches, so **values match on part of the text** |
| `[state]` | Every note with the property |
| `[project:Atlas]`, `[project:"[[Atlas]]"]` | The note whose `project` links to Atlas |

- Where two groups match a node, the first in the list wins (TESTED).
- A colour only shows on a visible node. If all Connections are hidden, `state` colours do nothing.
- Choose values that are not contained in each other. For example, `[state:accepted]` would also match `not accepted`.
- The help covers [groups as search queries](https://obsidian.md/help/plugins/graph) (DOCUMENTED). I found no official example of a property query in a group.

## Q4: renames

All TESTED unless marked.

| Link form | Rename inside Obsidian |
|---|---|
| Single, single-quoted, alias, heading, flow list, block list | Updated |
| Nested in a list object, nested in a map | Updated |
| Encoded Markdown link | Updated |
| `"[[Ren/Old target]]"` (with path) | Updated, and shortened to `[[New target]]` |
| `"see [[Old]] here"` | **Not updated**, because it is not a link |
| Key with a dot, `Person.name: "[[X]]"` | **Not updated**, so the link breaks |

- **Default behaviour disagrees with the help's wording.**
  - A vault with an empty `app.json` reported `alwaysUpdateLinks: false`.
  - Both `renameFile` and the real rename dialog then showed: "Update links... Always update / Just once / Do not update". The file is renamed before you answer.
  - The [help describes the setting as turning off to get a prompt](https://obsidian.md/help/links), which implies on by default, but [never states the default](https://obsidian.md/help/settings).
  - Whether a vault created through the app's own picker starts differently is UNVERIFIED.
- With `alwaysUpdateLinks: true`, seeded in `.obsidian/app.json` while Obsidian was closed, renames updated silently with no prompt.
- The API docs say `renameFile` [updates links "depending on the user's preferences"](https://docs.obsidian.md/Reference/TypeScript+API/FileManager/renameFile) (DOCUMENTED).
- **A rename rewrites the referring note's whole YAML** when a property link changes (see Q5). A note with only a body link keeps its frontmatter byte for byte.
- **A rename outside Obsidian updates nothing.** This held both while it was open and while it was closed. The links become unresolved.
- **The Connection's file name goes stale.** Renaming Beta updated `between`, but the file stayed `Alpha - Beta.md`.
- **The dotted-key bug is known.** The [forum thread](https://forum.obsidian.md/t/property-names-with-dots-break-link-updating-on-note-rename/102860) reports it through October 2026, tagged `repro`, with no fix listed. Testing reproduces it.
- **An older report no longer applies.** A [1.5.6 bug about property links not renaming](https://forum.obsidian.md/t/links-in-properties-are-not-renamed-automatically-after-renaming-a-linked-note/77249) was slated for a fix. It did not reproduce here.

**File names as link targets** (TESTED on 1.14.5):

- `C# plan` breaks, because `[[C# plan]]` resolves to `C`. The API `renameFile` still allowed `#` in a name.
- `A^B`, `Why [now]`, `Dr. Who`, `100% sure`, `It's fine` and `Comma, here` all resolved.
- Link matching ignores case: `[[PLAIN CASE]]` found `plain case.md`.
- **Same names silently change the target.** Adding `Connections/Alpha.md` made `[[Alpha]]` in notes in `Connections/` point to that file, while notes in `Thoughts/` still resolved to `Thoughts/Alpha.md`.

## Q5: formatting and external edits

All TESTED. The probe note had comments, mixed quotes, flow lists, `007`, `null`, a block string and out-of-order keys.

| Action | Effect on the YAML |
|---|---|
| Open in live preview, then reading view | Nothing, bytes identical |
| Edit the body in the editor | YAML text kept, but **CRLF became LF for the whole file** |
| `processFrontMatter`, even to add one key | Whole YAML rewritten |
| Edit one value in the Properties panel | Whole YAML rewritten, same way |
| Rename a target linked from a property | Whole YAML rewritten, same way |

What the rewrite does:

- Removes all comments.
- Drops unneeded quotes and turns single quotes into double.
- Turns flow lists and objects into block form, with 2-space indent.
- Changes `007` to `7` and `null` to empty.
- Keeps key order, `bool: yes` as text, dates and block strings.

**Canonical form survives.** A note in Obsidian's own output style came back identical after a no-op `processFrontMatter`, and a one-value change altered only that value. The style:

- Block lists, with links in double quotes.
- LF line endings and no comments.
- `empty: []` and an over-long plain string were both unchanged.

Official and staff sources:

- Help says [JSON frontmatter is saved as YAML](https://obsidian.md/help/properties) (DOCUMENTED).
- The [`processFrontMatter` docs](https://docs.obsidian.md/Reference/TypeScript+API/FileManager/processFrontMatter) call it atomic and say nothing about formatting.
- A [long forum thread](https://forum.obsidian.md/t/yaml-properties-api-processfrontmatter-removes-alters-string-quotes-comments-types-formatting/65851) records the staff position that this is an accepted trade-off.
- A [2025 reply](https://forum.obsidian.md/t/yaml-bug-editing-in-live-preview-or-reading-mode-reformats-and-deletes-comments/104006) says there are no plans to change it.

External edits while Obsidian is open:

| Case | Result |
|---|---|
| Note open and clean, atomic replace (temp file then rename) | Kept. Editor, cache and Properties panel all updated. Obsidian wrote nothing back. |
| Unsaved body text, external edit to another line | Both kept (merged) |
| Unsaved edit and external edit to the **same line** | **External edit silently lost**: Obsidian's value was saved, with no notice |
| External invalid YAML | File left alone, panel shows "Invalid properties", `frontmatter` is null, `processFrontMatter` throws `YAMLParseError` |
| External edit to `graph.json` | An open graph view ignored it, a newly opened graph view used it |

Whether an open graph view later overwrites an external `graph.json` edit is UNVERIFIED.

## Recommendation

Store flat properties only, in Obsidian's own output style.

**Thought** (`from` as links, plus a `connected` list for accepted Connections):

```yaml
---
status: Parked
project: "[[Atlas]]"
from:
  - "[[Add AI to Atlas]]"
  - "[[Bring your own AI]]"
connected:
  - "[[Bring your own AI]]"
---
```

**Connection** (reason in the body):

```yaml
---
between:
  - "[[Credit system]]"
  - "[[Bring your own AI]]"
state: suggested
---
```

**Graph settings:**

| Setting | Value |
|---|---|
| Filter | `-[state:accepted] -[state:rejected]` |
| Groups | `[state:suggested]` first, then `["status":Doing]` and similar |

In the graph, suggested Connections show as coloured midpoints, and accepted ones become direct lines.

**What the app must do itself:**

1. **Accepting:** add the other Thought to `connected` on both Thoughts. A link on one side is enough for the line, but both sides make it visible in each note's properties. Rejecting or deleting removes those links.
2. **Reading:** parse YAML with a real parser, and take the two Thoughts from `between`, never from the Connection's file name.
3. **Writing:** use the canonical form above with LF. Keep nothing in comments, and do not depend on quoting or number formats surviving.
4. **Its own renames:** rewrite every link in the vault itself, because Obsidian will not notice.
5. **Obsidian's renames:** watch for them and report broken links, because the user can pick "Do not update". Set `alwaysUpdateLinks: true` in `.obsidian/app.json` only with the user's agreement. It does not cover the cases below.
6. **Names:** ban `#` in file names, and keep file names unique across the whole vault, including Connections. Use no dots in property keys, and do not put links inside longer text values.
7. **External edits:** write atomically, then re-read after a few seconds and reconcile, because an unsaved same-line edit in Obsidian wins silently.
8. **`graph.json`:** merge your keys rather than replacing the file, and preferably do it while Obsidian is closed. Tell users the local graph keeps its own filter.

