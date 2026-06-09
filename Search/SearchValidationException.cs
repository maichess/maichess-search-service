namespace MaichessSearchService.Search;

// Raised by SearchService for client-side input errors (bad FEN, bad scope). The REST
// adapter maps it to 400; nothing else catches it.
internal sealed class SearchValidationException : Exception
{
    internal SearchValidationException(string message)
        : base(message)
    {
    }

    internal SearchValidationException()
    {
    }

    internal SearchValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
