using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineSessions(int maximumSessions)
{
    private const int EmptySessionCapacity = 0;
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, NativeTextOnlineSession> sessions = [];
    private readonly Dictionary<Guid, NativeMaintenanceSessionWork> work = [];
    private bool closed;

    internal void BeginWork(Guid id, NativeMaintenanceSessionWork original)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (maximumSessions <= EmptySessionCapacity || work.Count >= maximumSessions || !work.TryAdd(id, original))
            {
                throw NativeTextErrors.Busy();
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

    internal void RemoveWorkWithoutSession(Guid id, NativeMaintenanceSessionWork original)
    {
        lock (gate)
        {
            if (!sessions.ContainsKey(id) && ReferenceEquals(work.GetValueOrDefault(id), original))
            {
                work.Remove(id);
            }
        }
    }

    internal void RetireWork(Guid id)
    {
        lock (gate)
        {
            if (work.TryGetValue(id, out var original) && !sessions.ContainsKey(id))
            {
                original.DisposeAfterStage();
                work.Remove(id);
            }
        }
    }

    internal async Task<NativeTextOnlineSession> AddOwnedAsync(NativeTextOnlineSession session)
    {
        try
        {
            Add(session);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(session.CloseCommandAdmissionAndJoinAsync, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(session.DisposeAfterJoinedNativeOwners, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        return session;
    }

    private void Add(NativeTextOnlineSession session)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (!ReferenceEquals(work.GetValueOrDefault(session.Id), session.Work))
            {
                throw NativeTextErrors.Busy();
            }
            if (!sessions.TryAdd(session.Id, session))
            {
                throw NativeTextErrors.Mismatch();
            }
        }
    }

    internal NativeTextOnlineSession Require(Guid id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return sessions.TryGetValue(id, out var original) ? original : throw NativeTextErrors.Mismatch();
        }
    }

    internal OnlineTextPublicationWork? TryMatchCheckpoint(CommitProjectionBatchRequest command,
        string principalId, DateTimeOffset originalExpiry)
    {
        lock (gate)
        {
            foreach (var session in sessions.Values)
            {
                var matched = TryMatchOriginal(session, command, principalId, originalExpiry);
                if (matched is not null)
                {
                    return matched;
                }
            }
            return null;
        }
    }

    internal NativeTextOnlineSession[] CaptureOriginals(string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var originals = new List<NativeTextOnlineSession>();
            foreach (var session in sessions.Values)
            {
                if (MatchesOriginal(session, principalId, request, budget))
                {
                    originals.Add(session);
                }
            }
            budget.Check();
            return originals.ToArray();
        }
    }

    private OnlineTextPublicationWork? TryMatchOriginal(NativeTextOnlineSession session,
        CommitProjectionBatchRequest command, string principalId, DateTimeOffset originalExpiry)
    {
        lock (session.Gate)
        {
            var matched = session.TryMatchCheckpoint(command, principalId, originalExpiry);
            if (matched is not null)
            {
                ObjectDisposedException.ThrowIf(closed, this);
            }
            return matched;
        }
    }

    private static bool MatchesOriginal(NativeTextOnlineSession session, string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        lock (session.Gate)
        {
            if (session.PrincipalId != principalId || session.Request.CommandId != request.CommandId)
            {
                return false;
            }
            budget.ChargeBytes(NativeSerialization.Measure(session.Request));
            budget.ChargeBytes(NativeSerialization.Measure(request));
            if (!NativeSerialization.Serialize(session.Request).AsSpan().SequenceEqual(NativeSerialization.Serialize(request)))
            {
                throw NativeTextErrors.Mismatch();
            }
            return true;
        }
    }

    internal NativeTextOnlineSession? FindForCleanup(Guid id)
    {
        lock (gate)
        {
            return sessions.GetValueOrDefault(id);
        }
    }

    internal void RemoveAfterJoinedCleanup(NativeTextOnlineSession original)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(original.Id, out var actual) || !ReferenceEquals(actual, original))
            {
                throw NativeTextErrors.Ownership();
            }
            sessions.Remove(original.Id);
            work.Remove(original.Id);
        }
    }

    internal void DisposeUncapturedWorkAfterWorkerJoin()
    {
        KeyValuePair<Guid, NativeMaintenanceSessionWork>[] pending;
        lock (gate)
        {
            pending = work.Where(pair => !sessions.ContainsKey(pair.Key)).ToArray();
        }
        var failures = new List<Exception>();
        foreach (var original in pending)
        {
            ServerFailureObserver.Observe(() => RetireWork(original.Key), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal NativeTextOnlineSession[] CloseAndCapture()
    {
        lock (gate)
        {
            closed = true;
            return sessions.Values.ToArray();
        }
    }
}
