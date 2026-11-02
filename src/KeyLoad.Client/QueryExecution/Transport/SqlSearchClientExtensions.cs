using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Executes the bounded SQL graph-search profile through the shared database transport.</summary>
public static class SqlSearchClientExtensions
{
    private const string SearchPath = "/query/search";

    /// <summary>Compiles SQL graph operators and returns ranked hits with separate projected context.</summary>
    public static Task<Result<GraphSearchResult>> SearchSqlAsync(this KeyLoadClient client,
        SqlGraphSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request SqlGraphSearchRequest reference through its object parameter. */;
        return client.Send<GraphSearchResult, SqlGraphSearchRequest>(SearchPath, request, false, null, cancellationToken);
    }
}
