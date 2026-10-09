using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal sealed class DocumentHttpSessionAcquisition : IAsyncDisposable
{
    private DocumentHttpClient? connection;
    private IComparisonSession? session;
    private Exception? primary;
    internal static async Task<IDocumentComparisonSession> OpenAsync(HttpClient source,
        IOptions<NativeComparisonExecutionOptions> options, Func<HttpClient, CancellationToken, Task> admit,
        Func<HttpClient, IComparisonSession> createSession, CancellationToken token,
        Func<HttpClient, IComparisonSession, Scenario, BenchmarkDocument, CancellationToken, Task<OperationResult>>? execute = null,
        Func<HttpClient, IComparisonSession, CancellationToken, IAsyncEnumerable<FoundDocument>>? readback = null)
    {
        await using var owner = new DocumentHttpSessionAcquisition();
        try
        {
            return await owner.AcquireAsync(source, options, admit, createSession, execute, readback, token).ConfigureAwait(false);
        }
        catch (Exception failure) { owner.primary = failure; throw; }
    }
    private async Task<IDocumentComparisonSession> AcquireAsync(HttpClient source,
        IOptions<NativeComparisonExecutionOptions> options, Func<HttpClient, CancellationToken, Task> admit,
        Func<HttpClient, IComparisonSession> createSession,
        Func<HttpClient, IComparisonSession, Scenario, BenchmarkDocument, CancellationToken, Task<OperationResult>>? execute,
        Func<HttpClient, IComparisonSession, CancellationToken, IAsyncEnumerable<FoundDocument>>? readback, CancellationToken token)
    {
        connection = new(source, options);
        await admit(connection.Client, token).ConfigureAwait(false);
        session = createSession(connection.Client);
        var transferredClient = connection.Client;
        var transferredSession = session;
        var result = new DocumentOwnedSession(session, Guid.NewGuid(), readback is null ? session.ReadCorpusAsync :
            cancellation => readback(transferredClient, transferredSession, cancellation), () => ValueTask.CompletedTask,
            executeOperation: execute is null ? null : (operation, document, cancellation) =>
                execute(transferredClient, transferredSession, operation, document, cancellation), nativeClient: connection);
        session = null;
        connection = null;
        return result;
    }
    public async ValueTask DisposeAsync()
    {
        var cleanup = new List<Exception>();
        if (session is not null && await OpenLoopFailure.ObserveAsync(DisposeSessionAsync()).ConfigureAwait(false) is { } sessionFailure)
        {
            cleanup.Add(sessionFailure);
        }

        if (connection is not null && await OpenLoopFailure.ObserveAsync(DisposeConnectionAsync()).ConfigureAwait(false) is { } clientFailure)
        {
            cleanup.Add(clientFailure);
        }

        var combined = OpenLoopFailure.Combine(primary, [.. cleanup]);
        if (cleanup.Count != DocumentMeasurementValues.NoObservedItems && combined is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }
    private async Task DisposeSessionAsync() => await session!.DisposeAsync().ConfigureAwait(false);
    private async Task DisposeConnectionAsync() { await Task.CompletedTask.ConfigureAwait(false); connection!.Dispose(); }
}
