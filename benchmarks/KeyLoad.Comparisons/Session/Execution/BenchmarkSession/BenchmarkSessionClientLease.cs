using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionClientLease(BenchmarkSessionContext context)
{
    private readonly List<IDocumentComparisonSession> sessions = [];
    internal (bool HasValue, IComparisonSession? Value) At(int worker, bool documentSessions) => documentSessions ? (true, ((IComparisonSession)(sessions[worker]))) : (false, default(IComparisonSession?));
    internal async Task OpenAsync(int clients, bool documentSessions, CancellationToken token)
    {
        for (var index = BenchmarkSessionLimits.NoObservedItems; documentSessions && index < clients; index++)
        {
            var session = await ((IDocumentComparisonTarget)context.Target).OpenDocumentSessionAsync(token).ConfigureAwait(false);
            sessions.Add(session);
            if (!session.SingleOperationTiming)
            {
                throw new InvalidOperationException(BenchmarkSessionProtocol.SessionRequiresSingleOperationTiming);
            }
        }
    }
    internal async Task<ImmutableArray<Exception>> CloseAsync()
    {
        var failures = ImmutableArray.CreateBuilder<Exception>();
        foreach (var session in sessions)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
            {
                failures.Add(error);
            }
        }
        return failures.ToImmutable();
    }
}
