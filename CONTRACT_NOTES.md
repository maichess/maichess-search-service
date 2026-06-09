# Contract Notes — maichess-search-service

Implements `maichess-knowledge-base/tasks/implemented/13` (Caching Stage 5). See
[search-elasticsearch.md](../../maichess-knowledge-base/search-elasticsearch.md) and
[change-data-capture.md](../../maichess-knowledge-base/change-data-capture.md).

## Direct Elasticsearch access (documented exception)

This service talks to **Elasticsearch directly**, bypassing the generic DatabaseService
gRPC CRUD contract that every domain store must otherwise go through (root `CLAUDE.md`,
overview convention 6). This is an explicit, sanctioned exception on the same grounds as
the Redis read model: **Elasticsearch is a derived, rebuildable read/search engine, not a
system of record.** Mongo (behind match-db's DatabaseService) stays authoritative; every
ES index is reconstructible from the source collections via the reindex Job / CDC replay.
If the ES cluster is lost, nothing is lost.

The ES seam is `ISearchIndex`; the only implementation that touches ES is
`Search/Elastic/ElasticSearchIndex.cs` (`[ExcludeFromCodeCoverage]`, like other live-infra
adapters). All projection and query-building logic is behind the seam and fully unit-tested.

### ES client choice: REST + HttpClient, not the typed client

`search-elasticsearch.md` says "via the official client." We instead use the **Elasticsearch
REST API directly over `HttpClient` + `System.Text.Json`**. Rationale:

- The wire field names and query bodies stay fully under our control (no serializer-naming
  surprises), and the build carries no heavyweight typed-client dependency.
- The decision is isolated behind `ISearchIndex`; swapping in `Elastic.Clients.Elasticsearch`
  later is a single-file change that touches no tested code.

This is a minor deviation from the ADR wording (not its intent — "direct ES access via the
official REST surface"); recorded here per the Contract Policy. No objection raised; proceed.

## No proto / PlatformProtos bump

The only contract this feature adds is the **REST** spec
`maichess-api-contracts/rest/search.md` (authored before the code). REST specs are Markdown,
not part of the published `Maichess.PlatformProtos` package, so there is **no tag/publish
handoff** for this prompt. The service consumes the existing `database.proto` for the reindex
path and pins `Maichess.PlatformProtos` **0.4.0** — the same version as bot-arena and
user-service (no version reconciliation needed).

## CDC feed assumptions

- Fed only from `match.cdc.v1` (Debezium Mongo connector in `kafka-connect.yaml`, capturing
  both `matches` and `analysis_games`, routed onto one topic). analysis-service and
  match-manager never call ES.
- The connector runs with `capture.mode=change_streams_update_full` so updates carry the
  full post-image the projection needs. Deletes resolve the id from the change `before`
  image or the message key.
- Per-game/match position entries use a deterministic ES `_id` (`{kind}:{parent_id}:{ply}`),
  so CDC replay and reindex are idempotent (upsert, never duplicate). A match's plies only
  grow, so re-indexing overwrites in place; `DeleteGame`/`DeleteMatch` remove the summary
  plus all position entries.
