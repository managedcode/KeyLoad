namespace KeyLoad.Comparisons;

/// <summary>Owns native client acquisition until a session takes ownership, preserving original and cleanup failures.</summary>
internal sealed class DocumentNativeSessionAcquisition<TClient> : IAsyncDisposable where TClient : class, IAsyncDisposable
{
    private TClient? client;
    private IComparisonSession? session;
    private Exception? primaryFailure;
    internal static async Task<IDocumentComparisonSession> OpenAsync(Func<CancellationToken, Task<TClient>> connect,
        Func<TClient, CancellationToken, Task> admit, Func<TClient, IComparisonSession> createSession,
        CancellationToken token, Func<TClient, CancellationToken, IAsyncEnumerable<FoundDocument>>? readback = null)
    {
        await using var owner = new DocumentNativeSessionAcquisition<TClient>();
        try
        {
            owner.client = await connect(token).ConfigureAwait(false);
            await admit(owner.client, token).ConfigureAwait(false);
            var nativeClient = owner.client;
            owner.session = createSession(nativeClient);
            owner.client = null; // The native session now owns the client.
            var result = new DocumentOwnedSession(owner.session, Guid.NewGuid(), readback is null
                ? owner.session.ReadCorpusAsync : cancellation => readback(nativeClient, cancellation), () => ValueTask.CompletedTask);
            owner.session = null; // The document session now owns the native session.
            return result;
        }
        catch (Exception error)
        {
            owner.primaryFailure = error;
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        var sessionFailure = await OpenLoopFailure.ObserveAsync(DisposeSessionAsync()).ConfigureAwait(false);
        var clientFailure = await OpenLoopFailure.ObserveAsync(DisposeClientAsync()).ConfigureAwait(false);
        var cleanup = new List<Exception>();
        if (sessionFailure is not null)
        {
            cleanup.Add(sessionFailure);
        }

        if (clientFailure is not null)
        {
            cleanup.Add(clientFailure);
        }

        var combined = OpenLoopFailure.Combine(primaryFailure, [.. cleanup]);
        if (cleanup.Count != DocumentMeasurementValues.NoObservedItems && combined is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }
    private async Task DisposeSessionAsync()
    {
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
    private async Task DisposeClientAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
