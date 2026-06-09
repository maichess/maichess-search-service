using MaichessSearchService.Search.Documents;

namespace MaichessSearchService.Search.Indexing;

// The result of projecting one change event (or one backfilled document): the index
// mutation(s) to apply to Elasticsearch. A discriminated union over the three shapes the
// indexer ever needs. Kept transport-agnostic so the same projection feeds both the CDC
// consumer and the reindex job.
internal abstract record IndexCommand
{
    private IndexCommand()
    {
    }

    // Upsert an analysis game summary plus its per-ply position entries.
    internal sealed record UpsertGame(GameDoc Game, IReadOnlyList<PositionDoc> Positions) : IndexCommand;

    // Upsert a match summary plus its per-ply position entries.
    internal sealed record UpsertMatch(MatchDoc Match, IReadOnlyList<PositionDoc> Positions) : IndexCommand;

    // Remove a game or match (and its positions) by id. Kind is "game" or "match".
    internal sealed record Delete(string Kind, string Id) : IndexCommand;
}
