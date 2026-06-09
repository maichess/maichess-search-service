namespace MaichessSearchService.Search.Documents;

// One entry per ply in the `positions` index. The ES `_id` is deterministic
// (`{kind}:{parent_id}:{ply}`) so re-indexing the same game/match overwrites rather than
// duplicates. `OwnerIds` scopes the lookup to the caller's own games/matches.
internal sealed record PositionDoc(
    string Kind,
    string ParentId,
    IReadOnlyList<string> OwnerIds,
    int Ply,
    string PlacementKey,
    string Fen,
    string White,
    string Black)
{
    // Stable document id — idempotent on reindex / CDC replay.
    internal string DocId => $"{Kind}:{ParentId}:{Ply}";
}
