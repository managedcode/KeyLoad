using System.Runtime.ExceptionServices;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsCanonicalApplyBridge(string voter) : IReplicaTransportObservation
{
    private const int ContiguousIndexStep = 1;
    private readonly AsyncLocal<RequestCqrsCanonicalApplyScope?> current = new();
    private RequestCqrsProbeObserver? observer;
    private readonly Lock sync = new();
    private RequestCqrsCanonicalApplyScope? held;
    internal void Attach(RequestCqrsProbeObserver owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (Interlocked.CompareExchange(ref observer, owner, null) is not null)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidOptions); }
    }
    internal IDisposable? Enter(ReplicaEntry entry)
    {
        var operation = entry.Operation!;
        if (operation.Kind != OperationKind.Batch || Volatile.Read(ref observer) is not { } owner)
        { return null; }
        if (current.Value is not null)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidFiles); }
        var partition = JsonDefaults.Deserialize<CommandRequest>(operation.PayloadJson).Partition;
        RequestCqrsCanonicalApplyScope? scope = null;
        RequestCqrsCanonicalApplyScope? transferred = null;
        Exception? primary = null;
        Exception? cleanup = null;
        try
        {
            try
            {
                scope = owner.Canonical.EnterCanonical(entry, partition, voter, () => current.Value = null, SetHolding);
                if (scope is not null)
                {
                    current.Value = scope;
                    transferred = scope;
                    scope = null;
                }
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { primary = error; }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { primary = error; }
        }
        finally
        {
            try
            {
                scope?.Dispose();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { cleanup = error; }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { cleanup = error; }
        }
        ThrowTransferFailure(primary, cleanup);
        return transferred;
    }
    private static void ThrowTransferFailure(Exception? primary, Exception? cleanup)
    {
        if (primary is not null && cleanup is not null)
        { throw new AggregateException(primary, cleanup); }
        if (primary is not null)
        { ExceptionDispatchInfo.Capture(primary).Throw(); }
        if (cleanup is not null)
        { ExceptionDispatchInfo.Capture(cleanup).Throw(); }
    }
    private void SetHolding(RequestCqrsCanonicalApplyScope scope, bool holding)
    {
        lock (sync)
        {
            if (holding && held is not null || !holding && !ReferenceEquals(held, scope))
            { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidFiles); }
            held = holding ? scope : null;
        }
    }
    public Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>? BeginIncoming(ReplicaRpc method)
    {
        if (method != ReplicaRpc.Append)
        { return null; }
        RequestCqrsCanonicalApplyScope? original;
        lock (sync)
        { original = held; }
        if (original is null)
        { return null; }
        return (request, reply) => CompleteIncoming(original, request, reply);
    }
    private void CompleteIncoming(RequestCqrsCanonicalApplyScope original, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> bytes)
    {
        var request = ReplicaProtocolCodec.Deserialize<AppendRequest>(payload.Span);
        if (!original.Matches(request))
        { return; }
        var reply = ReplicaProtocolCodec.Deserialize<AppendReply>(bytes.Span);
        if (!reply.Accepted || reply.Term != request.Term || reply.MatchedIndex != request.PreviousIndex
            || reply.NextIndex != checked(request.PreviousIndex + ContiguousIndexStep))
        { return; }
        lock (sync)
        {
            if (ReferenceEquals(held, original))
            { original.IndependentAppendCompleted(); }
        }
    }
    internal Action<CommitStage, long, int> StorageObserver => (stage, _, _) => ObserveStorage(stage);
    private void ObserveStorage(CommitStage stage)
    {
        if (stage == CommitStage.JournalFlushed)
        { current.Value?.JournalFlushed(); }
    }
}
