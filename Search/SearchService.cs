namespace MaichessSearchService.Search;

// Builds auth-scoped, paged queries from REST request parameters and runs them against
// the search index. All scoping (UserId is always forced onto the query), paging
// normalisation, and FEN -> placement-key folding live here; the index seam only
// executes the resulting query. This is the testable heart of the API layer.
internal sealed class SearchService(ISearchIndex index)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private static readonly string[] ValidScopes = ["games", "matches", "all"];

    internal Task<SearchPage<GameResult>> SearchGamesAsync(
        string userId,
        string? text,
        string? opponent,
        string? opening,
        string? result,
        string? source,
        long? fromMs,
        long? toMs,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        (int p, int ps) = NormalisePaging(page, pageSize);
        GameQuery query = new(
            UserId: Canonical.UserId(userId),
            Text: Blank(text),
            Opponent: Blank(opponent),
            Opening: Blank(opening),
            Result: Blank(result),
            Source: Blank(source),
            FromMs: fromMs,
            ToMs: toMs,
            Page: p,
            PageSize: ps);
        return index.SearchGamesAsync(query, ct);
    }

    internal Task<SearchPage<MatchResult>> SearchMatchesAsync(
        string userId,
        string? opponent,
        string? result,
        string? source,
        string? externalProvider,
        long? fromMs,
        long? toMs,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        (int p, int ps) = NormalisePaging(page, pageSize);
        MatchQuery query = new(
            UserId: Canonical.UserId(userId),
            Opponent: Blank(opponent),
            Result: Blank(result),
            Source: Blank(source),
            ExternalProvider: Blank(externalProvider),
            FromMs: fromMs,
            ToMs: toMs,
            Page: p,
            PageSize: ps);
        return index.SearchMatchesAsync(query, ct);
    }

    internal Task<SearchPage<PositionResult>> SearchPositionsAsync(
        string userId,
        string? fen,
        string? scope,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        string placementKey = PlacementKey.FromFen(fen)
            ?? throw new SearchValidationException("fen is required and must contain a piece placement");

        string resolvedScope = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim().ToLowerInvariant();
        if (!ValidScopes.Contains(resolvedScope))
        {
            throw new SearchValidationException("scope must be one of: games, matches, all");
        }

        (int p, int ps) = NormalisePaging(page, pageSize);
        PositionQuery query = new(
            UserId: Canonical.UserId(userId),
            PlacementKey: placementKey,
            Scope: resolvedScope,
            Page: p,
            PageSize: ps);
        return index.SearchPositionsAsync(query, ct);
    }

    private static (int Page, int PageSize) NormalisePaging(int page, int pageSize)
    {
        int p = page < 1 ? 1 : page;
        int ps = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };
        return (p, ps);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
