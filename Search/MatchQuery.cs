namespace MaichessSearchService.Search;

// Auth-scoped, normalised query for /search/matches.
internal sealed record MatchQuery(
    string UserId,
    string? Text,
    string? Opponent,
    string? Result,
    string? Source,
    string? ExternalProvider,
    long? FromMs,
    long? ToMs,
    int Page,
    int PageSize);
