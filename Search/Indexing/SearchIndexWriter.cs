namespace MaichessSearchService.Search.Indexing;

// Applies a projected IndexCommand to the search index. The CDC consumer and the reindex
// job both funnel their projections through here so the upsert/delete semantics (a summary
// upsert always re-indexes that document's position entries; a delete removes summary +
// positions) live in exactly one tested place.
internal sealed class SearchIndexWriter(ISearchIndex index)
{
    internal async Task ApplyAsync(IndexCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case IndexCommand.UpsertGame upsertGame:
                await index.IndexGameAsync(upsertGame.Game, ct);
                await index.IndexPositionsAsync(upsertGame.Positions, ct);
                break;
            case IndexCommand.UpsertMatch upsertMatch:
                await index.IndexMatchAsync(upsertMatch.Match, ct);
                await index.IndexPositionsAsync(upsertMatch.Positions, ct);
                break;
            case IndexCommand.Delete { Kind: "game" } deleteGame:
                await index.DeleteGameAsync(deleteGame.Id, ct);
                break;
            case IndexCommand.Delete deleteMatch:
                await index.DeleteMatchAsync(deleteMatch.Id, ct);
                break;
        }
    }
}
