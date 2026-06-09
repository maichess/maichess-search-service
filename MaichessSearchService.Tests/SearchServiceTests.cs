using MaichessSearchService.Search;
using MaichessSearchService.Tests.Support;
using Xunit;

namespace MaichessSearchService.Tests;

public class SearchServiceTests
{
    private const string Guid1 = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";
    private const string Canon1 = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private readonly FakeSearchIndex index = new();
    private readonly SearchService service;

    public SearchServiceTests()
    {
        service = new SearchService(index);
    }

    [Fact]
    public async Task GamesScopesToCanonicalUserAndForwardsFacets()
    {
        await service.SearchGamesAsync(
            Guid1, " sicilian ", "Magnus", "Sicilian", "1-0", "match", 100, 200, 2, 50, CancellationToken.None);

        GameQuery q = Assert.IsType<GameQuery>(index.LastGameQuery);
        Assert.Equal(Canon1, q.UserId);
        Assert.Equal("sicilian", q.Text);
        Assert.Equal("Magnus", q.Opponent);
        Assert.Equal("Sicilian", q.Opening);
        Assert.Equal("1-0", q.Result);
        Assert.Equal("match", q.Source);
        Assert.Equal(100, q.FromMs);
        Assert.Equal(200, q.ToMs);
        Assert.Equal(2, q.Page);
        Assert.Equal(50, q.PageSize);
    }

    [Fact]
    public async Task GamesBlanksWhitespaceFacetsToNull()
    {
        await service.SearchGamesAsync(
            "u", "   ", "", null, "  ", null, null, null, 1, 20, CancellationToken.None);

        GameQuery q = index.LastGameQuery!;
        Assert.Null(q.Text);
        Assert.Null(q.Opponent);
        Assert.Null(q.Opening);
        Assert.Null(q.Result);
        Assert.Null(q.Source);
        Assert.Null(q.FromMs);
        Assert.Null(q.ToMs);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public async Task PageIsClampedToAtLeastOne(int requested, int expected)
    {
        await service.SearchGamesAsync("u", null, null, null, null, null, null, null, requested, 20, CancellationToken.None);
        Assert.Equal(expected, index.LastGameQuery!.Page);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(5000, 100)]
    public async Task PageSizeDefaultsAndCaps(int requested, int expected)
    {
        await service.SearchGamesAsync("u", null, null, null, null, null, null, null, 1, requested, CancellationToken.None);
        Assert.Equal(expected, index.LastGameQuery!.PageSize);
    }

    [Fact]
    public async Task MatchesScopesAndForwardsFacets()
    {
        await service.SearchMatchesAsync(
            Guid1, "Hikaru", "white_won", "external", "lichess", 1, 2, 1, 20, CancellationToken.None);

        MatchQuery q = Assert.IsType<MatchQuery>(index.LastMatchQuery);
        Assert.Equal(Canon1, q.UserId);
        Assert.Equal("Hikaru", q.Opponent);
        Assert.Equal("white_won", q.Result);
        Assert.Equal("external", q.Source);
        Assert.Equal("lichess", q.ExternalProvider);
        Assert.Equal(1, q.FromMs);
        Assert.Equal(2, q.ToMs);
    }

    [Fact]
    public async Task PositionsFoldsFenToPlacementKeyAndDefaultsScope()
    {
        await service.SearchPositionsAsync(
            Guid1, "8/8/8/8/8/8/8/8 b - - 5 9", null, 1, 20, CancellationToken.None);

        PositionQuery q = Assert.IsType<PositionQuery>(index.LastPositionQuery);
        Assert.Equal(Canon1, q.UserId);
        Assert.Equal("8/8/8/8/8/8/8/8 b", q.PlacementKey);
        Assert.Equal("all", q.Scope);
    }

    [Theory]
    [InlineData("games")]
    [InlineData("matches")]
    [InlineData("ALL")]
    public async Task PositionsAcceptsValidScopes(string scope)
    {
        await service.SearchPositionsAsync(
            "u", "8/8/8/8/8/8/8/8 w", scope, 1, 20, CancellationToken.None);
        Assert.Equal(scope.ToLowerInvariant(), index.LastPositionQuery!.Scope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PositionsRejectsMissingFen(string? fen)
    {
        await Assert.ThrowsAsync<SearchValidationException>(() =>
            service.SearchPositionsAsync("u", fen, null, 1, 20, CancellationToken.None));
    }

    [Fact]
    public async Task PositionsRejectsInvalidScope()
    {
        await Assert.ThrowsAsync<SearchValidationException>(() =>
            service.SearchPositionsAsync("u", "8/8/8/8/8/8/8/8 w", "openings", 1, 20, CancellationToken.None));
    }
}
