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

Task 24 (searchable names + partial matching) likewise only edits `rest/search.md` (added
`q` to `/search/matches` + name-matching semantics) — still no PlatformProtos bump.

## Searchable names: matches limited to ids (task 24)

`CdcDocumentMapper` indexes a `names` blob so free-text/opponent can find players by
username, bot name, or id with **prefix matching** (`edge_ngram`). This is **asymmetric by
index** and that is intentional, not a gap to "fix" by reaching across services:

- **Games** carry resolved usernames/bot names already (analysis service denormalised them),
  so games get full name search.
- **Matches** carry **only ids** in match-db ("best-effort id labels" per
  `search-service.md`; clients hydrate names from match-manager). Resolving human usernames /
  bot display names for matches at index time would require a **user replica** (consume
  `user.events.v1`) + a bot-name cache inside search-service, which would break the indexer's
  pure, I/O-free `CdcDocumentMapper` (the thing that makes CDC replay + reindex testable and
  idempotent). **Deferred as a follow-up**, not implemented here. Match free-text therefore
  matches user-ids / bot-ids only.

## Mapping change needs a reindex (task 24)

The new `names` field + `edge_ngram` analyzer settings are applied by
`EnsureIndexesAsync` **only when an index is absent** (it never mutates an existing
mapping). Existing `analysis_games` / `matches` indexes keep the old mapping until rebuilt.
**Rollout:** ship the new mapping, then run the one-shot reindex Job
(`searchService.reindex=true`, or `dotnet run -- --reindex`) — it drops nothing but
re-projects every Mongo document through the same `ProjectGame`/`ProjectMatch` path, so the
`names` blob is backfilled. (For a clean staging env, deleting the two indexes before the
reindex forces the new mapping; ES will not change an existing field's analyzer in place.)

The `ElasticSearchIndex` JSON (analyzer settings + query bodies) is `[ExcludeFromCodeCoverage]`
live-infra and so is verified by **manual ES checks**, not unit tests; the projection that
builds the `names`/display values (`CdcDocumentMapper`) and the query records
(`SearchService`) are fully unit-tested.

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
