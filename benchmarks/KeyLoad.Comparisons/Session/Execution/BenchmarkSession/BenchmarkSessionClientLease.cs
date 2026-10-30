using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionClientLease(BenchmarkSessionContext context)
{
    private readonly List<IComparisonSession> sessions = [];
    internal (bool HasValue, IComparisonSession? Value) At(int worker) => sessions.Count > BenchmarkSessionLimits.NoObservedItems ? (true, sessions[worker]) : (false, default(IComparisonSession?));
    internal async Task OpenAsync(int clients, bool documentSessions, bool nativeSessions, CancellationToken token)
    {
        for (var index = BenchmarkSessionLimits.NoObservedItems; (documentSessions || nativeSessions) && index < clients; index++)
        {
            var session = documentSessions
                ? await ((IDocumentComparisonTarget)context.Target).OpenDocumentSessionAsync(token).ConfigureAwait(false)
                : await context.Target.OpenSessionAsync(token).ConfigureAwait(false);
            sessions.Add(session);
            if (session is IDocumentComparisonSession documents && !documents.SingleOperationTiming)
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
