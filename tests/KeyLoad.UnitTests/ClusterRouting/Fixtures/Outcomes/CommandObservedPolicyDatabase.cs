using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.RecoveryHost.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.ClusterRouting.CommandObservedPolicyProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exposes the same real source host, journal and coordinator without constructing another storage owner.</summary>
internal sealed class CommandObservedPolicyDatabase(Func<ControlledPartitionMovementNativeHost> source)
{
    internal DatabaseEngine Database => source().Database;
    internal ZoneTreeStore Store => (ZoneTreeStore)source().Store;
    internal PartitionRef Partition { get; } = P.Partition;
    internal ICommitCoordinator Coordinator => source().Partition.Coordinator;

    internal (long LastIndex, long CommittedIndex, long Term) ReadCanonicalCut()
    {
        var state = source().Log.State;
        return (state.LastIndex, state.CommittedIndex, state.Term);
    }

    internal Task<GrainOperationReply> SubmitAsync<T>(OperationKind kind, T payload,
        CancellationToken token, string principal = P.Root, Guid? id = null)
        => source().SubmitAsync(kind, id ?? Guid.NewGuid(), principal,
            NativeSerialization.Serialize(payload), token);
}
