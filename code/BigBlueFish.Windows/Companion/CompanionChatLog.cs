using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BigBlueFish.Windows;

internal sealed record CompanionChatLine(bool User, string Text);

// Chat lines shown when scrolling up in the input box, saved next to preferences so they survive restarts.
internal sealed class CompanionChatLog
{
    private const int Limit = 300;
    private readonly string path;
    private readonly List<CompanionChatLine> lines = new();
    private readonly bool enabled;
    internal int Version { get; private set; }

    internal CompanionChatLog(string path = "chat-log.json", bool enabled = true)
    {
        this.path = Path.GetFullPath(path);
        this.enabled = enabled;
        if (!enabled) return;
        try { lines.AddRange((JsonSerializer.Deserialize<CompanionChatLine?[]>(File.ReadAllText(this.path)) ?? []).Where(l => l != null && !string.IsNullOrWhiteSpace(l.Text)).Select(l => l!).TakeLast(Limit)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    internal void Add(bool user, string text)
    {
        if (!enabled || string.IsNullOrWhiteSpace(text)) return;
        lines.Add(new(user, text.Trim()));
        if (lines.Count > Limit) lines.RemoveAt(0);
        Version++;
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(lines));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // History is a convenience; never interrupt the pet.
    }

    // Default view: each owner message plus the pet line right before and right after it.
    internal IEnumerable<CompanionChatLine> Lines(bool all) => all ? lines
        : lines.Where((line, i) => line.User || i > 0 && lines[i - 1].User || i + 1 < lines.Count && lines[i + 1].User);
}
