namespace MaichessSearchService.Search;

// A position search hit: which game/match reached the position and at what ply.
internal sealed record PositionResult(
    string Kind,
    string Id,
    int Ply,
    string Fen,
    string White,
    string Black);
