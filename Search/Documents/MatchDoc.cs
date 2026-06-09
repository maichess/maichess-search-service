namespace MaichessSearchService.Search.Documents;

// A projected matches row in the `matches` ES index. match-db stores only player ids
// (no display names), so White/Black are best-effort identifiers (bot id, else user id);
// clients hydrate real names from match-manager. `OwnerIds` is the canonical set of user
// ids that may search this match (white, black, created_by) and drives auth scoping.
internal sealed record MatchDoc(
    string MatchId,
    IReadOnlyList<string> OwnerIds,
    string White,
    string Black,
    string Status,
    string Source,
    string ExternalProvider,
    int MoveCount,
    long FinishedAtMs);
