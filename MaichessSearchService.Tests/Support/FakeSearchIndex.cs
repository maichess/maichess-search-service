using MaichessSearchService.Search;
using MaichessSearchService.Search.Documents;

namespace MaichessSearchService.Tests.Support;

// In-memory ISearchIndex that records the documents/queries it receives, so tests can
// assert what the projection / SearchService produced without a live Elasticsearch.
internal sealed class FakeSearchIndex : ISearchIndex
{
    internal List<GameDoc> Games { get; } = [];

    internal List<MatchDoc> Matches { get; } = [];

    internal List<PositionDoc> Positions { get; } = [];

    internal List<string> DeletedGames { get; } = [];

    internal List<string> DeletedMatches { get; } = [];

    internal int EnsureCalls { get; private set; }

    internal GameQuery? LastGameQuery { get; private set; }

    internal MatchQuery? LastMatchQuery { get; private set; }

    internal PositionQuery? LastPositionQuery { get; private set; }

    public Task EnsureIndexesAsync(CancellationToken ct)
    {
        EnsureCalls++;
        return Task.CompletedTask;
    }

    public Task IndexGameAsync(GameDoc game, CancellationToken ct)
    {
        Games.Add(game);
        return Task.CompletedTask;
    }

    public Task IndexMatchAsync(MatchDoc match, CancellationToken ct)
    {
        Matches.Add(match);
        return Task.CompletedTask;
    }

    public Task IndexPositionsAsync(IReadOnlyList<PositionDoc> positions, CancellationToken ct)
    {
        Positions.AddRange(positions);
        return Task.CompletedTask;
    }

    public Task DeleteGameAsync(string gameId, CancellationToken ct)
    {
        DeletedGames.Add(gameId);
        return Task.CompletedTask;
    }

    public Task DeleteMatchAsync(string matchId, CancellationToken ct)
    {
        DeletedMatches.Add(matchId);
        return Task.CompletedTask;
    }

    public Task<SearchPage<GameResult>> SearchGamesAsync(GameQuery query, CancellationToken ct)
    {
        LastGameQuery = query;
        return Task.FromResult(new SearchPage<GameResult>([], 0, query.Page, query.PageSize));
    }

    public Task<SearchPage<MatchResult>> SearchMatchesAsync(MatchQuery query, CancellationToken ct)
    {
        LastMatchQuery = query;
        return Task.FromResult(new SearchPage<MatchResult>([], 0, query.Page, query.PageSize));
    }

    public Task<SearchPage<PositionResult>> SearchPositionsAsync(PositionQuery query, CancellationToken ct)
    {
        LastPositionQuery = query;
        return Task.FromResult(new SearchPage<PositionResult>([], 0, query.Page, query.PageSize));
    }
}
