using TraySounds.Services;
using Xunit;

namespace TraySounds.Tests;

public class SoundCreditPolicyTests
{
    private const string Csv =
        "freesound_community-rimshot\t\"Sound Effect by <a href=\"\"https://pixabay.com/users/fc-1/?x=1\"\">freesound_community</a> from <a href=\"\"https://pixabay.com/sound-effects//?x=1\"\">Pixabay</a>\"\r\n" +
        "Horn Stabs Entrance\tHorn Stabs Entrance by bevibeldesign -- https://freesound.org/s/350433/ -- License: Creative Commons 0\r\n";

    [Fact]
    public void Parse_ReadsPixabayHtml()
    {
        SoundCredit credit = SoundCreditPolicy.Parse(Csv)["freesound_community-rimshot"];
        Assert.Equal("freesound_community", credit.Author);
        Assert.Equal("Pixabay", credit.SourceName);
        Assert.Equal("https://pixabay.com/users/fc-1/?x=1", credit.AuthorUrl.ToString());
        Assert.Null(credit.License);
    }

    [Fact]
    public void Parse_ReadsFreesoundText()
    {
        SoundCredit credit = SoundCreditPolicy.Parse(Csv)["Horn Stabs Entrance"];
        Assert.Equal("bevibeldesign", credit.Author);
        Assert.Equal("https://freesound.org/people/bevibeldesign/", credit.AuthorUrl.ToString());
        Assert.Equal("https://freesound.org/s/350433/", credit.SourceUrl.ToString());
        Assert.Equal("Creative Commons 0", credit.License);
        Assert.Equal("by bevibeldesign · Freesound · Creative Commons 0", credit.Summary);
    }

    [Fact]
    public void Find_MatchesFreesoundFilesById()
    {
        var credits = SoundCreditPolicy.Parse(Csv);
        Assert.Equal("bevibeldesign", SoundCreditPolicy.Find(credits, "350433__bevibeldesign__horn-stabs-entrance")?.Author);
        Assert.Null(SoundCreditPolicy.Find(credits, "ding"));
    }
}
