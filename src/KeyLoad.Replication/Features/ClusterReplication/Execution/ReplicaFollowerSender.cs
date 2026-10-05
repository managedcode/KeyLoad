namespace KeyLoad.Replication;

internal sealed class ReplicaFollowerSender(ReplicaState state, ReplicaRpcClient rpc, int maximumSnapshotChunksPerRound) : IDisposable
{
    private const int ExclusiveSynchronizationPermit = 1;
    private const int ContiguousIndexStep = 1;
    private const int EmptyEntryCount = 0;
    private const int FirstLogPosition = 1;
    private const int FirstRoundChunk = 0;
    private const int BeforeFirstTransferByte = 0;

    private const int SynchronizationAdmissionWaitMilliseconds = 0;
    private readonly Dictionary<string, SemaphoreSlim> gates = state.Configuration.VoterIds.ToDictionary(
        voter => voter, _ => new SemaphoreSlim(ExclusiveSynchronizationPermit, ExclusiveSynchronizationPermit), StringComparer.Ordinal);

    internal async Task<bool> SynchronizeAsync(string voter, long term, ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
    {
        if (!await gates[voter].WaitAsync(SynchronizationAdmissionWaitMilliseconds, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        try
        {
            var plan = await state.LockedAsync(() => Plan(voter, term), cancellationToken).ConfigureAwait(false);
            return plan.Snapshot is { } snapshot
                ? await SnapshotAsync(voter, term, snapshot, cancellationToken).ConfigureAwait(false)
                : await AppendAsync(voter, term, plan.Append!, purpose, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (ReplicaRpcClient.Unavailable(error)) { return false; }
        catch (Exception error) when (error is KeyLoadException or IOException)
        { state.Poison(error); return false; }
        finally { gates[voter].Release(); }
    }

    private (AppendRequest? Append, ReplicaSnapshot? Snapshot) Plan(string voter, long term)
    {
        state.RequireLeader(term);
        var durable = state.Log.State;
        var next = state.Progress[voter].NextIndex;
        if (durable.Snapshot is { } snapshot && next <= snapshot.Index)
        {
            return (null, snapshot);
        }
        next = Math.Min(next, checked(durable.LastIndex + ContiguousIndexStep));
        var entries = state.Log.Read(next, state.Configuration.MaxAppendEntries, state.Configuration.MaxAppendBytes);
        return (new(state.Configuration.LocalId, term, next - ContiguousIndexStep, state.Log.TermAt(next - ContiguousIndexStep), durable.CommittedIndex, entries), null);
    }

    private async Task<bool> AppendAsync(string voter, long term, AppendRequest request, ReplicaReadRoundPurpose purpose,
        CancellationToken cancellationToken)
    {
        AppendReply reply;
        try
        {
            reply = await rpc.InvokeAsync<AppendRequest, AppendReply>(voter, AppendMethod(request, purpose), request, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted && request.Entries.Length > EmptyEntryCount)
        {
            // A read-generated fallback remains in application admission; native control retains its reserve.
            request = request with { Entries = [] };
            reply = await rpc.InvokeAsync<AppendRequest, AppendReply>(voter, AppendMethod(request, purpose), request, cancellationToken).ConfigureAwait(false);
        }

        return await state.LockedAsync(() =>
        {
            state.ObserveTerm(reply.Term);
            if (state.Role != ReplicaRole.Leader || state.Log.State.Term != term || reply.Term != term)
            {
                return false;
            }
            var through = checked(request.PreviousIndex + request.Entries.Length);
            if (reply.Accepted && (reply.MatchedIndex != through || reply.NextIndex != through + ContiguousIndexStep))
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer);
            }
            var old = state.Progress[voter];
            state.Progress[voter] = reply.Accepted ? new(reply.NextIndex, Math.Max(old.MatchedIndex, through))
                : new(Math.Clamp(reply.NextIndex, FirstLogPosition, state.Log.State.LastIndex + ContiguousIndexStep), old.MatchedIndex);
            return reply.Accepted;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ReplicaRpc AppendMethod(AppendRequest request, ReplicaReadRoundPurpose purpose)
        => purpose == ReplicaReadRoundPurpose.Application && request.Entries.Length == EmptyEntryCount ? ReplicaRpc.ReadProbe : ReplicaRpc.Append;

    private async Task<bool> SnapshotAsync(string voter, long term, ReplicaSnapshot snapshot, CancellationToken cancellationToken)
    {
        var reply = await rpc.InvokeAsync<SnapshotBeginRequest, SnapshotReply>(voter, ReplicaRpc.SnapshotBegin,
            new(state.Configuration.LocalId, term, snapshot), cancellationToken).ConfigureAwait(false);
        if (!await ObserveSnapshotAsync(voter, term, snapshot, reply, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        var offset = reply.Offset;
        for (var chunk = FirstRoundChunk; !reply.Installed && offset < snapshot.Length && chunk < maximumSnapshotChunksPerRound; chunk++)
        {
            var bytes = state.Materializer.Snapshots.ReadChunk(snapshot.TransferId, offset, state.Configuration.SnapshotChunkBytes);
            reply = await rpc.InvokeAsync<SnapshotChunkRequest, SnapshotReply>(voter, ReplicaRpc.SnapshotChunk,
                new(state.Configuration.LocalId, term, snapshot.TransferId, offset, bytes), cancellationToken).ConfigureAwait(false);
            if (!await ObserveSnapshotAsync(voter, term, snapshot, reply, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            if (reply.Offset != offset + bytes.LongLength)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot);
            }
            offset = reply.Offset;
        }
        if (!reply.Installed && offset == snapshot.Length)
        {
            reply = await rpc.InvokeAsync<SnapshotCompleteRequest, SnapshotReply>(voter, ReplicaRpc.SnapshotComplete,
                new(state.Configuration.LocalId, term, snapshot.TransferId), cancellationToken).ConfigureAwait(false);
            return await ObserveSnapshotAsync(voter, term, snapshot, reply, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    private Task<bool> ObserveSnapshotAsync(string voter, long term, ReplicaSnapshot snapshot, SnapshotReply reply,
        CancellationToken cancellationToken) => state.LockedAsync(() =>
    {
        state.ObserveTerm(reply.Term);
        if (state.Role != ReplicaRole.Leader || state.Log.State.Term != term || reply.Term != term)
        {
            return false;
        }
        if (reply.Offset < BeforeFirstTransferByte || reply.Offset > snapshot.Length || reply.Installed && (reply.Index != snapshot.Index || reply.Offset != snapshot.Length))
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot);
        }
        if (reply.Installed)
        {
            var old = state.Progress[voter];
            state.Progress[voter] = new(snapshot.Index + ContiguousIndexStep, Math.Max(old.MatchedIndex, snapshot.Index));
        }
        return true;
    }, cancellationToken);

    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        foreach (var gate in gates.Values)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var gate in gates.Values)
        {
            gate.Dispose();
        }
    }
}
