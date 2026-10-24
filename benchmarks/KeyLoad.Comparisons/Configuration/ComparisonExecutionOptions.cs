namespace KeyLoad.Comparisons;

public sealed partial class ComparisonExecutionOptions
{
    internal const int RequiredDocumentClientConnections = 1;
    internal const int RequiredDocumentClientResponseHeadersKilobytes = 1024;
    private const int RequiredDocumentCleanupMinutes = 20;
    internal static readonly TimeSpan RequiredDocumentCleanupTimeout = TimeSpan.FromMinutes(RequiredDocumentCleanupMinutes);
    /// <summary>Additional execution primary connection reserved for untimed final verification.</summary>
    public int DocumentVerificationConnectionReserve { get; set; } = RequiredDocumentClientConnections;
    /// <summary>One actual connection per independently admitted HTTP document client.</summary>
    public int DocumentClientMaxConnections { get; set; } = RequiredDocumentClientConnections;
    /// <summary>Response header bound of the document HTTP client; OpenSearch answers a synchronous update-by-query with one TASK_RESOURCE_USAGE header per scroll batch, about 230 KB for one million documents.</summary>
    public int DocumentClientMaxResponseHeadersKilobytes { get; set; } = RequiredDocumentClientResponseHeadersKilobytes;
    /// <summary>Joined cleanup bound for a complete million-record execution document namespace.</summary>
    public TimeSpan DocumentCleanupTimeout { get; set; } = RequiredDocumentCleanupTimeout;
}
