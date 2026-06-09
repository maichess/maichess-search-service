using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using MaichessSearchService.Search;
using Microsoft.AspNetCore.Mvc;

namespace MaichessSearchService.Rest;

// Thin HTTP adapter over SearchService. Excluded from coverage (REST endpoint handler
// per the project convention); all behaviour — scoping, paging, FEN folding, validation —
// lives in the tested SearchService.
[ExcludeFromCodeCoverage]
internal static class SearchEndpoints
{
    internal static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/search").RequireAuthorization();
        group.MapGet("/games", Games);
        group.MapGet("/matches", Matches);
        group.MapGet("/positions", Positions);
        return routes;
    }

    private static async Task<IResult> Games(
        ClaimsPrincipal principal,
        SearchService service,
        CancellationToken ct,
        [FromQuery] string? q = null,
        [FromQuery] string? opponent = null,
        [FromQuery] string? opening = null,
        [FromQuery] string? result = null,
        [FromQuery] string? source = null,
        [FromQuery(Name = "from_ms")] long? fromMs = null,
        [FromQuery(Name = "to_ms")] long? toMs = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20)
    {
        if (!TryGetUserId(principal, out string userId))
        {
            return Results.Unauthorized();
        }

        SearchPage<GameResult> results = await service.SearchGamesAsync(
            userId, q, opponent, opening, result, source, fromMs, toMs, page, pageSize, ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> Matches(
        ClaimsPrincipal principal,
        SearchService service,
        CancellationToken ct,
        [FromQuery] string? opponent = null,
        [FromQuery] string? result = null,
        [FromQuery] string? source = null,
        [FromQuery(Name = "external_provider")] string? externalProvider = null,
        [FromQuery(Name = "from_ms")] long? fromMs = null,
        [FromQuery(Name = "to_ms")] long? toMs = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20)
    {
        if (!TryGetUserId(principal, out string userId))
        {
            return Results.Unauthorized();
        }

        SearchPage<MatchResult> results = await service.SearchMatchesAsync(
            userId, opponent, result, source, externalProvider, fromMs, toMs, page, pageSize, ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> Positions(
        ClaimsPrincipal principal,
        SearchService service,
        CancellationToken ct,
        [FromQuery] string? fen = null,
        [FromQuery] string? scope = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20)
    {
        if (!TryGetUserId(principal, out string userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            SearchPage<PositionResult> results =
                await service.SearchPositionsAsync(userId, fen, scope, page, pageSize, ct);
            return Results.Ok(results);
        }
        catch (SearchValidationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out string userId)
    {
        string? value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        userId = value ?? string.Empty;
        return value is not null;
    }
}
