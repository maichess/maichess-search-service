namespace MaichessSearchService.Search.Documents;

// A projected analysis_games row in the `analysis_games` ES index. Carries only the
// summary + facet fields the search API returns; full game detail is hydrated from
// analysis-service by id. `Text` is the concatenated free-text blob (PGN + headers).
// `Names` is the searchable name blob — every human username, bot name, and id of both
// players — indexed into the partial-matching (edge_ngram) `names` field so free-text and
// opponent queries find usernames/bot names by full or partial token (task 24).
internal sealed record GameDoc(
    string GameId,
    string UserId,
    string Source,
    string? MatchId,
    string White,
    string Black,
    string Names,
    string Result,
    string Opening,
    string Eco,
    string Text,
    long CreatedAtMs);
