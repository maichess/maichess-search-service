using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MaichessSearchService.Search.Documents;

namespace MaichessSearchService.Search.Elastic;

// The one place that talks to Elasticsearch — a documented exception to the
// persist-via-DatabaseService rule (ES is a derived, rebuildable read model, like the
// Redis cache; see CONTRACT_NOTES.md). Implemented over the ES REST API with HttpClient +
// System.Text.Json so the wire field names and queries are fully under our control and the
// build has no heavyweight typed-client dependency; the ISearchIndex seam lets this be
// swapped for the typed client later without touching any tested code.
//
// Excluded from coverage: live-infrastructure adapter (needs a running ES). Everything
// that produces the documents/queries it receives is fully unit-tested.
[ExcludeFromCodeCoverage]
internal sealed class ElasticSearchIndex(HttpClient http) : ISearchIndex
{
    private const string GamesIndex = "analysis_games";
    private const string MatchesIndex = "matches";
    private const string PositionsIndex = "positions";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
    };

    private static readonly string[] GameTextFields = ["text", "white", "black", "opening"];

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        await CreateIfAbsentAsync(GamesIndex, GamesMapping(), ct);
        await CreateIfAbsentAsync(MatchesIndex, MatchesMapping(), ct);
        await CreateIfAbsentAsync(PositionsIndex, PositionsMapping(), ct);
    }

    public async Task IndexGameAsync(GameDoc game, CancellationToken ct)
    {
        object doc = new
        {
            game_id = game.GameId,
            user_id = game.UserId,
            source = game.Source,
            match_id = game.MatchId,
            white = game.White,
            black = game.Black,
            result = game.Result,
            opening = game.Opening,
            eco = game.Eco,
            text = game.Text,
            created_at_ms = game.CreatedAtMs,
        };
        await PutDocAsync(GamesIndex, game.GameId, doc, ct);
    }

    public async Task IndexMatchAsync(MatchDoc match, CancellationToken ct)
    {
        object doc = new
        {
            match_id = match.MatchId,
            owner_ids = match.OwnerIds,
            white = match.White,
            black = match.Black,
            status = match.Status,
            source = match.Source,
            external_provider = match.ExternalProvider,
            move_count = match.MoveCount,
            finished_at_ms = match.FinishedAtMs,
        };
        await PutDocAsync(MatchesIndex, match.MatchId, doc, ct);
    }

    public async Task IndexPositionsAsync(IReadOnlyList<PositionDoc> positions, CancellationToken ct)
    {
        if (positions.Count == 0)
        {
            return;
        }

        StringBuilder ndjson = new();
        foreach (PositionDoc p in positions)
        {
            ndjson.Append(JsonSerializer.Serialize(new { index = new { _id = p.DocId } }, Json)).Append('\n');
            ndjson.Append(JsonSerializer.Serialize(
                new
                {
                    kind = p.Kind,
                    parent_id = p.ParentId,
                    owner_ids = p.OwnerIds,
                    ply = p.Ply,
                    placement_key = p.PlacementKey,
                    fen = p.Fen,
                    white = p.White,
                    black = p.Black,
                },
                Json)).Append('\n');
        }

        using StringContent content = new(ndjson.ToString(), Encoding.UTF8, "application/x-ndjson");
        HttpResponseMessage resp = await http.PostAsync(Rel($"/{PositionsIndex}/_bulk?refresh=wait_for"), content, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteGameAsync(string gameId, CancellationToken ct)
    {
        await DeleteDocAsync(GamesIndex, gameId, ct);
        await DeletePositionsAsync("game", gameId, ct);
    }

    public async Task DeleteMatchAsync(string matchId, CancellationToken ct)
    {
        await DeleteDocAsync(MatchesIndex, matchId, ct);
        await DeletePositionsAsync("match", matchId, ct);
    }

    public async Task<SearchPage<GameResult>> SearchGamesAsync(GameQuery query, CancellationToken ct)
    {
        List<object> filters = [Term("user_id", query.UserId)];
        List<object> musts = [];

        if (query.Text is not null)
        {
            musts.Add(new { multi_match = new { query = query.Text, fields = GameTextFields } });
        }

        if (query.Opponent is not null)
        {
            filters.Add(ShouldMatch(query.Opponent, "white", "black"));
        }

        if (query.Opening is not null)
        {
            filters.Add(ShouldMatch(query.Opening, "opening", "eco"));
        }

        AddTerm(filters, "result", query.Result);
        AddTerm(filters, "source", query.Source);
        AddRange(filters, "created_at_ms", query.FromMs, query.ToMs);

        object body = SearchBody(filters, musts, query.Page, query.PageSize, "created_at_ms");
        JsonElement hits = await RunSearchAsync(GamesIndex, body, ct);

        List<GameResult> results = [];
        foreach (JsonElement src in Sources(hits))
        {
            results.Add(new GameResult(
                GameId: Str(src, "game_id"),
                White: Str(src, "white"),
                Black: Str(src, "black"),
                Result: Str(src, "result"),
                Opening: Str(src, "opening"),
                Eco: Str(src, "eco"),
                Source: Str(src, "source"),
                CreatedAtMs: Long(src, "created_at_ms")));
        }

        return new SearchPage<GameResult>(results, Total(hits), query.Page, query.PageSize);
    }

    public async Task<SearchPage<MatchResult>> SearchMatchesAsync(MatchQuery query, CancellationToken ct)
    {
        List<object> filters = [Term("owner_ids", query.UserId)];

        if (query.Opponent is not null)
        {
            filters.Add(ShouldMatch(query.Opponent, "white", "black"));
        }

        AddTerm(filters, "status", query.Result);
        AddTerm(filters, "source", query.Source);
        AddTerm(filters, "external_provider", query.ExternalProvider);
        AddRange(filters, "finished_at_ms", query.FromMs, query.ToMs);

        object body = SearchBody(filters, [], query.Page, query.PageSize, "finished_at_ms");
        JsonElement hits = await RunSearchAsync(MatchesIndex, body, ct);

        List<MatchResult> results = [];
        foreach (JsonElement src in Sources(hits))
        {
            results.Add(new MatchResult(
                MatchId: Str(src, "match_id"),
                White: Str(src, "white"),
                Black: Str(src, "black"),
                Status: Str(src, "status"),
                Source: Str(src, "source"),
                ExternalProvider: Str(src, "external_provider"),
                MoveCount: (int)Long(src, "move_count"),
                FinishedAtMs: Long(src, "finished_at_ms")));
        }

        return new SearchPage<MatchResult>(results, Total(hits), query.Page, query.PageSize);
    }

    public async Task<SearchPage<PositionResult>> SearchPositionsAsync(PositionQuery query, CancellationToken ct)
    {
        List<object> filters =
        [
            Term("owner_ids", query.UserId),
            Term("placement_key", query.PlacementKey),
        ];

        if (query.Scope is "games" or "matches")
        {
            filters.Add(Term("kind", query.Scope == "games" ? "game" : "match"));
        }

        object body = SearchBody(filters, [], query.Page, query.PageSize, "ply");
        JsonElement hits = await RunSearchAsync(PositionsIndex, body, ct);

        List<PositionResult> results = [];
        foreach (JsonElement src in Sources(hits))
        {
            results.Add(new PositionResult(
                Kind: Str(src, "kind"),
                Id: Str(src, "parent_id"),
                Ply: (int)Long(src, "ply"),
                Fen: Str(src, "fen"),
                White: Str(src, "white"),
                Black: Str(src, "black")));
        }

        return new SearchPage<PositionResult>(results, Total(hits), query.Page, query.PageSize);
    }

    private static object SearchBody(List<object> filters, List<object> musts, int page, int pageSize, string sortField)
    {
        bool ascending = sortField == "ply";
        return new
        {
            from = (page - 1) * pageSize,
            size = pageSize,
            track_total_hits = true,
            query = new { @bool = new { filter = filters, must = musts } },
            sort = new object[] { new Dictionary<string, object> { [sortField] = new { order = ascending ? "asc" : "desc" } } },
        };
    }

    private static object Term(string field, string value) =>
        new { term = new Dictionary<string, object> { [field] = value } };

    private static object ShouldMatch(string value, params string[] fields) =>
        new
        {
            @bool = new
            {
                should = fields.Select(f => (object)new { match = new Dictionary<string, object> { [f] = value } }).ToArray(),
                minimum_should_match = 1,
            },
        };

    private static void AddTerm(List<object> filters, string field, string? value)
    {
        if (value is not null)
        {
            filters.Add(Term(field, value));
        }
    }

    private static void AddRange(List<object> filters, string field, long? from, long? to)
    {
        if (from is null && to is null)
        {
            return;
        }

        Dictionary<string, object> bounds = [];
        if (from is { } f)
        {
            bounds["gte"] = f;
        }

        if (to is { } t)
        {
            bounds["lte"] = t;
        }

        filters.Add(new { range = new Dictionary<string, object> { [field] = bounds } });
    }

    private static IEnumerable<JsonElement> Sources(JsonElement hits)
    {
        foreach (JsonElement hit in hits.GetProperty("hits").EnumerateArray())
        {
            yield return hit.GetProperty("_source");
        }
    }

    private static long Total(JsonElement hits) =>
        hits.GetProperty("total").GetProperty("value").GetInt64();

    private static string Str(JsonElement src, string name) =>
        src.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

    private static long Long(JsonElement src, string name) =>
        src.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number
            ? el.GetInt64()
            : 0L;

    private async Task<JsonElement> RunSearchAsync(string index, object body, CancellationToken ct)
    {
        HttpResponseMessage resp = await http.PostAsJsonAsync(Rel($"/{index}/_search"), body, Json, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("hits").Clone();
    }

    private async Task PutDocAsync(string index, string id, object doc, CancellationToken ct)
    {
        HttpResponseMessage resp = await http.PutAsJsonAsync(
            Rel($"/{index}/_doc/{Uri.EscapeDataString(id)}?refresh=wait_for"), doc, Json, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task DeleteDocAsync(string index, string id, CancellationToken ct)
    {
        HttpResponseMessage resp = await http.DeleteAsync(
            Rel($"/{index}/_doc/{Uri.EscapeDataString(id)}?refresh=wait_for"), ct);
        if (resp.StatusCode != HttpStatusCode.NotFound)
        {
            resp.EnsureSuccessStatusCode();
        }
    }

    private async Task DeletePositionsAsync(string kind, string parentId, CancellationToken ct)
    {
        object body = new
        {
            query = new
            {
                @bool = new { filter = new object[] { Term("kind", kind), Term("parent_id", parentId) } },
            },
        };
        HttpResponseMessage resp = await http.PostAsJsonAsync(
            Rel($"/{PositionsIndex}/_delete_by_query?refresh=true"), body, Json, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task CreateIfAbsentAsync(string index, object mapping, CancellationToken ct)
    {
        using HttpRequestMessage head = new(HttpMethod.Head, Rel($"/{index}"));
        HttpResponseMessage exists = await http.SendAsync(head, ct);
        if (exists.StatusCode == HttpStatusCode.OK)
        {
            return;
        }

        HttpResponseMessage resp = await http.PutAsJsonAsync(Rel($"/{index}"), mapping, Json, ct);
        if (resp.StatusCode != HttpStatusCode.BadRequest)
        {
            // 400 = resource_already_exists (a racing replica created it first); tolerate.
            resp.EnsureSuccessStatusCode();
        }
    }

    private static object GamesMapping() => new
    {
        mappings = new
        {
            properties = new Dictionary<string, object>
            {
                ["game_id"] = Keyword(),
                ["user_id"] = Keyword(),
                ["source"] = Keyword(),
                ["match_id"] = Keyword(),
                ["white"] = TextWithKeyword(),
                ["black"] = TextWithKeyword(),
                ["result"] = Keyword(),
                ["opening"] = TextWithKeyword(),
                ["eco"] = Keyword(),
                ["text"] = Text(),
                ["created_at_ms"] = LongType(),
            },
        },
    };

    private static object MatchesMapping() => new
    {
        mappings = new
        {
            properties = new Dictionary<string, object>
            {
                ["match_id"] = Keyword(),
                ["owner_ids"] = Keyword(),
                ["white"] = TextWithKeyword(),
                ["black"] = TextWithKeyword(),
                ["status"] = Keyword(),
                ["source"] = Keyword(),
                ["external_provider"] = Keyword(),
                ["move_count"] = IntType(),
                ["finished_at_ms"] = LongType(),
            },
        },
    };

    private static object PositionsMapping() => new
    {
        mappings = new
        {
            properties = new Dictionary<string, object>
            {
                ["kind"] = Keyword(),
                ["parent_id"] = Keyword(),
                ["owner_ids"] = Keyword(),
                ["ply"] = IntType(),
                ["placement_key"] = Keyword(),
                ["fen"] = Keyword(),
                ["white"] = Keyword(),
                ["black"] = Keyword(),
            },
        },
    };

    private static Uri Rel(string path) => new(path, UriKind.Relative);

    private static object Keyword() => new { type = "keyword" };

    private static object Text() => new { type = "text" };

    private static object TextWithKeyword() => new
    {
        type = "text",
        fields = new { keyword = new { type = "keyword", ignore_above = 256 } },
    };

    private static object LongType() => new { type = "long" };

    private static object IntType() => new { type = "integer" };
}
