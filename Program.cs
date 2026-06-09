using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grpc.Net.Client;
using Maichess.Database.V1;
using MaichessSearchService.Kafka;
using MaichessSearchService.Reindex;
using MaichessSearchService.Rest;
using MaichessSearchService.Search;
using MaichessSearchService.Search.Elastic;
using MaichessSearchService.Search.Indexing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

DotNetEnv.Env.Load();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// When run as the reindex Job (`--reindex`) the service backfills ES from Mongo and exits;
// it does not serve HTTP or consume CDC.
bool reindexMode = args.Contains("--reindex");

string dbServiceUrl = builder.Configuration["Services:DatabaseService"]
    ?? throw new InvalidOperationException("Services:DatabaseService is not configured");
string esUrl = builder.Configuration["Elasticsearch:Url"]
    ?? throw new InvalidOperationException("Elasticsearch:Url is not configured");

builder.Services.AddSingleton(new Database.DatabaseClient(GrpcChannel.ForAddress(dbServiceUrl)));

// A single long-lived HttpClient to the (stable, in-cluster) ES service is the recommended
// pattern and keeps the CDC BackgroundService's singleton dependency graph consistent.
builder.Services.AddSingleton<ISearchIndex>(_ =>
    new ElasticSearchIndex(new HttpClient { BaseAddress = new Uri(esUrl) }));
builder.Services.AddSingleton<SearchIndexWriter>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<ReindexService>();

if (reindexMode)
{
    using WebApplication reindexHost = builder.Build();
    ReindexService reindexer = reindexHost.Services.GetRequiredService<ReindexService>();
    await reindexer.ReindexAllAsync(CancellationToken.None);
    return;
}

string jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured");

if (builder.Configuration.GetValue("Cdc:Enabled", false))
{
    builder.Services.AddHostedService<CdcIndexer>();
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out string? token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
    ?? "http://otel-collector:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("search-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint)));

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok());
app.MapSearchEndpoints();

await app.RunAsync();
