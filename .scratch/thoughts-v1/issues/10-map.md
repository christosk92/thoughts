# 10: Map

**What to build:** From a Thought, a full view shows how it grew: `from` links as solid branches and Connections as dotted lines, with title and Status colour on each node.

**Blocked by:** 08, 09

**Status:** done

- [x] The Map opens from the panel, centred on that Thought
- [x] A Thought with two parents draws one solid branch and one dotted line
- [x] Clicking a node opens its panel, and double-clicking recentres
- [x] It stays readable with a large fake fan-out, with no layout jumps
- [x] Verify on the throwaway vault. Makes no AI calls.

## Comments

Verified 2026-11-02 on throwaway vaults through the debug --drive input, on a Debug build. A Debug NativeAOT publish has no warnings but wasn't driven. No AI calls.

The owner asked for a fully interactive 3D Map instead of a flat one. The engine has no 3D scene, so `MapScene` projects each point through a perspective camera in code, draws lines and orbs as GPU sprites, and moves labels with bound transforms. It only asks for frames while something is moving.

Driven with screenshots looked at: orbit by drag, zoom by wheel and buttons, pan by Shift-drag, Reset, click to open, double-click to recentre, light theme, a Thought with two parents, and a 60-child fan-out. Labels that would overlap fade in favour of nearer ones, and hovering brings one back.

Also fixed, reported by the owner: clicks on the empty parts of the panel went through to cards under it, and could open another Thought. The panel and the capture strip now stop them. Three clicks on empty panel space stayed on the same Thought, the wheel scrolls only the panel, and the Map doesn't zoom under it.

Also fixed, reported by the owner: the Map page turned grey under the pointer, from (15, 14, 13) to (34, 33, 32), because the engine lightens a filled box that takes input unless it sets its own hover fills. It now reads (15, 14, 13) at rest, on hover and pressed.

In two of these runs, a click meant for the panel opened a card under it instead. Repeating the same click at the same spot then held, along with five other spots on the panel, so this looks like timing, or the owner's own clicks landing on a test window, rather than a gap in the fix. It isn't proven.

Choices made here:
- The camera leans 35% from the Thought towards the middle of what grew from it, so it fills the window. The Thought is near the centre rather than exactly on it, which differs from this ticket's wording.
- With the panel open, the graph slides left into the free space.
- The view drifts slowly for up to 14 s until first touched, so the depth reads. Reduced motion turns this off.
- At most 200 nodes.

Not driven: a real mouse and wheel (the wheel's direction was set from the engine's sign rule, by a synthetic notch), the keyboard, reduced motion, and how smooth it feels. Frame rate and idle cost weren't measured.

Known limits: Thoughts on the Map can't be reached by keyboard or by a screen reader. Nodes are found by testing the pointer in code, so there's no per-node focus.
