using System.Text;
using System.Text.Json;
using Thoughts;

// thoughts-mcp --vault <folder> [--settings <file>]: an MCP server over stdio, one JSON message per line. Protocol traffic only on
// stdout, so anything else goes to stderr. It runs with the app closed, and the app picks up its changes from the file watcher.

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i < args.Length - 1 ? args[i + 1] : null;
}

var settings = AppSettings.Load(Arg("--settings") ?? AppSettings.DefaultFile);
string? vaultPath = Arg("--vault") ?? settings.VaultPath;
if (vaultPath is null)
{
    Console.Error.WriteLine("Say which vault: thoughts-mcp --vault <folder>");
    return 2;
}

Console.InputEncoding = new UTF8Encoding(false);
using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
var server = new McpServer(new Vault(vaultPath), settings);

while (Console.In.ReadLine() is { } line)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    string? reply = server.Handle(line);
    if (reply is not null) output.WriteLine(reply);
}
return 0;
