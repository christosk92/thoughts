# Thoughts

A place to drop any thought, circle back on it, and work on it. The AI mostly organizes; the person decides what gets worked on.

## Language

**Thought**:
A single thing someone wrote down, in their own words, and the unit whose progress is tracked. It can be as large as a product feature or as small as a conversation to have.
_Avoid_: Idea, issue, ticket, task, card

**Build on**:
To add detail to a Thought's body after working on it.
_Avoid_: Edit, expand, comment

**Fan out**:
To turn parts of a Thought into new Thoughts, each with its own Status and a link back.
_Avoid_: Split, sub-tasks, children

**Connection**:
Two Thoughts and a sentence stating how they relate.
_Avoid_: Link, related, relationship

**Suggested Connection**:
A Connection the AI proposed and the person has not yet accepted or dismissed.
_Avoid_: Recommendation, hint

**Granularity**:
How much the AI suggests. Fine suggests only Connections; Coarse also suggests plain related links with no sentence.
_Avoid_: Sensitivity, level

**Budget**:
The monthly amount the AI may spend. Reaching it pauses AI work, never capture.
_Avoid_: Limit, quota

**Map**:
A view of how one Thought grew, with that Thought at the centre and what came from it branching out.
_Avoid_: Graph, tree, mind map

**Status**:
Where a Thought stands: Inbox, Parked, Next, Doing, Done or Dropped. Only the person changes it.
_Avoid_: State, stage, column

**Parked**:
The Status of a Thought that is kept but not for now.
_Avoid_: Backlog, later

**Dropped**:
The Status of a Thought the person decided against. It stays searchable and can still surface as related.
_Avoid_: Deleted, archived, cancelled

**Project**:
What a Thought belongs to. Projects form one flat list, for example Atlas, Bulletin or Wavee.
_Avoid_: Area, category, feature, epic

**Unsorted**:
Where a Thought goes when no Project fits it.
_Avoid_: Misc, other, none

**Person**:
Someone a Thought is about or meant for. A Person's note gathers every Thought linked to them, whatever its Project.
_Avoid_: Contact, assignee

**Board**:
The one view of all Thoughts, with one row per Project and one column per Status.
_Avoid_: Kanban, dashboard

**Review**:
Going through Inbox and Parked Thoughts one at a time to choose a Status for each. The person starts it themselves.
_Avoid_: Triage, reminder, standup

**Vault**:
The Obsidian folder that holds every Thought and Project.
_Avoid_: Database, store

**Caller**:
A Claude Code session or other MCP client that adds Thoughts and does the AI's reasoning on its own model.
_Avoid_: Client, agent, backend

**Keep-local Thought**:
A Thought that is never sent to the AI. One added by a Caller has been seen once by that Caller's model, and is never sent again.
_Avoid_: Private note, offline thought
