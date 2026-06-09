using System.Diagnostics.CodeAnalysis;

namespace MaichessSearchService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record ErrorResponse(string Error);
