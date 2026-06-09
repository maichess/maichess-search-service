using MaichessSearchService.Search.Documents;

namespace MaichessSearchService.Search;

// The seam over Elasticsearch — the one place allowed to talk to ES directly (a
// documented exception to persist-via-DatabaseService, on the same grounds as the Redis
// read model; see CONTRACT_NOTES.md). The concrete ElasticSearchIndex is excluded from
// coverage as live infrastructure; everything that builds the commands/queries it receives
// (CdcDocumentMapper, SearchIndexWriter, SearchService) is fully tested against this seam.
internal interface ISearchIndex
{
    // Creates the analysis_games / matches / positions indexes + mappings if absent.
    Task EnsureIndexesAsync(CancellationToken ct);

    Task IndexGameAsync(GameDoc game, CancellationToken ct);

    Task IndexMatchAsync(MatchDoc match, CancellationToken ct);

    Task IndexPositionsAsync(IReadOnlyList<PositionDoc> positions, CancellationToken ct);

    // Removes a game/match summary and every position entry under it.
    Task DeleteGameAsync(string gameId, CancellationToken ct);

    Task DeleteMatchAsync(string matchId, CancellationToken ct);

    Task<SearchPage<GameResult>> SearchGamesAsync(GameQuery query, CancellationToken ct);

    Task<SearchPage<MatchResult>> SearchMatchesAsync(MatchQuery query, CancellationToken ct);

    Task<SearchPage<PositionResult>> SearchPositionsAsync(PositionQuery query, CancellationToken ct);
}
