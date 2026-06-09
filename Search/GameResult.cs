namespace MaichessSearchService.Search;

// A games search hit: ids + summary only; detail is hydrated from analysis-service.
internal sealed record GameResult(
    string GameId,
    string White,
    string Black,
    string Result,
    string Opening,
    string Eco,
    string Source,
    long CreatedAtMs);
