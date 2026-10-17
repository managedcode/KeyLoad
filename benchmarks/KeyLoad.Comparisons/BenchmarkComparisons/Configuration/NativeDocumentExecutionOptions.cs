namespace KeyLoad.Comparisons;

public sealed partial class NativeComparisonExecutionOptions
{
    internal const int RequiredDocumentClientConnections = 1;
    private const int RequiredDocumentCleanupMinutes = 20;
    internal static readonly TimeSpan RequiredDocumentCleanupTimeout = TimeSpan.FromMinutes(RequiredDocumentCleanupMinutes);
    /// <summary>Additional native primary connection reserved for untimed final verification.</summary>
    public int DocumentVerificationConnectionReserve { get; set; } = RequiredDocumentClientConnections;
    /// <summary>One actual connection per independently admitted HTTP document client.</summary>
    public int DocumentClientMaxConnections { get; set; } = RequiredDocumentClientConnections;
    /// <summary>Joined cleanup bound for a complete million-record native document namespace.</summary>
    public TimeSpan DocumentCleanupTimeout { get; set; } = RequiredDocumentCleanupTimeout;
}
