using Xunit;
using TraySounds.Services;

namespace TraySounds.Tests;

public class SoundTrimPolicyTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Clamp_KeepsRangeInsideTheClip()
    {
        SoundTrim trim = SoundTrimPolicy.Clamp(Ms(-50), Ms(9000), Ms(2000));
        Assert.Equal(TimeSpan.Zero, trim.Start);
        Assert.Equal(Ms(2000), trim.End);
    }

    [Fact]
    public void Clamp_EnforcesMinimumLength()
    {
        SoundTrim trim = SoundTrimPolicy.Clamp(Ms(1000), Ms(1010), Ms(2000));
        Assert.Equal(SoundTrimPolicy.MinLength, trim.Length);
    }

    [Fact]
    public void Clamp_PushesStartBackWhenEndIsAtTheClipEnd()
    {
        SoundTrim trim = SoundTrimPolicy.Clamp(Ms(1990), Ms(2000), Ms(2000));
        Assert.Equal(Ms(2000), trim.End);
        Assert.Equal(SoundTrimPolicy.MinLength, trim.Length);
    }

    [Fact]
    public void Clamp_ClipShorterThanMinimumIsKeptWhole()
    {
        SoundTrim trim = SoundTrimPolicy.Clamp(Ms(0), Ms(50), Ms(50));
        Assert.Equal(Ms(50), trim.Length);
    }

    [Fact]
    public void ToStored_FullRangeIsNull()
    {
        Assert.Null(SoundTrimPolicy.ToStored(new(Ms(10), Ms(1990)), Ms(2000)));
        Assert.NotNull(SoundTrimPolicy.ToStored(new(Ms(500), Ms(2000)), Ms(2000)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 1000)]
    [InlineData(900, 950)]
    public void FromStored_RejectsUnusableRanges(long start, long end) =>
        Assert.Null(SoundTrimPolicy.FromStored(start, end));

    [Fact]
    public void FromStored_AcceptsAGoodRange() =>
        Assert.Equal(new SoundTrim(Ms(250), Ms(1500)), SoundTrimPolicy.FromStored(250, 1500));

    [Fact]
    public void FormatTime_ShowsHundredths() =>
        Assert.Equal("1:05.25", SoundTrimPolicy.FormatTime(Ms(65250)));

    [Fact]
    public void Peaks_NormalizesToTheLoudestBucket()
    {
        float[] peaks = SoundTrimPolicy.Peaks([0.1f, -0.2f, 0.05f, 0.4f], 2);
        Assert.Equal(2, peaks.Length);
        Assert.Equal(0.5f, peaks[0], 3);
        Assert.Equal(1f, peaks[1], 3);
    }

    [Fact]
    public void Peaks_SilenceStaysZero() =>
        Assert.All(SoundTrimPolicy.Peaks(new float[100], 10), p => Assert.Equal(0f, p));

    [Fact]
    public void Peaks_FewerSamplesThanBucketsIsFine() =>
        Assert.Equal(8, SoundTrimPolicy.Peaks([0.5f, 1f], 8).Length);
}
