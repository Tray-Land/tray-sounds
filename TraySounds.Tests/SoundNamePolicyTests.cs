using TraySounds.Services;
using Xunit;

namespace TraySounds.Tests;

public class SoundNamePolicyTests
{
    [Theory]
    [InlineData(@"C:\clips\my_cool-sound.mp3", "My cool sound")]
    [InlineData("WOW.wav", "WOW")]
    [InlineData("iphone ding.m4a", "Iphone ding")]
    [InlineData("___.wav", "Sound")]
    [InlineData("laugh.track.final.ogg", "Laugh track final")]
    public void FromFileName_MakesAReadableName(string path, string expected) =>
        Assert.Equal(expected, SoundNamePolicy.FromFileName(path));

    [Theory]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("tab\there", "tab here")]
    [InlineData("bell\u0007", "bell")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Clean_NormalizesWhitespaceAndControlCharacters(string? input, string expected) =>
        Assert.Equal(expected, SoundNamePolicy.Clean(input));

    [Fact]
    public void Clean_CapsTheLength()
    {
        string cleaned = SoundNamePolicy.Clean(new string('a', 100));
        Assert.Equal(SoundNamePolicy.MaxNameLength, cleaned.Length);
    }

    [Fact]
    public void MakeUnique_KeepsAFreeName() =>
        Assert.Equal("Ding", SoundNamePolicy.MakeUnique("Ding", ["Boing"]));

    [Fact]
    public void MakeUnique_NumbersATakenNameIgnoringCase() =>
        Assert.Equal("Ding 3", SoundNamePolicy.MakeUnique("Ding", ["ding", "Ding 2"]));

    [Fact]
    public void MakeUnique_StaysWithinTheLengthLimit()
    {
        string name = new('x', SoundNamePolicy.MaxNameLength);
        string unique = SoundNamePolicy.MakeUnique(name, [name]);
        Assert.True(unique.Length <= SoundNamePolicy.MaxNameLength);
        Assert.EndsWith(" 2", unique);
    }

    [Theory]
    [InlineData("a.WAV", true)]
    [InlineData("a.mp3", true)]
    [InlineData("a.opus", true)]
    [InlineData("a.mp4", false)]
    [InlineData("noextension", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_ChecksTheExtension(string? path, bool expected) =>
        Assert.Equal(expected, SoundNamePolicy.IsSupported(path));

    [Fact]
    public void StorageFileName_UsesTheIdAndLowercaseExtension() =>
        Assert.Equal("abc.mp3", SoundNamePolicy.StorageFileName("abc", @"C:\x\Song.MP3"));

    [Theory]
    [InlineData("🎉", "🎉")]
    [InlineData("  🎉🎺", "🎉")]
    [InlineData("hi 🥁", "🥁")]
    [InlineData("👍🏽", "👍🏽")]
    [InlineData("❤️", "❤️")]
    [InlineData("abc 123 !", "🎵")]
    [InlineData("", "🎵")]
    [InlineData(null, "🎵")]
    public void FirstEmoji_PicksTheFirstEmojiOrFallsBack(string? input, string expected) =>
        Assert.Equal(expected, SoundNamePolicy.FirstEmoji(input, "🎵"));

    [Fact]
    public void EmojiChoices_AreAllValidEmoji() =>
        Assert.All(SoundNamePolicy.EmojiChoices, e => Assert.Equal(e, SoundNamePolicy.FirstEmoji(e, "?")));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7, "0:07")]
    [InlineData(60, "1:00")]
    [InlineData(125, "2:05")]
    public void FormatDuration_ShowsMinutesAndSeconds(int seconds, string expected) =>
        Assert.Equal(expected, SoundNamePolicy.FormatDuration(TimeSpan.FromSeconds(seconds)));
}
