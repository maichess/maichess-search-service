using MaichessSearchService.Search;
using Xunit;

namespace MaichessSearchService.Tests;

public class PlacementKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsNullForMissingFen(string? fen)
    {
        Assert.Null(PlacementKey.FromFen(fen));
    }

    [Fact]
    public void DropsMoveCountersAndKeepsSideToMove()
    {
        string? key = PlacementKey.FromFen(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");

        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w", key);
    }

    [Fact]
    public void KeepsBlackToMove()
    {
        string? key = PlacementKey.FromFen("8/8/8/8/8/8/8/8 b - - 5 12");
        Assert.Equal("8/8/8/8/8/8/8/8 b", key);
    }

    [Fact]
    public void DefaultsToWhiteWhenSideToMoveMissing()
    {
        Assert.Equal("8/8/8/8/8/8/8/8 w", PlacementKey.FromFen("8/8/8/8/8/8/8/8"));
    }

    [Fact]
    public void DefaultsToWhiteWhenSideToMoveInvalid()
    {
        Assert.Equal("rnbq w", PlacementKey.FromFen("rnbq x KQkq - 0 1"));
    }

    [Fact]
    public void NormalisesEquivalentPositionsToTheSameKey()
    {
        string? a = PlacementKey.FromFen("8/8/8/8/8/8/8/8 w KQkq e3 0 1");
        string? b = PlacementKey.FromFen("8/8/8/8/8/8/8/8 w - - 7 42");
        Assert.Equal(a, b);
    }
}
