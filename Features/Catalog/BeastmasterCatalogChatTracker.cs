using System.Text.RegularExpressions;

namespace Beastmaster;

public sealed partial class BeastmasterCatalogChatTracker : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> CaptureNameAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["羊羔"] = "迷途羊羔",
        };

    private readonly BeastmasterProgressService progressService;

    public BeastmasterCatalogChatTracker(BeastmasterProgressService progressService)
    {
        this.progressService = progressService;
        DalamudApi.ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        DalamudApi.ChatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(object message)
    {
        var text = ExtractChatMessageText(message);
        var match = CaptureMessageRegex().Match(text);
        if (!match.Success)
        {
            return;
        }

        var capturedName = match.Groups["name"].Value.Trim();
        var catalogName = CaptureNameAliases.GetValueOrDefault(capturedName, capturedName);
        var entry = BeastmasterCatalog.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, catalogName, StringComparison.Ordinal));
        if (entry == null || progressService.IsCompleted(entry.Key))
        {
            return;
        }

        progressService.SetCompleted(entry.Key, true);
        DalamudApi.Log.Information(
            "Auto-marked Beastmaster catalog entry {Number} ({Name}) from capture message.",
            entry.Number,
            entry.Name);
    }

    private static string ExtractChatMessageText(object message)
    {
        try
        {
            var messageProperty = message.GetType().GetProperty("Message");
            var value = messageProperty?.GetValue(message);
            var textValueProperty = value?.GetType().GetProperty("TextValue");
            return textValueProperty?.GetValue(value) as string
                   ?? value?.ToString()
                   ?? message.ToString()
                   ?? string.Empty;
        }
        catch
        {
            return message.ToString() ?? string.Empty;
        }
    }

    [GeneratedRegex("成功结识了(?<name>.+?)种的魔兽[！!]", RegexOptions.CultureInvariant)]
    private static partial Regex CaptureMessageRegex();
}
