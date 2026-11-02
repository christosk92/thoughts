# Thoughts
_What if your brain was a kanban board?_

Drop in any thought, circle back on it, and work on it. A Windows desktop app on [FluentGpu](https://github.com/christosk92/fluent-gpu), over an Obsidian vault: every Thought is a plain Markdown note that stays readable without the app.

**Status:** early. Capture, the Board with drag and drop, Obsidian sync and the panel with Markdown editing work. The AI features aren't built yet. See [the design](docs/design.md) and [the tickets](.scratch/thoughts-v1/issues).

## What it will do

- **Capture in seconds.** Saving never depends on the network.
- **A Board** with one row per Project and one column per Status. At most 2 Thoughts in Doing.
- **AI that organizes.** It suggests a Project and says how a new Thought relates to past ones, with a concrete reason. It never writes your words or changes a Status.
- **Keep-local Thoughts** that are never sent to any AI.
- **A Map** of how an idea grew, and a **Review** that brings back what you parked.
- **Costs always visible,** with a monthly Budget.

## Build

Needs Windows and the .NET 10 SDK. Clone [fluent-gpu](https://github.com/christosk92/fluent-gpu) beside this repo.

```powershell
dotnet run --project src/Thoughts
```

On first run, choose where your vault lives. To try it without touching a real vault:

```powershell
dotnet run --project src/Thoughts -- --vault C:\temp\thoughts-try --settings C:\temp\thoughts-try.json
```

## License

[MIT](LICENSE)
