namespace TraySounds.Services;

/// <summary>
/// Board ordering and grid sizing rules. No WinRT here, so the test project links it.
/// </summary>
internal static class SoundOrderPolicy
{
    public const int MinColumns = 2;
    public const int MaxColumns = 5;
    public const int DefaultColumns = 3;

    public static int ClampColumns(int columns) => Math.Clamp(columns, MinColumns, MaxColumns);

    /// <summary>
    /// <paramref name="items"/> in the <paramref name="saved"/> id order. Items the saved order
    /// doesn't know (newly added sounds) keep their relative order at the end; saved ids with no
    /// matching item are ignored.
    /// </summary>
    public static List<T> Apply<T>(IEnumerable<T> items, IReadOnlyList<string> saved, Func<T, string> idOf)
    {
        Dictionary<string, int> rank = [];
        for (int i = 0; i < saved.Count; i++)
        {
            rank.TryAdd(saved[i], i);
        }

        return items
            .Select((item, index) => (item, index, rank: rank.TryGetValue(idOf(item), out int r) ? r : int.MaxValue))
            .OrderBy(x => x.rank)
            .ThenBy(x => x.index)
            .Select(x => x.item)
            .ToList();
    }

    /// <summary>Parses the stored form (one id per line).</summary>
    public static List<string> Parse(string? stored) =>
        string.IsNullOrEmpty(stored)
            ? []
            : [.. stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static string Serialize(IEnumerable<string> ids) => string.Join('\n', ids);
}
