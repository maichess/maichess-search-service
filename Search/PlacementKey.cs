namespace MaichessSearchService.Search;

// Normalises a FEN into the exact-match term used for position search.
//
// A full FEN is `<placement> <stm> <castling> <ep> <halfmove> <fullmove>`. Two games
// reach "the same position" when their piece placement (and, to be precise, the side to
// move) match — the move counters and castling/en-passant bookkeeping are irrelevant to
// "have we been here before?". So the key folds in field 1 (placement) plus field 2
// (side to move) and drops everything else. Storing one such key per ply turns position
// search into a single exact-term lookup instead of a board scan.
internal static class PlacementKey
{
    // Builds the normalised key from a FEN. Returns null when the FEN has no placement
    // field so callers can skip un-indexable plies rather than store an empty term.
    internal static string? FromFen(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            return null;
        }

        // A non-whitespace string always yields at least one field after splitting.
        string[] fields = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string placement = fields[0];

        // Side to move defaults to white when the FEN omits it (a bare placement string).
        string sideToMove = fields.Length > 1 && (fields[1] == "w" || fields[1] == "b")
            ? fields[1]
            : "w";

        return $"{placement} {sideToMove}";
    }
}
