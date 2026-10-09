namespace KeyLoad.Comparisons;

/// <summary>Owns one actual native client and delegates its existing operation implementation.</summary>
internal sealed class DocumentOwnedSession(IComparisonSession session, Guid clientIdentity,
    Func<CancellationToken, IAsyncEnumerable<FoundDocument>> readback, Func<ValueTask> disposeClient,
    bool singleOperationTiming = true, Func<Scenario, BenchmarkDocument, CancellationToken, Task<OperationResult>>? executeOperation = null, IDisposable? nativeClient = null) : IDocumentComparisonSession
{
    private Task? disposal;
    private readonly System.Threading.Lock gate = new();
    public Guid ClientIdentity { get; } = clientIdentity;
    public bool SingleOperationTiming => singleOperationTiming;
    public Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken token) => executeOperation is null ? session.ExecuteAsync(scenario, document, token) : executeOperation(scenario, document, token);
    public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken token) => session.ReadAsync(document, token);
    public Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken token) => session.ReadEventAsync(document, token);
    public IAsyncEnumerable<FoundDocument> ReadCorpusAsync(CancellationToken token) => session.ReadCorpusAsync(token);
    public IAsyncEnumerable<FoundDocument> ReadDocumentsAsync(CancellationToken token) => readback(token);
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            return new(disposal ??= DisposeOriginalAsync());
        }
    }
    private async Task DisposeOriginalAsync()
    {
        var sessionFailure = await OpenLoopFailure.ObserveAsync(DisposeSessionAsync()).ConfigureAwait(false);
        var clientFailure = await OpenLoopFailure.ObserveAsync(DisposeClientAsync()).ConfigureAwait(false);
        var combined = OpenLoopFailure.Combine(sessionFailure, clientFailure is null ? [] : [clientFailure]);
        if (combined is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }
    private async Task DisposeSessionAsync() => await session.DisposeAsync().ConfigureAwait(false);
    private async Task DisposeClientAsync()
    {
        try
        { await disposeClient().ConfigureAwait(false); }
        finally { nativeClient?.Dispose(); }
    }
}
