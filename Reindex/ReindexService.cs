using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Maichess.Database.V1;
using MaichessSearchService.Search;
using MaichessSearchService.Search.Indexing;

namespace MaichessSearchService.Reindex;

// Rebuilds Elasticsearch from the authoritative Mongo collections via DatabaseService —
// the recovery path the ADR mandates (ES is derived, never a source of truth; drop the
// cluster and run this to fully recover). Reads each document, reuses the exact same
// projection the CDC indexer uses (CdcDocumentMapper.ProjectGame / ProjectMatch), and
// applies it through SearchIndexWriter. Runs on first rollout and on demand (Helm Job /
// `--reindex` arg).
//
// Excluded from coverage: needs live DatabaseService + ES. The projection it reuses is
// fully unit-tested.
[ExcludeFromCodeCoverage]
internal sealed class ReindexService(
    Database.DatabaseClient db,
    ISearchIndex index,
    SearchIndexWriter writer,
    ILogger<ReindexService> logger)
{
    private const int BatchSize = 200;

    internal async Task ReindexAllAsync(CancellationToken ct)
    {
        await index.EnsureIndexesAsync(ct);

        int games = await ReindexCollectionAsync(
            "analysis_games",
            (doc, id) => CdcDocumentMapper.ProjectGame(doc, id),
            ct);
        int matches = await ReindexCollectionAsync(
            "matches",
            (doc, id) => CdcDocumentMapper.ProjectMatch(doc, id),
            ct);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Reindex complete: {Games} games, {Matches} matches", games, matches);
        }
    }

    private async Task<int> ReindexCollectionAsync(
        string collection,
        Func<JsonElement, string, IndexCommand> project,
        CancellationToken ct)
    {
        int offset = 0;
        int total = 0;
        while (true)
        {
            ListResponse response = await db.ListAsync(
                new ListRequest { Collection = collection, Limit = BatchSize, Offset = offset },
                cancellationToken: ct);

            if (response.Records.Count == 0)
            {
                break;
            }

            foreach (Struct record in response.Records)
            {
                string id = record.Fields.TryGetValue("id", out Value? idVal)
                    && idVal.KindCase == Value.KindOneofCase.StringValue
                        ? idVal.StringValue
                        : string.Empty;
                if (id.Length == 0)
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(JsonFormatter.Default.Format(record));
                await writer.ApplyAsync(project(doc.RootElement, id), ct);
                total++;
            }

            offset += response.Records.Count;
            if (response.Records.Count < BatchSize)
            {
                break;
            }
        }

        return total;
    }
}
