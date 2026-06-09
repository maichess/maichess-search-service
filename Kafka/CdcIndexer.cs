using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using MaichessSearchService.Search;
using MaichessSearchService.Search.Indexing;

namespace MaichessSearchService.Kafka;

// Consumes the raw Debezium Mongo change stream match.cdc.v1 (matches + analysis_games,
// folded onto one topic by the connector's RegexRouter) and projects each change into
// Elasticsearch via the pure CdcDocumentMapper + SearchIndexWriter. This is the indexer
// half of the service: the ADR forbids the owning services from writing ES, so the index
// is fed only from CDC here.
//
// Excluded from coverage: Kafka consume plumbing requiring a live broker. The projection
// (CdcDocumentMapper) and the apply semantics (SearchIndexWriter) are fully unit-tested.
[ExcludeFromCodeCoverage]
internal sealed class CdcIndexer : BackgroundService
{
    private const string CdcTopic = "match.cdc.v1";
    private const string ConsumerGroup = "search-cdc-indexer";

    private readonly ISearchIndex index;
    private readonly SearchIndexWriter writer;
    private readonly ILogger<CdcIndexer> logger;
    private readonly string bootstrap;

    public CdcIndexer(ISearchIndex index, SearchIndexWriter writer, IConfiguration config, ILogger<CdcIndexer> logger)
    {
        this.index = index;
        this.writer = writer;
        this.logger = logger;
        bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP")
            ?? config["Kafka:Bootstrap"]
            ?? "kafka:9092";
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => Run(stoppingToken), stoppingToken);

    private async Task Run(CancellationToken stoppingToken)
    {
        await index.EnsureIndexesAsync(stoppingToken);

        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(
                new ConsumerConfig
                {
                    BootstrapServers = bootstrap,
                    GroupId = ConsumerGroup,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnableAutoCommit = false,
                })
            .Build();

        consumer.Subscribe(CdcTopic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value is null)
                {
                    continue;
                }

                foreach (IndexCommand command in CdcDocumentMapper.Map(result.Message.Key, result.Message.Value))
                {
                    await writer.ApplyAsync(command, stoppingToken);
                }

                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            consumer.Close();
        }
    }
}
