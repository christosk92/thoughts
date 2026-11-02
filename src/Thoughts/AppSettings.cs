using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thoughts;

/// <summary>Settings that can't live in the vault, because they say where the vault is.</summary>
sealed class AppSettings
{
    public string? VaultPath { get; set; }

    /// <summary>US dollars a month the AI may spend. Reaching it pauses AI work, never capture.</summary>
    public double BudgetUsd { get; set; } = 20;

    /// <summary>Fine suggests only Connections. Coarse also suggests plain related links with no sentence.</summary>
    public bool Coarse { get; set; }

    /// <summary>The language for titles and explanations. Null follows each Thought. Cards are always English.</summary>
    public string? Language { get; set; }

    public string OrganizeModel { get; set; } = "claude-sonnet-5-5";
    public string ConnectModel { get; set; } = "claude-opus-5-5";
    public string CheckModel { get; set; } = "claude-opus-5-5";
    public string FanOutModel { get; set; } = "claude-sonnet-5-5";

    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thoughts", "settings.json");

    /// <summary>What first run suggests: the repo's vault in a build from this repo, else a folder in Documents.</summary>
    public static string SuggestedVaultPath =>
        typeof(AppSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DefaultVaultPath")?.Value
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Thoughts");

    public static AppSettings Load(string file)
    {
        if (!File.Exists(file)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(file), AppSettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, AppSettingsJson.Default.AppSettings));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
partial class AppSettingsJson : JsonSerializerContext;
