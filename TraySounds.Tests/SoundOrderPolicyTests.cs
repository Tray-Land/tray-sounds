using TraySounds.Services;
using Xunit;

namespace TraySounds.Tests;

public class SoundOrderPolicyTests
{
    [Fact]
    public void Apply_FollowsSavedOrder() =>
        Assert.Equal(["c", "a", "b"], SoundOrderPolicy.Apply(["a", "b", "c"], ["c", "a", "b"], s => s));

    [Fact]
    public void Apply_AppendsUnknownItemsInOriginalOrder() =>
        Assert.Equal(["b", "a", "x", "y"], SoundOrderPolicy.Apply(["x", "a", "y", "b"], ["b", "a"], s => s));

    [Fact]
    public void Apply_IgnoresSavedIdsThatNoLongerExist() =>
        Assert.Equal(["b", "a"], SoundOrderPolicy.Apply(["a", "b"], ["gone", "b", "a"], s => s));

    [Fact]
    public void Apply_WithNoSavedOrderKeepsItems() =>
        Assert.Equal(["a", "b"], SoundOrderPolicy.Apply(["a", "b"], [], s => s));

    [Fact]
    public void ParseAndSerialize_RoundTrip() =>
        Assert.Equal(["stock:ding", "abc"], SoundOrderPolicy.Parse(SoundOrderPolicy.Serialize(["stock:ding", "abc"])));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, 3)]
    [InlineData(99, 5)]
    public void ClampColumns_StaysInRange(int input, int expected) =>
        Assert.Equal(expected, SoundOrderPolicy.ClampColumns(input));
}
