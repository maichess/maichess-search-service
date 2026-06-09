using System.Globalization;
using System.Text;
using System.Text.Json;
using MaichessSearchService.Search.Documents;

namespace MaichessSearchService.Search.Indexing;

// Pure transform: a Debezium Mongo change event (raw JSON off match.cdc.v1) -> the
// IndexCommand(s) to apply to Elasticsearch. Stateless and deterministic, so replaying
// the same change yields the same documents (ES upserts are idempotent on the stable
// doc ids). The CdcIndexer wires this to the live consumer/ES client; the reindex job
// reuses ProjectGame / ProjectMatch directly on backfilled documents — the same
// projection feeds both paths, so testing the projection covers both.
//
// Routing: match.cdc.v1 carries both `matches` and `analysis_games` (RegexRouter folds
// them onto one topic, see kafka-connect.yaml); the source collection in the envelope
// selects the projection. op c/r/u -> upsert; op d -> delete; anything else -> nothing.
internal static class CdcDocumentMapper
{
    internal static IReadOnlyList<IndexCommand> Map(string? key, string cdcValueJson)
    {
        if (string.IsNullOrWhiteSpace(cdcValueJson))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(cdcValueJson);
        JsonElement change = Unwrap(doc.RootElement);

        if (change.ValueKind != JsonValueKind.Object
            || !TryGetString(change, "op", out string op))
        {
            return [];
        }

        string kind = CollectionKind(change);
        if (kind.Length == 0)
        {
            return [];
        }

        return op switch
        {
            "c" or "r" or "u" => Upsert(change, key, kind),
            "d" => Delete(change, key, kind),
            _ => [],
        };
    }

    // Projects a backfilled analysis_games document (id supplied separately by the
    // reindex job, which reads it from the DatabaseService record).
    internal static IndexCommand.UpsertGame ProjectGame(JsonElement game, string id)
    {
        string userId = Canonical.UserId(GetString(game, "user_id"));
        string white = DictName(game, "white");
        string black = DictName(game, "black");
        JsonElement? tags = GetObject(game, "tags");
        string opening = TagValue(tags, "Opening", "opening");
        string eco = TagValue(tags, "ECO", "eco");
        string pgn = GetString(game, "pgn");
        string matchId = GetString(game, "match_id");

        GameDoc gameDoc = new(
            GameId: id,
            UserId: userId,
            Source: GetString(game, "source"),
            MatchId: matchId.Length == 0 ? null : matchId,
            White: white,
            Black: black,
            Result: GetString(game, "result"),
            Opening: opening,
            Eco: eco,
            Text: BuildText(pgn, white, black, opening, eco, tags),
            CreatedAtMs: ReadCreatedAtMs(game));

        string startingFen = GetString(game, "starting_fen");
        List<string> fens = [];
        if (startingFen.Length > 0)
        {
            fens.Add(startingFen);
        }

        fens.AddRange(GetStringList(game, "fens"));

        IReadOnlyList<PositionDoc> positions = BuildPositions("game", id, [userId], fens, white, black);
        return new IndexCommand.UpsertGame(gameDoc, positions);
    }

    // Projects a backfilled matches document.
    internal static IndexCommand.UpsertMatch ProjectMatch(JsonElement match, string id)
    {
        string whiteUser = Canonical.UserId(GetString(match, "white_user_id"));
        string blackUser = Canonical.UserId(GetString(match, "black_user_id"));
        string createdByUser = Canonical.UserId(GetString(match, "created_by_user_id"));

        List<string> ownerIds = [];
        foreach (string owner in new[] { whiteUser, blackUser, createdByUser })
        {
            if (owner.Length > 0 && !ownerIds.Contains(owner))
            {
                ownerIds.Add(owner);
            }
        }

        string white = PlayerLabel(match, "white");
        string black = PlayerLabel(match, "black");
        string source = GetString(match, "source");

        MatchDoc matchDoc = new(
            MatchId: id,
            OwnerIds: ownerIds,
            White: white,
            Black: black,
            Status: GetString(match, "status"),
            Source: source.Length == 0 ? "native" : source,
            ExternalProvider: GetString(match, "external_provider"),
            MoveCount: GetStringList(match, "moves").Count,
            FinishedAtMs: GetLong(match, "finished_at_ms"));

        IReadOnlyList<PositionDoc> positions =
            BuildPositions("match", id, ownerIds, GetStringList(match, "fen_history"), white, black);
        return new IndexCommand.UpsertMatch(matchDoc, positions);
    }

    private static IReadOnlyList<IndexCommand> Upsert(JsonElement change, string? key, string kind)
    {
        if (!TryGetDocJson(change, "after", out string afterJson))
        {
            return [];
        }

        using var after = JsonDocument.Parse(afterJson);
        string id = ReadId(after.RootElement);
        if (id.Length == 0)
        {
            id = ReadKeyId(key);
        }

        if (id.Length == 0)
        {
            return [];
        }

        return kind == "game"
            ? [ProjectGame(after.RootElement, id)]
            : [ProjectMatch(after.RootElement, id)];
    }

    private static IReadOnlyList<IndexCommand> Delete(JsonElement change, string? key, string kind)
    {
        string id = string.Empty;
        if (TryGetDocJson(change, "before", out string beforeJson))
        {
            using var before = JsonDocument.Parse(beforeJson);
            id = ReadId(before.RootElement);
        }

        if (id.Length == 0)
        {
            id = ReadKeyId(key);
        }

        return id.Length == 0 ? [] : [new IndexCommand.Delete(kind, id)];
    }

    private static List<PositionDoc> BuildPositions(
        string kind,
        string parentId,
        IReadOnlyList<string> ownerIds,
        List<string> fens,
        string white,
        string black)
    {
        List<PositionDoc> positions = [];
        for (int ply = 0; ply < fens.Count; ply++)
        {
            string? placementKey = PlacementKey.FromFen(fens[ply]);
            if (placementKey is null)
            {
                continue;
            }

            positions.Add(new PositionDoc(kind, parentId, ownerIds, ply, placementKey, fens[ply], white, black));
        }

        return positions;
    }

    private static string BuildText(string pgn, string white, string black, string opening, string eco, JsonElement? tags)
    {
        StringBuilder sb = new();
        sb.Append(pgn).Append(' ').Append(white).Append(' ').Append(black)
            .Append(' ').Append(opening).Append(' ').Append(eco);

        if (tags is { } t)
        {
            foreach (JsonProperty prop in t.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    sb.Append(' ').Append(prop.Value.GetString());
                }
            }
        }

        return sb.ToString().Trim();
    }

    // Debezium with the JSON converter + schemas wraps the event as
    // { "schema": {...}, "payload": {...} }; with schemas disabled the root is the event.
    private static JsonElement Unwrap(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("schema", out _)
        && root.TryGetProperty("payload", out JsonElement payload)
        && payload.ValueKind == JsonValueKind.Object
            ? payload
            : root;

    private static string CollectionKind(JsonElement change)
    {
        string collection = change.TryGetProperty("source", out JsonElement source)
            && source.ValueKind == JsonValueKind.Object
                ? GetString(source, "collection")
                : string.Empty;

        return collection switch
        {
            "analysis_games" => "game",
            "matches" => "match",
            _ => string.Empty,
        };
    }

    // The Mongo connector serialises `before`/`after` as a JSON *string* containing the
    // document; an ExtractNewDocumentState SMT (or non-Mongo source) would inline it as an
    // object. Accept both and hand back the raw document JSON.
    private static bool TryGetDocJson(JsonElement parent, string name, out string json)
    {
        json = string.Empty;
        if (!parent.TryGetProperty(name, out JsonElement el))
        {
            return false;
        }

        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                json = el.GetString()!;
                return json.Length > 0;
            case JsonValueKind.Object:
                json = el.GetRawText();
                return true;
            default:
                return false;
        }
    }

    private static string ReadId(JsonElement doc) =>
        doc.TryGetProperty("_id", out JsonElement idEl) ? ReadIdValue(idEl)
        : doc.TryGetProperty("id", out JsonElement plainId) ? ReadIdValue(plainId)
        : string.Empty;

    // Mongo's key payload carries `id` as a stringified extended-JSON document key,
    // e.g. {"id":"{\"_id\": \"abc\"}"}. Parse it out for deletes that lack a before-image.
    private static string ReadKeyId(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        try
        {
            using var keyDoc = JsonDocument.Parse(key);
            JsonElement payload = Unwrap(keyDoc.RootElement);
            if (!TryGetString(payload, "id", out string idJson))
            {
                return ReadId(payload);
            }

            using var inner = JsonDocument.Parse(idJson);
            return ReadId(inner.RootElement);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string ReadIdValue(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()!,
            JsonValueKind.Object => GetString(el, "$oid"),
            JsonValueKind.Number => el.GetRawText(),
            _ => string.Empty,
        };

    private static long ReadCreatedAtMs(JsonElement game)
    {
        if (game.TryGetProperty("created_at", out JsonElement el))
        {
            if (el.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    el.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed))
            {
                return parsed.ToUnixTimeMilliseconds();
            }

            if (el.ValueKind == JsonValueKind.Number || el.ValueKind == JsonValueKind.Object)
            {
                return GetLong(game, "created_at");
            }
        }

        return 0L;
    }

    private static string PlayerLabel(JsonElement match, string side)
    {
        string bot = GetString(match, $"{side}_bot_id");
        return bot.Length > 0 ? bot : GetString(match, $"{side}_user_id");
    }

    private static string DictName(JsonElement doc, string name) =>
        GetObject(doc, name) is { } obj ? GetString(obj, "name") : string.Empty;

    private static string TagValue(JsonElement? tags, params string[] names)
    {
        if (tags is not { } t)
        {
            return string.Empty;
        }

        foreach (string name in names)
        {
            foreach (JsonProperty prop in t.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String)
                {
                    return prop.Value.GetString()!;
                }
            }
        }

        return string.Empty;
    }

    private static JsonElement? GetObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Object
            ? el
            : null;

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()!
            : string.Empty;

    private static List<string> GetStringList(JsonElement obj, string name)
    {
        List<string> list = [];
        if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    list.Add(item.GetString()!);
                }
            }
        }

        return list;
    }

    // Reads a Mongo numeric field, tolerating both relaxed JSON (plain number) and
    // canonical extended JSON ({"$numberLong": "..."} etc.).
    private static long GetLong(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement el))
        {
            return 0L;
        }

        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetInt64(out long n) ? n : (long)el.GetDouble();
            case JsonValueKind.String:
                return long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s)
                    ? s
                    : 0L;
            case JsonValueKind.Object:
                foreach (string wrapper in new[] { "$numberLong", "$numberInt", "$numberDouble", "$date" })
                {
                    if (el.TryGetProperty(wrapper, out JsonElement inner))
                    {
                        return inner.ValueKind == JsonValueKind.String
                            && long.TryParse(inner.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long w)
                                ? w
                                : inner.ValueKind == JsonValueKind.Number && inner.TryGetInt64(out long wn)
                                    ? wn
                                    : 0L;
                    }
                }

                return 0L;
            default:
                return 0L;
        }
    }
}
