using System.Text.RegularExpressions;

namespace TraySounds.Services;

/// <summary>Who made a downloaded sound, and where to find them and it.</summary>
public sealed record SoundCredit(string Author, Uri AuthorUrl, string SourceName, Uri SourceUrl, string? License)
{
    /// <summary>"by Author · Freesound · Creative Commons 0", for tooltips.</summary>
    public string Summary => string.Join(" · ", new[] { $"by {Author}", SourceName, License }.Where(p => !string.IsNullOrEmpty(p)));
}

/// <summary>
/// Reads "sound attribution.csv": tab-separated rows of a key and a credit line. The credit is either
/// Pixabay's HTML snippet or Freesound's "title by author -- url -- License: x" text. Pure, so it's testable.
/// </summary>
public static partial class SoundCreditPolicy
{
    public static IReadOnlyDictionary<string, SoundCredit> Parse(string csv)
    {
        Dictionary<string, SoundCredit> credits = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            string key = line[..tab].Trim();
            string text = Unquote(line[(tab + 1)..].Trim());
            if (ParseCredit(text) is { } credit)
            {
                credits[key] = credit;
            }
        }

        return credits;
    }

    /// <summary>Finds the credit for an audio file's name without extension, by exact key or Freesound id prefix.</summary>
    public static SoundCredit? Find(IReadOnlyDictionary<string, SoundCredit> credits, string fileStem)
    {
        if (credits.TryGetValue(fileStem, out SoundCredit? exact))
        {
            return exact;
        }

        // Freesound files are named "<id>__<user>__<title>", but the CSV key may be only the title.
        int separator = fileStem.IndexOf("__", StringComparison.Ordinal);
        if (separator <= 0 || !fileStem[..separator].All(char.IsAsciiDigit))
        {
            return null;
        }

        string marker = "/s/" + fileStem[..separator] + "/";
        return credits.Values.FirstOrDefault(c => c.SourceUrl.AbsolutePath.StartsWith(marker, StringComparison.Ordinal));
    }

    private static SoundCredit? ParseCredit(string text)
    {
        MatchCollection anchors = AnchorRegex().Matches(text);
        if (anchors.Count >= 2
            && Uri.TryCreate(anchors[0].Groups[1].Value, UriKind.Absolute, out Uri? authorUrl)
            && Uri.TryCreate(anchors[1].Groups[1].Value, UriKind.Absolute, out Uri? sourceUrl))
        {
            return new SoundCredit(anchors[0].Groups[2].Value.Trim(), authorUrl, anchors[1].Groups[2].Value.Trim(), sourceUrl, null);
        }

        Match free = FreesoundRegex().Match(text);
        if (free.Success
            && Uri.TryCreate(free.Groups[2].Value, UriKind.Absolute, out Uri? soundUrl))
        {
            string author = free.Groups[1].Value.Trim();
            Uri profile = new($"https://freesound.org/people/{Uri.EscapeDataString(author)}/");
            return new SoundCredit(author, profile, "Freesound", soundUrl, free.Groups[3].Value.Trim());
        }

        return null;
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1].Replace("\"\"", "\"") : text;

    [GeneratedRegex("""<a href="([^"]+)">([^<]+)</a>""")]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"^.+ by (\S+) -- (https?://\S+) -- License: (.+)$")]
    private static partial Regex FreesoundRegex();
}
