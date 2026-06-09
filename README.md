# maichess-search-service

Search over a derived **Elasticsearch** read model: games-library search, Past Matches
facets, and FEN **position search**. Implements `maichess-knowledge-base/tasks/implemented/13` (Caching Stage 5).

ES is **never a source of truth** — Mongo (behind match-db's DatabaseService) stays
authoritative and every index is rebuildable from it. The index is fed only from CDC
(`match.cdc.v1`); analysis-service and match-manager never write ES. See
[search-elasticsearch.md](../../maichess-knowledge-base/search-elasticsearch.md),
[change-data-capture.md](../../maichess-knowledge-base/change-data-capture.md), and
[CONTRACT_NOTES.md](CONTRACT_NOTES.md).

## Two halves

1. **Indexer** (`Kafka/CdcIndexer.cs`) — consumes the raw Debezium Mongo change stream
   `match.cdc.v1` (matches + analysis_games) and projects each change into the
   `analysis_games`, `matches`, and `positions` ES indexes via the pure
   `CdcDocumentMapper` + `SearchIndexWriter`. Enabled when `Cdc:Enabled` (Helm sets it when
   `kafkaConnect.enabled`).
2. **Search API** (`Rest/SearchEndpoints.cs` → `SearchService` → `ISearchIndex`) — the REST
   contract in `maichess-api-contracts/rest/search.md`:
   - `GET /search/games` — faceted/full-text over the caller's analysis games.
   - `GET /search/matches` — faceted Past Matches for the caller.
   - `GET /search/positions?fen=` — games/matches that reached a position (exact
     placement-key term match).

   Auth is the shared JWT (`JwtBearer` + `access_token` cookie). Results carry ids + summary
   fields; clients hydrate detail from the owning service.

## Reindex / backfill

`Reindex/ReindexService.cs` rebuilds every index from Mongo via DatabaseService — the
recovery path the ADR requires. Run it as the Helm Job (`searchService.reindex=true`) or
locally:

```
dotnet run -- --reindex
```

## Position search

One entry per ply: `{ game_id|match_id, ply, placement_key, fen }`, where `placement_key`
is the FEN piece-placement + side-to-move, move counters dropped (`Search/PlacementKey.cs`).
A position lookup is therefore a single exact-term query.

## Layout

| Path | Role |
|---|---|
| `Search/PlacementKey.cs` | FEN → normalised placement key (tested) |
| `Search/Canonical.cs` | user-id canonicalisation for auth scoping (tested) |
| `Search/Indexing/CdcDocumentMapper.cs` | CDC change → index commands (tested) |
| `Search/Indexing/SearchIndexWriter.cs` | apply commands to the index seam (tested) |
| `Search/SearchService.cs` | scope + page + FEN-fold queries (tested) |
| `Search/ISearchIndex.cs` | the ES seam |
| `Search/Elastic/ElasticSearchIndex.cs` | ES REST adapter (excluded — live infra) |
| `Kafka/CdcIndexer.cs` | CDC consumer (excluded — live broker) |
| `Reindex/ReindexService.cs` | Mongo → ES backfill (excluded — live deps) |
| `Rest/SearchEndpoints.cs` | HTTP adapter (excluded) |

## Build / test

```
dotnet build
dotnet test MaichessSearchService.Tests/MaichessSearchService.Tests.csproj -p:CollectCoverage=true
```

100% line/branch/method coverage on non-excluded code (exclusions mirror the project
convention: ES/Kafka/reindex adapters, REST handlers, `Program.cs`).

### Mutation testing

```
dotnet tool restore
cd MaichessSearchService.Tests && dotnet stryker
```

Stryker mirrors the coverage exclusions (`stryker-config.json`).
