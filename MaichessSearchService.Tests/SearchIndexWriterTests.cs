using MaichessSearchService.Search.Documents;
using MaichessSearchService.Search.Indexing;
using MaichessSearchService.Tests.Support;
using Xunit;

namespace MaichessSearchService.Tests;

public class SearchIndexWriterTests
{
    private readonly FakeSearchIndex index = new();
    private readonly SearchIndexWriter writer;

    public SearchIndexWriterTests()
    {
        writer = new SearchIndexWriter(index);
    }

    [Fact]
    public async Task UpsertGameIndexesSummaryAndPositions()
    {
        GameDoc game = new("G1", "u1", "pgn", null, "w", "b", "w b", "1-0", "Open", "C20", "text", 10);
        PositionDoc pos = new("game", "G1", ["u1"], 0, "k w", "fen", "w", "b");

        await writer.ApplyAsync(new IndexCommand.UpsertGame(game, [pos]), CancellationToken.None);

        Assert.Equal(game, Assert.Single(index.Games));
        Assert.Equal(pos, Assert.Single(index.Positions));
        Assert.Empty(index.Matches);
    }

    [Fact]
    public async Task UpsertMatchIndexesSummaryAndPositions()
    {
        MatchDoc match = new("M1", ["u1"], "u1", "bot", "u1 bot", "white_won", "native", "", 3, 99);
        PositionDoc pos = new("match", "M1", ["u1"], 1, "k b", "fen", "u1", "bot");

        await writer.ApplyAsync(new IndexCommand.UpsertMatch(match, [pos]), CancellationToken.None);

        Assert.Equal(match, Assert.Single(index.Matches));
        Assert.Equal(pos, Assert.Single(index.Positions));
        Assert.Empty(index.Games);
    }

    [Fact]
    public async Task DeleteGameRoutesToGameDeletion()
    {
        await writer.ApplyAsync(new IndexCommand.Delete("game", "G9"), CancellationToken.None);

        Assert.Equal("G9", Assert.Single(index.DeletedGames));
        Assert.Empty(index.DeletedMatches);
    }

    [Fact]
    public async Task DeleteMatchRoutesToMatchDeletion()
    {
        await writer.ApplyAsync(new IndexCommand.Delete("match", "M9"), CancellationToken.None);

        Assert.Equal("M9", Assert.Single(index.DeletedMatches));
        Assert.Empty(index.DeletedGames);
    }

    [Fact]
    public void PositionDocIdIsDeterministic()
    {
        PositionDoc pos = new("game", "G1", ["u1"], 7, "k w", "fen", "w", "b");
        Assert.Equal("game:G1:7", pos.DocId);
    }
}
