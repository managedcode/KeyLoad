using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Executes only the claimed original node-local work through the existing native owner endpoint.</summary>
internal sealed class NativeCommandObservationTarget(DatabaseEngine database, Func<ICommitCoordinator> borrowCoordinator,
    TimeProvider clock, IOptions<GrainRoutingOptions> options, NativeRequestWorkOwner owner,
    IPhysicalRequestPlacement physical, ReplicaSiloDiscoveryState runtime, GrainRequestCodec codec)
{
    internal Task<T> ExecuteAsync<T>(string opaque, Func<CancellationToken, Task> admitPeer,
        Func<GrainOperationReply, ReadExecutionBudget, T> publish, Action<T> clearOnFailure,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var handle = NativeCommandObservationHandleCodec.Verify(database, opaque);
        var coordinator = borrowCoordinator();
        var publication = new NativeCommandObservationPublication<T>(publish, clearOnFailure);
        var original = owner.BeginCommandExecution(handle, physical.Owner, runtime.Read(), database);
        return NativeCommandObservationCommandExecution.ExecuteAsync(database, coordinator, clock,
            options.Value, codec, physical, runtime, original, admitPeer, publication, token);
    }

    internal Task<T> ReadAsync<T>(string opaque, Func<CancellationToken, Task> admitPeer,
        Func<GrainOperationReply, ReadExecutionBudget, T> publish, Action<T> clearOnFailure,
        CancellationToken token)
        => NativeCommandObservationRead.ExecuteAsync(database, borrowCoordinator(), owner, physical.Owner,
            runtime.Read(), opaque, options, admitPeer, publish, clearOnFailure, token);

    internal Task AbortAsync(string opaque, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var handle = NativeCommandObservationHandleCodec.Verify(database, opaque);
        return owner.AbortCommandObservationByHandleAsync(handle, physical.Owner, runtime.Read(), database);
    }
}
