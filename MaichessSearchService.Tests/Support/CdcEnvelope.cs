using System.Text.Json;

namespace MaichessSearchService.Tests.Support;

// Builds Debezium-Mongo-shaped CDC value JSON for the mapper tests. The Mongo connector
// serialises before/after as a JSON *string* containing the document, so by default the
// document object is embedded as a string (afterAsObject toggles the inlined-object shape
// an ExtractNewDocumentState SMT would produce).
internal static class CdcEnvelope
{
    internal static string Build(
        string? op,
        string? collection,
        object? after = null,
        object? before = null,
        bool wrapSchema = false,
        bool afterAsObject = false)
    {
        Dictionary<string, object?> payload = [];
        if (op is not null)
        {
            payload["op"] = op;
        }

        if (collection is not null)
        {
            payload["source"] = new Dictionary<string, object?> { ["collection"] = collection };
        }

        if (after is not null)
        {
            payload["after"] = afterAsObject ? after : JsonSerializer.Serialize(after);
        }

        if (before is not null)
        {
            payload["before"] = JsonSerializer.Serialize(before);
        }

        object root = wrapSchema
            ? new Dictionary<string, object?> { ["schema"] = new { }, ["payload"] = payload }
            : payload;

        return JsonSerializer.Serialize(root);
    }

    // A Mongo key payload: { "id": "{\"_id\": \"<id>\"}" } — a stringified document key.
    internal static string Key(string id) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = JsonSerializer.Serialize(new Dictionary<string, object?> { ["_id"] = id }),
        });
}
