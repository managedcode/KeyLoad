using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineSessions(int maximumSessions)
{
    private const int EmptySessionCapacity = 0;
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, NativeTextOnlineSession> sessions = [];
    private bool closed;

    internal void Add(NativeTextOnlineSession session)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (maximumSessions <= EmptySessionCapacity || sessions.Count >= maximumSessions)
            { throw NativeTextErrors.Busy(); }
            if (!sessions.TryAdd(session.Id, session))
            { throw NativeTextErrors.Mismatch(); }
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
                { return matched; }
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
                { originals.Add(session); }
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
            { ObjectDisposedException.ThrowIf(closed, this); }
            return matched;
        }
    }

    private static bool MatchesOriginal(NativeTextOnlineSession session, string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        lock (session.Gate)
        {
            if (session.PrincipalId != principalId || session.Request.CommandId != request.CommandId)
            { return false; }
            budget.ChargeBytes(NativeSerialization.Measure(session.Request));
            budget.ChargeBytes(NativeSerialization.Measure(request));
            if (!NativeSerialization.Serialize(session.Request).AsSpan().SequenceEqual(NativeSerialization.Serialize(request)))
            { throw NativeTextErrors.Mismatch(); }
            return true;
        }
    }

    internal NativeTextOnlineSession? FindForCleanup(Guid id)
    {
        lock (gate)
        { return sessions.GetValueOrDefault(id); }
    }

    internal void RemoveAfterJoinedCleanup(NativeTextOnlineSession original)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(original.Id, out var actual) || !ReferenceEquals(actual, original))
            { throw NativeTextErrors.Ownership(); }
            sessions.Remove(original.Id);
        }
    }

    internal NativeTextOnlineSession[] CloseAndCapture()
    {
        lock (gate)
        { closed = true; return sessions.Values.ToArray(); }
    }
}
