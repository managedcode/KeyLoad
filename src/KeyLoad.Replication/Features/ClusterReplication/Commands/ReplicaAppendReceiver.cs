using System.Collections.Immutable;

namespace KeyLoad.Replication;

internal sealed class ReplicaAppendReceiver(ReplicaState state)
{
    internal Task<AppendReply> ReceiveAsync(AppendRequest request, CancellationToken cancellationToken)
        => state.LockedAsync(() => Receive(request), cancellationToken);

    private AppendReply Receive(AppendRequest request)
    {
        var log = state.Log;
        request = OwnEntries(request);
        if (!state.ObserveLeader(request.Term, request.LeaderId))
        {
            return Reject(log.State.LastIndex + 1);
        }
        if (request.PreviousIndex < 0 || request.PreviousTerm < 0 || request.CommittedIndex < 0 || request.Entries.IsDefault
            || request.Entries.Length > state.Configuration.MaxAppendEntries)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var durable = log.State;
        var snapshotCut = durable.Snapshot?.Index ?? 0;
        if (request.PreviousIndex < snapshotCut)
        {
            return Reject(snapshotCut + 1);
        }
        if (request.PreviousIndex > durable.LastIndex)
        {
            return Reject(durable.LastIndex + 1);
        }
        if (log.TermAt(request.PreviousIndex) != request.PreviousTerm)
        {
            return Reject(FirstConflict(request.PreviousIndex));
        }
        ValidateEntries(request);
        log.Append(request.Entries);
        var matched = checked(request.PreviousIndex + request.Entries.Length);
        var committed = Math.Min(request.CommittedIndex, matched);
        if (committed > log.State.CommittedIndex)
        {
            state.Materializer.Commit(committed);
        }
        return new(log.State.Term, true, matched, checked(matched + 1));
    }

    private AppendRequest OwnEntries(AppendRequest request)
    {
        if (request.Entries.IsDefault || request.Entries.Length > state.Configuration.MaxAppendEntries)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
        }
        var owned = ImmutableArray.CreateBuilder<ReplicaEntry>(request.Entries.Length);
        foreach (var entry in request.Entries)
        {
            if (entry is null)
            {
                throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
            }
            owned.Add(ReplicaOperationAuthority.Own(entry, state.Materializer.Database));
        }
        return request with { Entries = owned.MoveToImmutable() };
    }

    private void ValidateEntries(AppendRequest request)
    {
        for (var position = 0; position < request.Entries.Length; position++)
        {
            var incoming = request.Entries[position];
            if (incoming.Index != checked(request.PreviousIndex + position + 1L) || incoming.Term > request.Term)
            {
                throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidAppend);
            }
            var existing = state.Log.ReadEntry(incoming.Index);
            if (existing is not null && existing.Term == incoming.Term
                && !ReplicaOperationAuthority.Equal(existing, incoming, state.Materializer.Database))
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
        }
    }

    private long FirstConflict(long index)
    {
        var cut = state.Log.State.Snapshot?.Index ?? 0;
        var term = state.Log.TermAt(index);
        while (index > cut + 1 && state.Log.TermAt(index - 1) == term)
        {
            index--;
        }
        return Math.Max(cut + 1, index);
    }

    private AppendReply Reject(long next) => new(state.Log.State.Term, false, 0, next);
}
