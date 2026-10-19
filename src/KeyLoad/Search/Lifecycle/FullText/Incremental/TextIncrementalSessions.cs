namespace KeyLoad.Server.Features.Search;

/// <summary>Owns exact active session identities and preserves failed execution disposals.</summary>
internal sealed class TextIncrementalSessions
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, TextIncrementalSession> sessions = [];
    private readonly Dictionary<Guid, MaintenanceSessionWork> work = [];
    private readonly int maximumSessions;
    private bool closed;

    internal TextIncrementalSessions(int maximumSessions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSessions);
        this.maximumSessions = maximumSessions;
    }

    internal void BeginWork(Guid id, MaintenanceSessionWork original)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (work.Count >= maximumSessions || !work.TryAdd(id, original))
            {
                throw TextErrors.BoundExceeded();
            }
        }
    }

    internal void Own(TextIncrementalSession actual)
    {
        lock (gate)
        {
            if (!ReferenceEquals(work.GetValueOrDefault(actual.Id), actual.Work) || !sessions.TryAdd(actual.Id, actual))
            {
                throw TextErrors.Ownership();
            }
        }
    }

    internal MaintenanceSessionWork? FindWork(Guid id)
    {
        lock (gate)
        {
            return work.GetValueOrDefault(id);
        }
    }

    internal void Add(TextIncrementalSession actual)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (actual.GenerationLeaf is null)
            {
                throw TextErrors.Mismatch();
            }
            if (sessions.Count > maximumSessions || sessions.Values.Any(current => !ReferenceEquals(current, actual) &&
                string.Equals(current.GenerationLeaf, actual.GenerationLeaf, StringComparison.Ordinal)))
            {
                throw TextErrors.BoundExceeded();
            }
            if (!sessions.TryGetValue(actual.Id, out var owned) || !ReferenceEquals(owned, actual))
            {
                throw TextErrors.Mismatch();
            }
        }
    }

    internal TextIncrementalSession Require(Guid id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return sessions.TryGetValue(id, out var actual) ? actual : throw TextErrors.Mismatch();
        }
    }

    internal void Retire(Guid id)
    {
        TextIncrementalSession? actual;
        MaintenanceSessionWork? original;
        lock (gate)
        {
            actual = sessions.GetValueOrDefault(id);
            original = work.GetValueOrDefault(id);
        }
        if (actual is not null)
        {
            TextIncrementalSessionLifetime.RuntimeDispose(actual);
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
