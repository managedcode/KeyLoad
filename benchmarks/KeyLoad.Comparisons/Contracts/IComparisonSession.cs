namespace KeyLoad.Comparisons;

/// <summary>Executes workload operations within one comparison target session.</summary>
public interface IComparisonSession : IAsyncDisposable
{
    private const string OrderedCorpusReadbackIsUnavailableDetail = "Execution ordered corpus readback is unavailable.";

    /// <summary>Executes the selected workload operation for a document.</summary>
    /// <param name="scenario">The workload operation to execute.</param>
    /// <param name="document">The document used as the operation input.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task whose result contains any operation outputs.</returns>
    Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken);
    /// <summary>Reads the specified document from the target.</summary>
    /// <param name="document">The document whose identifier is read.</param>
    /// <param name="cancellationToken">A token used to cancel the read.</param>
    /// <returns>A task containing the found document or an explicit not-found result.</returns>
    Task<global::ManagedCode.Communication.Result<FoundDocument?>> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken);
    /// <summary>Reads the event associated with the specified document.</summary>
    /// <param name="document">The document identifying the event to read.</param>
    /// <param name="cancellationToken">A token used to cancel the read.</param>
    /// <returns>A task whose result is the found event, or <see langword="null"/> when it is absent.</returns>
    Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken cancellationToken) => throw new NotSupportedException();
    /// <summary>Reads all actual target corpus records once in bounded ascending identifier order.</summary>
    IAsyncEnumerable<FoundDocument> ReadCorpusAsync(CancellationToken cancellationToken) => throw new NotSupportedException(OrderedCorpusReadbackIsUnavailableDetail);
}
