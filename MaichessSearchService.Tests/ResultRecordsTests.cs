using MaichessSearchService.Search;
using Xunit;

namespace MaichessSearchService.Tests;

// Covers the result records returned to clients and the validation-exception ctors
// (required by the analyzer's standard-exception rule).
public class ResultRecordsTests
{
    [Fact]
    public void GameResultExposesAllFields()
    {
        GameResult r = new("G1", "w", "b", "1-0", "Sicilian", "B20", "match", 42);
        Assert.Equal("G1", r.GameId);
        Assert.Equal("w", r.White);
        Assert.Equal("b", r.Black);
        Assert.Equal("1-0", r.Result);
        Assert.Equal("Sicilian", r.Opening);
        Assert.Equal("B20", r.Eco);
        Assert.Equal("match", r.Source);
        Assert.Equal(42, r.CreatedAtMs);
    }

    [Fact]
    public void MatchResultExposesAllFields()
    {
        MatchResult r = new("M1", "w", "b", "white_won", "external", "lichess", 7, 99);
        Assert.Equal("M1", r.MatchId);
        Assert.Equal("w", r.White);
        Assert.Equal("b", r.Black);
        Assert.Equal("white_won", r.Status);
        Assert.Equal("external", r.Source);
        Assert.Equal("lichess", r.ExternalProvider);
        Assert.Equal(7, r.MoveCount);
        Assert.Equal(99, r.FinishedAtMs);
    }

    [Fact]
    public void PositionResultExposesAllFields()
    {
        PositionResult r = new("game", "G1", 12, "fen", "w", "b");
        Assert.Equal("game", r.Kind);
        Assert.Equal("G1", r.Id);
        Assert.Equal(12, r.Ply);
        Assert.Equal("fen", r.Fen);
        Assert.Equal("w", r.White);
        Assert.Equal("b", r.Black);
    }

    [Fact]
    public void SearchPageCarriesResultsAndPaging()
    {
        SearchPage<GameResult> page = new([], 5, 2, 20);
        Assert.Empty(page.Results);
        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public void ValidationExceptionConstructors()
    {
        Assert.Equal("bad", new SearchValidationException("bad").Message);
        Assert.NotNull(new SearchValidationException().Message);
        Exception inner = new InvalidOperationException("x");
        SearchValidationException withInner = new("outer", inner);
        Assert.Same(inner, withInner.InnerException);
    }
}
