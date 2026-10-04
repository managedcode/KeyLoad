using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Executes the bounded SQL graph-search profile through the shared database transport.</summary>
public static class SqlSearchClientExtensions
{
    private const string SearchPath = "/v1/query/search";

    /// <summary>Compiles SQL graph operators and returns ranked hits with separate projected context.</summary>
    public static Task<Result<GraphSearchResult>> SearchSqlAsync(this KeyLoadClient client,
        SqlGraphSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<GraphSearchResult>(SearchPath, request, cancellationToken);
    }
}
