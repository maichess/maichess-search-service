namespace MaichessSearchService.Search;

// A page of search results plus the total hit count and echoed paging — the JSON shape
// returned by every /search endpoint.
internal sealed record SearchPage<T>(IReadOnlyList<T> Results, long Total, int Page, int PageSize);
