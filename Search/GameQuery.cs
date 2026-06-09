namespace MaichessSearchService.Search;

// Auth-scoped, normalised query for /search/games (built by SearchService, executed by
// ISearchIndex). UserId is always set; paging is already clamped.
internal sealed record GameQuery(
    string UserId,
    string? Text,
    string? Opponent,
    string? Opening,
    string? Result,
    string? Source,
    long? FromMs,
    long? ToMs,
    int Page,
    int PageSize);
