namespace MaichessSearchService.Search.Documents;

// A projected matches row in the `matches` ES index. match-db stores only player ids
// (no display names), so White/Black are best-effort identifiers (bot id, else user id);
// clients hydrate real names from match-manager. `OwnerIds` is the canonical set of user
// ids that may search this match (white, black, created_by) and drives auth scoping.
// `Names` is the searchable name blob fed into the partial-matching `names` field. Because
// match-db carries only ids (no resolved usernames/bot display names), this is limited to
// the player user-ids and bot-ids — full username/bot-name search for matches needs a user
// replica and is deferred (see CONTRACT_NOTES.md / search-service.md). Games, whose names
// the analysis service denormalises, do get full name search.
internal sealed record MatchDoc(
    string MatchId,
    IReadOnlyList<string> OwnerIds,
    string White,
    string Black,
    string Names,
    string Status,
    string Source,
    string ExternalProvider,
    int MoveCount,
    long FinishedAtMs);
