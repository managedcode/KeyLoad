namespace KeyLoad.Server.Features.Search;

/// <summary>Owns exact active session identities and preserves failed native disposals.</summary>
internal sealed class NativeTextIncrementalSessions
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, NativeTextIncrementalSession> sessions = [];
    private readonly int maximumSessions;
    private bool closed;

    internal NativeTextIncrementalSessions(int maximumSessions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSessions);
        this.maximumSessions = maximumSessions;
    }

    internal void Add(NativeTextIncrementalSession actual)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (actual.GenerationLeaf is null)
            { throw NativeTextErrors.Mismatch(); }
            if (sessions.Count >= maximumSessions || sessions.Values.Any(current =>
                string.Equals(current.GenerationLeaf, actual.GenerationLeaf, StringComparison.Ordinal)))
            { throw NativeTextErrors.BoundExceeded(); }
            if (!sessions.TryAdd(actual.Id, actual))
            { throw NativeTextErrors.Mismatch(); }
        }
    }

    internal NativeTextIncrementalSession Require(Guid id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return sessions.TryGetValue(id, out var actual) ? actual : throw NativeTextErrors.Mismatch();
        }
    }

    internal void Retire(Guid id)
    {
        NativeTextIncrementalSession actual;
        lock (gate)
        {
            if (!sessions.TryGetValue(id, out var found))
            { return; }
            actual = found;
        }
        NativeTextIncrementalSessionLifetime.DisposeNative(actual);
        lock (gate)
        { sessions.Remove(id); }
    }

    internal void RetireGeneration(string leaf)
    {
        Guid[] originals;
        lock (gate)
        {
            originals = sessions.Values.Where(session => string.Equals(session.GenerationLeaf, leaf,
                StringComparison.Ordinal)).Select(session => session.Id).ToArray();
        }
        var failures = new List<Exception>();
        foreach (var id in originals)
        { ServerFailureObserver.Observe(() => Retire(id), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void CloseAdmission()
    {
        lock (gate)
        { closed = true; }
    }

    internal void DisposeAfterWorkerJoin()
    {
        NativeTextIncrementalSession[] originals;
        lock (gate)
        { originals = sessions.Values.ToArray(); }
        var failures = new List<Exception>();
        foreach (var actual in originals)
        {
            ServerFailureObserver.Observe(() => Retire(actual.Id), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
