namespace TraySounds.Services;

/// <summary>The slice of a clip that plays: from <see cref="Start"/> up to <see cref="End"/>.</summary>
public readonly record struct SoundTrim(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Length => End - Start;
}

/// <summary>
/// Trim-range and waveform rules for the sound editor. No WinRT here, so the test project links it.
/// </summary>
internal static class SoundTrimPolicy
{
    /// <summary>Shortest slice the editor allows, so a sound can never be trimmed to silence.</summary>
    public static readonly TimeSpan MinLength = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Keeps a saved or dragged range inside the clip and at least <see cref="MinLength"/> long
    /// (or the whole clip when it is shorter than that).
    /// </summary>
    public static SoundTrim Clamp(TimeSpan start, TimeSpan end, TimeSpan total)
    {
        if (total <= TimeSpan.Zero)
        {
            return new(TimeSpan.Zero, TimeSpan.Zero);
        }

        TimeSpan min = total < MinLength ? total : MinLength;
        start = Min(Max(start, TimeSpan.Zero), total - min);
        end = Min(Max(end, start + min), total);
        return new(start, end);
    }

    /// <summary>The trim to store: null when it covers the whole clip (within a frame or two).</summary>
    public static SoundTrim? ToStored(SoundTrim trim, TimeSpan total)
    {
        TimeSpan slack = TimeSpan.FromMilliseconds(30);
        return trim.Start <= slack && total - trim.End <= slack ? null : trim;
    }

    /// <summary>Whether a stored trim describes a usable slice (a hand-edited library.json might not).</summary>
    public static SoundTrim? FromStored(long startMs, long endMs)
    {
        if (endMs <= 0 || startMs < 0 || endMs - startMs < MinLength.TotalMilliseconds)
        {
            return null;
        }

        return new(TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs));
    }

    /// <summary>"0:01.25" for the trim handles.</summary>
    public static string FormatTime(TimeSpan time) =>
        $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 10:00}";

    /// <summary>
    /// Reduces <paramref name="samples"/> (mono, -1 to 1) to <paramref name="buckets"/> peak levels in 0 to 1,
    /// scaled so the loudest bucket reaches 1.
    /// </summary>
    public static float[] Peaks(ReadOnlySpan<float> samples, int buckets)
    {
        float[] peaks = new float[Math.Max(buckets, 0)];
        if (peaks.Length == 0 || samples.IsEmpty)
        {
            return peaks;
        }

        for (int i = 0; i < samples.Length; i++)
        {
            int bucket = (int)((long)i * peaks.Length / samples.Length);
            peaks[bucket] = Math.Max(peaks[bucket], Math.Abs(samples[i]));
        }

        float loudest = peaks.Max();
        if (loudest > 0)
        {
            for (int i = 0; i < peaks.Length; i++)
            {
                peaks[i] = Math.Min(peaks[i] / loudest, 1);
            }
        }

        return peaks;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
