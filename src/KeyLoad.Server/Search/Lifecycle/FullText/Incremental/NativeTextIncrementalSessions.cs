namespace KeyLoad.Server.Features.Search;

/// <summary>Owns exact active session identities and preserves failed native disposals.</summary>
internal sealed class NativeTextIncrementalSessions
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, NativeTextIncrementalSession> sessions = [];
    private readonly Dictionary<Guid, NativeMaintenanceSessionWork> work = [];
    private readonly int maximumSessions;
    private bool closed;

    internal NativeTextIncrementalSessions(int maximumSessions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSessions);
        this.maximumSessions = maximumSessions;
    }

    internal void BeginWork(Guid id, NativeMaintenanceSessionWork original)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (work.Count >= maximumSessions || !work.TryAdd(id, original))
            {
                throw NativeTextErrors.BoundExceeded();
            }
        }
    }

    internal void Own(NativeTextIncrementalSession actual)
    {
        lock (gate)
        {
            if (!ReferenceEquals(work.GetValueOrDefault(actual.Id), actual.Work) || !sessions.TryAdd(actual.Id, actual))
            {
                throw NativeTextErrors.Ownership();
            }
        }
    }

    internal NativeMaintenanceSessionWork? FindWork(Guid id)
    {
        lock (gate)
        {
            return work.GetValueOrDefault(id);
        }
    }

    internal void Add(NativeTextIncrementalSession actual)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (actual.GenerationLeaf is null)
            {
                throw NativeTextErrors.Mismatch();
            }
            if (sessions.Count > maximumSessions || sessions.Values.Any(current => !ReferenceEquals(current, actual) &&
                string.Equals(current.GenerationLeaf, actual.GenerationLeaf, StringComparison.Ordinal)))
            {
                throw NativeTextErrors.BoundExceeded();
            }
            if (!sessions.TryGetValue(actual.Id, out var owned) || !ReferenceEquals(owned, actual))
            {
                throw NativeTextErrors.Mismatch();
            }
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
        NativeTextIncrementalSession? actual;
        NativeMaintenanceSessionWork? original;
        lock (gate)
        {
            actual = sessions.GetValueOrDefault(id);
            original = work.GetValueOrDefault(id);
        }
        if (actual is not null)
        {
            NativeTextIncrementalSessionLifetime.DisposeNative(actual);
        }
        else
        {
            original?.DisposeAfterStage();
        }
        lock (gate)
        {
            sessions.Remove(id);
            work.Remove(id);
        }
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
        {
            ServerFailureObserver.Observe(() => Retire(id), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void CloseAdmission()
    {
        lock (gate)
        {
            closed = true;
        }
    }

    internal void DisposeAfterWorkerJoin()
    {
        Guid[] originals;
        lock (gate)
        {
            originals = work.Keys.ToArray();
        }
        var failures = new List<Exception>();
        foreach (var id in originals)
        {
            ServerFailureObserver.Observe(() => Retire(id), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
