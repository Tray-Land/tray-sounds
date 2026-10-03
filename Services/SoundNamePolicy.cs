using System.Globalization;
using System.Text;

namespace TraySounds.Services;

/// <summary>
/// Naming and file rules for the user's sounds. No WinRT here, so the test project links it.
/// </summary>
internal static class SoundNamePolicy
{
    public const int MaxNameLength = 32;

    /// <summary>Largest file the app will copy in; a soundboard clip is seconds, not an album.</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>Formats Windows' media stack plays without extra codecs.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
        [".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".ogg", ".opus"];

    public static bool IsSupported(string? path) =>
        !string.IsNullOrEmpty(path) && SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>"my_cool-sound.mp3" → "My cool sound".</summary>
    public static string FromFileName(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        StringBuilder builder = new(stem.Length);
        foreach (char c in stem)
        {
            builder.Append(c is '_' or '-' or '.' ? ' ' : c);
        }

        string name = Clean(builder.ToString());
        if (name.Length == 0)
        {
            return "Sound";
        }

        // Only capitalize the first letter; keep the user's casing elsewhere (e.g. "WOW", "iPhone ding").
        return char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];
    }

    /// <summary>Trims, collapses whitespace, drops control characters, and caps the length.</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        StringBuilder builder = new(name.Length);
        bool pendingSpace = false;
        foreach (char c in name.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (char.IsControl(c))
            {
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(c);
        }

        string cleaned = builder.ToString();
        return cleaned.Length <= MaxNameLength ? cleaned : cleaned[..MaxNameLength].TrimEnd();
    }

    /// <summary>Suggestions for the emoji picker; anything else can still be typed or pasted.</summary>
    public static readonly IReadOnlyList<string> EmojiChoices =
    [
        "🎵", "🎶", "🎙️", "🔔", "📯", "🥁", "🎺", "🎸", "🎹", "🎷", "🎻", "🎤",
        "🎉", "👏", "🙌", "👍", "👎", "💯", "🔥", "⚡", "💥", "✨", "💨", "🌊",
        "😂", "🤣", "😭", "😱", "😎", "🤔", "🙄", "😴", "🤯", "🥳", "😈", "👻",
        "🐶", "🐱", "🐔", "🐸", "🦗", "🦆", "🐑", "🐮", "🦁", "🐺", "🦅", "🐝",
        "🚗", "🚀", "🚨", "⏰", "📞", "🔫", "💣", "🔨", "🚪", "🛎️", "🪀", "🎯",
        "✅", "❌", "⚠️", "❓", "❗", "💀", "👀", "❤️", "🍕", "🍺", "☕", "🏆",
    ];

    /// <summary>
    /// The first emoji-like character (grapheme cluster) in <paramref name="input"/>, or
    /// <paramref name="fallback"/> when there is none. Letters, digits, punctuation, and spaces don't count.
    /// </summary>
    public static string FirstEmoji(string? input, string fallback)
    {
        if (string.IsNullOrEmpty(input))
        {
            return fallback;
        }

        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(input);
        while (elements.MoveNext())
        {
            string element = (string)elements.Current;
            if (element.Length == 1 && (char.IsLetterOrDigit(element[0]) || char.IsWhiteSpace(element[0]) || char.IsControl(element[0]) || char.IsPunctuation(element[0])))
            {
                continue;
            }

            return element;
        }

        return fallback;
    }

    /// <summary>"Ding" → "Ding 2" when "Ding" is taken (case-insensitive).</summary>
    public static string MakeUnique(string name, IEnumerable<string> existing)
    {
        HashSet<string> taken = new(existing, StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(name))
        {
            return name;
        }

        for (int n = 2; ; n++)
        {
            string suffix = $" {n}";
            string stem = name.Length + suffix.Length > MaxNameLength ? name[..(MaxNameLength - suffix.Length)].TrimEnd() : name;
            string candidate = stem + suffix;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Stored file name: a fresh id plus the original extension, so names can change freely.</summary>
    public static string StorageFileName(string id, string sourcePath) =>
        id + Path.GetExtension(sourcePath).ToLowerInvariant();

    /// <summary>"0:07" for the recording timer.</summary>
    public static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
}
