namespace MaichessSearchService.Search.Documents;

// A projected analysis_games row in the `analysis_games` ES index. Carries only the
// summary + facet fields the search API returns; full game detail is hydrated from
// analysis-service by id. `Text` is the concatenated free-text blob (PGN + headers).
internal sealed record GameDoc(
    string GameId,
    string UserId,
    string Source,
    string? MatchId,
    string White,
    string Black,
    string Result,
    string Opening,
    string Eco,
    string Text,
    long CreatedAtMs);
