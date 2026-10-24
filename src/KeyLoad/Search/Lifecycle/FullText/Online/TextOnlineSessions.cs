using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal sealed class TextOnlineSessions(int maximumSessions)
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, TextOnlineSession> sessions = [];
    private readonly Dictionary<Guid, MaintenanceSessionWork> work = [];
    private bool closed;

    internal void BeginWork(Guid id, MaintenanceSessionWork original)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (maximumSessions <= 0 || work.Count >= maximumSessions || !work.TryAdd(id, original))
            {
                throw TextErrors.Busy();
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

    internal void RemoveWorkWithoutSession(Guid id, MaintenanceSessionWork original)
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

    internal async Task<TextOnlineSession> AddOwnedAsync(TextOnlineSession session)
    {
        try
        {
            Add(session);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(session.CloseCommandAdmissionAndJoinAsync, failures);
            ServerFailureObserver.Observe(session.DisposeAfterJoinedOwners, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        return session;
    }

    private void Add(TextOnlineSession session)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (!ReferenceEquals(work.GetValueOrDefault(session.Id), session.Work))
            {
                throw TextErrors.Busy();
            }
            if (!sessions.TryAdd(session.Id, session))
            {
                throw TextErrors.Mismatch();
            }
        }
    }

    internal TextOnlineSession Require(Guid id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return sessions.TryGetValue(id, out var original) ? original : throw TextErrors.Mismatch();
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

    internal TextOnlineSession[] CaptureOriginals(string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var originals = new List<TextOnlineSession>();
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

    private OnlineTextPublicationWork? TryMatchOriginal(TextOnlineSession session,
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

    private static bool MatchesOriginal(TextOnlineSession session, string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        lock (session.Gate)
        {
            if (session.PrincipalId != principalId || session.Request.CommandId != request.CommandId)
            {
                return false;
            }
            budget.ChargeBytes(OrleansSerialization.Measure(session.Request));
            budget.ChargeBytes(OrleansSerialization.Measure(request));
            if (!OrleansSerialization.Serialize(session.Request).AsSpan().SequenceEqual(OrleansSerialization.Serialize(request)))
            {
                throw TextErrors.Mismatch();
            }
            return true;
        }
    }

    internal TextOnlineSession? FindForCleanup(Guid id)
    {
        lock (gate)
        {
            return sessions.GetValueOrDefault(id);
        }
    }

    internal void RemoveAfterJoinedCleanup(TextOnlineSession original)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(original.Id, out var actual) || !ReferenceEquals(actual, original))
            {
                throw TextErrors.Ownership();
            }
            sessions.Remove(original.Id);
            work.Remove(original.Id);
        }
    }

    internal void DisposeUncapturedWorkAfterWorkerJoin()
    {
        KeyValuePair<Guid, MaintenanceSessionWork>[] pending;
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

    internal TextOnlineSession[] CloseAndCapture()
    {
        lock (gate)
        {
            closed = true;
            return sessions.Values.ToArray();
        }
    }
}
