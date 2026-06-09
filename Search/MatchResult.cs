namespace MaichessSearchService.Search;

// A matches search hit: ids + summary only; detail is hydrated from match-manager.
internal sealed record MatchResult(
    string MatchId,
    string White,
    string Black,
    string Status,
    string Source,
    string ExternalProvider,
    int MoveCount,
    long FinishedAtMs);
