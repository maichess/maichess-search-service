using System.Globalization;

namespace MaichessSearchService.Search;

// Canonicalises user-id strings so auth scoping matches regardless of how the id was
// serialised. match-manager canonicalises stored ids to lowercase Guid "D" form (see the
// Past Matches fix in feature-prompts/08); we mirror that here so the JWT `sub` on a
// search request and the owner ids projected into ES compare equal. Non-Guid ids (bots,
// external) pass through unchanged.
internal static class Canonical
{
    internal static string UserId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return string.Empty;
        }

        return Guid.TryParse(id, out Guid g)
            ? g.ToString("D", CultureInfo.InvariantCulture)
            : id;
    }
}
