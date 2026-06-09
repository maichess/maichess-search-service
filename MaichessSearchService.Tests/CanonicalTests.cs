using MaichessSearchService.Search;
using Xunit;

namespace MaichessSearchService.Tests;

public class CanonicalTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyForMissingId(string? id)
    {
        Assert.Equal(string.Empty, Canonical.UserId(id));
    }

    [Fact]
    public void LowercasesGuidToDForm()
    {
        Assert.Equal(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            Canonical.UserId("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"));
    }

    [Fact]
    public void PassesThroughNonGuidIds()
    {
        Assert.Equal("bot-stockfish", Canonical.UserId("bot-stockfish"));
    }
}
