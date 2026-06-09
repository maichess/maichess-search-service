namespace MaichessSearchService.Search;

// Auth-scoped query for /search/positions: an exact placement-key lookup, optionally
// narrowed to games or matches.
internal sealed record PositionQuery(
    string UserId,
    string PlacementKey,
    string Scope,
    int Page,
    int PageSize);
