using System.Globalization;
using MaichessSearchService.Search;
using MaichessSearchService.Search.Indexing;
using MaichessSearchService.Tests.Support;
using Xunit;

namespace MaichessSearchService.Tests;

public class CdcDocumentMapperTests
{
    private const string Guid1 = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";
    private const string Canon1 = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static Dictionary<string, object?> FullGame() => new()
    {
        ["_id"] = "G1",
        ["user_id"] = Guid1,
        ["source"] = "pgn",
        ["match_id"] = "M1",
        ["starting_fen"] = StartFen,
        ["fens"] = new[] { "8/8/8/8/8/8/8/8 b - - 1 1", string.Empty },
        ["pgn"] = "[Event \"Test\"]",
        ["result"] = "1-0",
        ["white"] = new Dictionary<string, object?> { ["name"] = "Alice" },
        ["black"] = new Dictionary<string, object?> { ["name"] = "Bob" },
        ["tags"] = new Dictionary<string, object?>
        {
            ["Opening"] = "Sicilian",
            ["ECO"] = "B20",
            ["Event"] = "Test",
            ["Round"] = 3,
        },
        ["created_at"] = "2026-06-09T12:00:00.0000000+00:00",
    };

    private static Dictionary<string, object?> FullMatch() => new()
    {
        ["_id"] = "M1",
        ["white_user_id"] = Guid1,
        ["black_user_id"] = null,
        ["white_bot_id"] = null,
        ["black_bot_id"] = "bot-sf",
        ["created_by_user_id"] = Guid1,
        ["status"] = "white_won",
        ["source"] = string.Empty,
        ["external_provider"] = string.Empty,
        ["moves"] = new[] { "e2e4", "e7e5", "g1f3" },
        ["fen_history"] = new[] { StartFen, "8/8/8/8/8/8/8/8 b - - 1 1", "8/8/8/8/8/8/8/8 w - - 2 2" },
        ["finished_at_ms"] = 1234567890123L,
    };

    private static IndexCommand.UpsertGame MapGame(Dictionary<string, object?> doc) =>
        Assert.IsType<IndexCommand.UpsertGame>(
            Assert.Single(CdcDocumentMapper.Map(null, CdcEnvelope.Build("c", "analysis_games", doc))));

    private static IndexCommand.UpsertMatch MapMatch(Dictionary<string, object?> doc) =>
        Assert.IsType<IndexCommand.UpsertMatch>(
            Assert.Single(CdcDocumentMapper.Map(null, CdcEnvelope.Build("c", "matches", doc))));

    [Fact]
    public void ProjectsGameSummary()
    {
        IndexCommand.UpsertGame cmd = MapGame(FullGame());

        Assert.Equal("G1", cmd.Game.GameId);
        Assert.Equal(Canon1, cmd.Game.UserId);
        Assert.Equal("pgn", cmd.Game.Source);
        Assert.Equal("M1", cmd.Game.MatchId);
        Assert.Equal("Alice", cmd.Game.White);
        Assert.Equal("Bob", cmd.Game.Black);
        Assert.Equal("1-0", cmd.Game.Result);
        Assert.Equal("Sicilian", cmd.Game.Opening);
        Assert.Equal("B20", cmd.Game.Eco);
        Assert.Equal(
            DateTimeOffset.Parse("2026-06-09T12:00:00.0000000+00:00", CultureInfo.InvariantCulture).ToUnixTimeMilliseconds(),
            cmd.Game.CreatedAtMs);
    }

    [Fact]
    public void GameTextBlobIncludesHeadersAndSkipsNonStringTags()
    {
        IndexCommand.UpsertGame cmd = MapGame(FullGame());

        Assert.Contains("Alice", cmd.Game.Text, StringComparison.Ordinal);
        Assert.Contains("Sicilian", cmd.Game.Text, StringComparison.Ordinal);
        Assert.Contains("Test", cmd.Game.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Round", cmd.Game.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void GamePositionsStartAtPlyZeroAndSkipUnparsableFens()
    {
        IndexCommand.UpsertGame cmd = MapGame(FullGame());

        Assert.Equal(2, cmd.Positions.Count);
        Assert.Equal(0, cmd.Positions[0].Ply);
        Assert.Equal(PlacementKey.FromFen(StartFen), cmd.Positions[0].PlacementKey);
        Assert.Equal(1, cmd.Positions[1].Ply);
        Assert.Equal("game", cmd.Positions[0].Kind);
        Assert.Equal("G1", cmd.Positions[0].ParentId);
        Assert.Equal([Canon1], cmd.Positions[0].OwnerIds);
    }

    [Fact]
    public void GameWithoutStartingFenOnlyIndexesMoveFens()
    {
        Dictionary<string, object?> doc = FullGame();
        doc["starting_fen"] = string.Empty;
        doc["fens"] = new[] { "8/8/8/8/8/8/8/8 b - - 1 1" };

        IndexCommand.UpsertGame cmd = MapGame(doc);

        Assert.Equal(0, Assert.Single(cmd.Positions).Ply);
    }

    [Fact]
    public void GameMissingMatchIdMapsToNull()
    {
        Dictionary<string, object?> doc = FullGame();
        doc.Remove("match_id");
        Assert.Null(MapGame(doc).Game.MatchId);
    }

    [Fact]
    public void GameMissingOptionalFieldsDefaultToEmpty()
    {
        Dictionary<string, object?> doc = new()
        {
            ["_id"] = "G2",
            ["user_id"] = "bot",
        };

        IndexCommand.UpsertGame cmd = MapGame(doc);

        Assert.Equal(string.Empty, cmd.Game.White);
        Assert.Equal(string.Empty, cmd.Game.Black);
        Assert.Equal(string.Empty, cmd.Game.Opening);
        Assert.Equal(string.Empty, cmd.Game.Eco);
        Assert.Empty(cmd.Positions);
    }

    [Theory]
    [InlineData("opening", "French")]
    [InlineData("OPENING", "French")]
    public void OpeningTagIsCaseInsensitive(string tagKey, string expected)
    {
        Dictionary<string, object?> doc = FullGame();
        doc["tags"] = new Dictionary<string, object?> { [tagKey] = expected };
        Assert.Equal(expected, MapGame(doc).Game.Opening);
    }

    [Fact]
    public void NonStringTagValueIsIgnored()
    {
        Dictionary<string, object?> doc = FullGame();
        doc["tags"] = new Dictionary<string, object?> { ["Opening"] = 42 };
        Assert.Equal(string.Empty, MapGame(doc).Game.Opening);
    }

    [Theory]
    [InlineData("notadate", 0L)]
    public void InvalidCreatedAtStringBecomesZero(string value, long expected)
    {
        Dictionary<string, object?> doc = FullGame();
        doc["created_at"] = value;
        Assert.Equal(expected, MapGame(doc).Game.CreatedAtMs);
    }

    [Fact]
    public void CreatedAtAbsentBecomesZero()
    {
        Dictionary<string, object?> doc = FullGame();
        doc.Remove("created_at");
        Assert.Equal(0L, MapGame(doc).Game.CreatedAtMs);
    }

    [Fact]
    public void CreatedAtNumberIsReadAsEpochMs()
    {
        Dictionary<string, object?> doc = FullGame();
        doc["created_at"] = 5000L;
        Assert.Equal(5000L, MapGame(doc).Game.CreatedAtMs);
    }

    [Fact]
    public void CreatedAtExtendedJsonDateIsReadAsEpochMs()
    {
        Dictionary<string, object?> doc = FullGame();
        doc["created_at"] = new Dictionary<string, object?> { ["$date"] = 1700000000000L };
        Assert.Equal(1700000000000L, MapGame(doc).Game.CreatedAtMs);
    }

    [Fact]
    public void ProjectsMatchSummaryWithDedupedOwners()
    {
        IndexCommand.UpsertMatch cmd = MapMatch(FullMatch());

        Assert.Equal("M1", cmd.Match.MatchId);
        Assert.Equal([Canon1], cmd.Match.OwnerIds);
        Assert.Equal(Guid1, cmd.Match.White);
        Assert.Equal("bot-sf", cmd.Match.Black);
        Assert.Equal("white_won", cmd.Match.Status);
        Assert.Equal("native", cmd.Match.Source);
        Assert.Equal(3, cmd.Match.MoveCount);
        Assert.Equal(1234567890123L, cmd.Match.FinishedAtMs);
        Assert.Equal(3, cmd.Positions.Count);
        Assert.Equal("match", cmd.Positions[0].Kind);
    }

    [Fact]
    public void MatchKeepsExplicitSourceAndProvider()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["source"] = "external";
        doc["external_provider"] = "lichess";
        IndexCommand.UpsertMatch cmd = MapMatch(doc);
        Assert.Equal("external", cmd.Match.Source);
        Assert.Equal("lichess", cmd.Match.ExternalProvider);
    }

    [Fact]
    public void MatchCollectsAllDistinctOwners()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["white_user_id"] = "11111111-1111-1111-1111-111111111111";
        doc["black_user_id"] = "22222222-2222-2222-2222-222222222222";
        doc["created_by_user_id"] = "33333333-3333-3333-3333-333333333333";

        Assert.Equal(3, MapMatch(doc).Match.OwnerIds.Count);
    }

    [Fact]
    public void MatchPrefersBotIdForDisplayLabel()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["white_bot_id"] = "bot-white";
        Assert.Equal("bot-white", MapMatch(doc).Match.White);
    }

    [Theory]
    [InlineData(null, 0L)]
    [InlineData("999", 999L)]
    [InlineData("abc", 0L)]
    public void FinishedAtStringAndMissingVariants(string? value, long expected)
    {
        Dictionary<string, object?> doc = FullMatch();
        if (value is null)
        {
            doc.Remove("finished_at_ms");
        }
        else
        {
            doc["finished_at_ms"] = value;
        }

        Assert.Equal(expected, MapMatch(doc).Match.FinishedAtMs);
    }

    [Fact]
    public void FinishedAtDoubleIsTruncated()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["finished_at_ms"] = 5.9;
        Assert.Equal(5L, MapMatch(doc).Match.FinishedAtMs);
    }

    [Fact]
    public void FinishedAtNumberLongStringWrapper()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["finished_at_ms"] = new Dictionary<string, object?> { ["$numberLong"] = "42" };
        Assert.Equal(42L, MapMatch(doc).Match.FinishedAtMs);
    }

    [Fact]
    public void FinishedAtNumberLongNumericWrapper()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["finished_at_ms"] = new Dictionary<string, object?> { ["$numberLong"] = 8L };
        Assert.Equal(8L, MapMatch(doc).Match.FinishedAtMs);
    }

    [Fact]
    public void FinishedAtNumberLongUnparsableWrapper()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["finished_at_ms"] = new Dictionary<string, object?> { ["$numberLong"] = "nope" };
        Assert.Equal(0L, MapMatch(doc).Match.FinishedAtMs);
    }

    [Fact]
    public void FinishedAtNumberIntWrapperAdvancesPastMissingLong()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["finished_at_ms"] = new Dictionary<string, object?> { ["$numberInt"] = "7" };
        Assert.Equal(7L, MapMatch(doc).Match.FinishedAtMs);
    }

    [Fact]
    public void FinishedAtUnknownObjectWrapperBecomesZero()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["finished_at_ms"] = new Dictionary<string, object?> { ["something"] = "1" };
        Assert.Equal(0L, MapMatch(doc).Match.FinishedAtMs);
    }

    [Fact]
    public void SnapshotOpProjectsGame() =>
        Assert.IsType<IndexCommand.UpsertGame>(
            Assert.Single(CdcDocumentMapper.Map(null, CdcEnvelope.Build("r", "analysis_games", FullGame()))));

    [Fact]
    public void UpdateOpProjectsMatch() =>
        Assert.IsType<IndexCommand.UpsertMatch>(
            Assert.Single(CdcDocumentMapper.Map(null, CdcEnvelope.Build("u", "matches", FullMatch()))));

    [Fact]
    public void SchemaWrappedEnvelopeIsUnwrapped() =>
        Assert.IsType<IndexCommand.UpsertGame>(
            Assert.Single(CdcDocumentMapper.Map(
                null, CdcEnvelope.Build("c", "analysis_games", FullGame(), wrapSchema: true))));

    [Fact]
    public void AfterInlinedAsObjectIsAccepted() =>
        Assert.IsType<IndexCommand.UpsertMatch>(
            Assert.Single(CdcDocumentMapper.Map(
                null, CdcEnvelope.Build("c", "matches", FullMatch(), afterAsObject: true))));

    [Fact]
    public void DeleteWithBeforeImageRemovesGame()
    {
        IndexCommand.Delete del = Assert.IsType<IndexCommand.Delete>(
            Assert.Single(CdcDocumentMapper.Map(
                null, CdcEnvelope.Build("d", "analysis_games", before: new Dictionary<string, object?> { ["_id"] = "G5" }))));

        Assert.Equal("game", del.Kind);
        Assert.Equal("G5", del.Id);
    }

    [Fact]
    public void DeleteFallsBackToKeyForId()
    {
        IndexCommand.Delete del = Assert.IsType<IndexCommand.Delete>(
            Assert.Single(CdcDocumentMapper.Map(CdcEnvelope.Key("M5"), CdcEnvelope.Build("d", "matches"))));

        Assert.Equal("match", del.Kind);
        Assert.Equal("M5", del.Id);
    }

    [Fact]
    public void DeleteWithoutResolvableIdYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(null, CdcEnvelope.Build("d", "matches")));

    [Fact]
    public void UpsertUsesKeyWhenAfterHasNoId()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc.Remove("_id");
        IndexCommand.UpsertMatch cmd = Assert.IsType<IndexCommand.UpsertMatch>(
            Assert.Single(CdcDocumentMapper.Map(CdcEnvelope.Key("M7"), CdcEnvelope.Build("c", "matches", doc))));
        Assert.Equal("M7", cmd.Match.MatchId);
    }

    [Fact]
    public void UpsertReadsPlainIdField()
    {
        Dictionary<string, object?> doc = FullGame();
        doc.Remove("_id");
        doc["id"] = "G8";
        Assert.Equal("G8", MapGame2(doc).Game.GameId);
    }

    [Fact]
    public void IdFromOidWrapper()
    {
        Dictionary<string, object?> doc = FullGame();
        doc["_id"] = new Dictionary<string, object?> { ["$oid"] = "deadbeef" };
        Assert.Equal("deadbeef", MapGame2(doc).Game.GameId);
    }

    [Fact]
    public void NumericIdIsStringified()
    {
        Dictionary<string, object?> doc = FullGame();
        doc["_id"] = 123L;
        Assert.Equal("123", MapGame2(doc).Game.GameId);
    }

    [Fact]
    public void UnresolvableIdYieldsNothing()
    {
        Dictionary<string, object?> doc = FullGame();
        doc["_id"] = true;
        Assert.Empty(CdcDocumentMapper.Map(null, CdcEnvelope.Build("c", "analysis_games", doc)));
    }

    [Fact]
    public void UpsertWithoutAfterYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(null, CdcEnvelope.Build("c", "analysis_games")));

    [Fact]
    public void EmptyAfterStringYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(
            null, "{\"op\":\"c\",\"source\":{\"collection\":\"matches\"},\"after\":\"\"}"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankValueYieldsNothing(string value) =>
        Assert.Empty(CdcDocumentMapper.Map(null, value));

    [Fact]
    public void NonObjectValueYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(null, "[]"));

    [Fact]
    public void NullAfterValueYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(
            null, "{\"op\":\"c\",\"source\":{\"collection\":\"matches\"},\"after\":null}"));

    [Fact]
    public void FinishedAtNonNumericKindBecomesZero()
    {
        Dictionary<string, object?> doc = FullMatch();
        doc["finished_at_ms"] = true;
        Assert.Equal(0L, MapMatch(doc).Match.FinishedAtMs);
    }

    [Fact]
    public void MissingOpYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(null, CdcEnvelope.Build(null, "matches", FullMatch())));

    [Fact]
    public void NonStringOpYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(
            null, "{\"op\":5,\"source\":{\"collection\":\"matches\"}}"));

    [Fact]
    public void UnknownOpYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(null, CdcEnvelope.Build("t", "matches", FullMatch())));

    [Fact]
    public void UnknownCollectionYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(null, CdcEnvelope.Build("c", "users", FullMatch())));

    [Fact]
    public void MissingSourceYieldsNothing() =>
        Assert.Empty(CdcDocumentMapper.Map(null, CdcEnvelope.Build("c", null, FullMatch())));

    [Fact]
    public void MalformedKeyIsIgnored() =>
        Assert.Empty(CdcDocumentMapper.Map("{not json", CdcEnvelope.Build("d", "matches")));

    [Fact]
    public void KeyWithoutIdFieldFallsBackToReadingDirectId()
    {
        // Key whose payload is itself the document key object (no nested "id" string).
        string key = "{\"_id\":\"M3\"}";
        IndexCommand.Delete del = Assert.IsType<IndexCommand.Delete>(
            Assert.Single(CdcDocumentMapper.Map(key, CdcEnvelope.Build("d", "matches"))));
        Assert.Equal("M3", del.Id);
    }

    private static IndexCommand.UpsertGame MapGame2(Dictionary<string, object?> doc) =>
        Assert.IsType<IndexCommand.UpsertGame>(
            Assert.Single(CdcDocumentMapper.Map(null, CdcEnvelope.Build("c", "analysis_games", doc))));
}
