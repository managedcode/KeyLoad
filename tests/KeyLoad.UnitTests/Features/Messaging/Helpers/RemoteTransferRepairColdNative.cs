using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairColdNative
{
    internal static PrincipalRecord Principal(RemoteTransferDatabase fixture)
        => fixture.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(RemoteTransferRepairColdProtocol.Subject)))
            ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);

    internal static void Policy(RemoteTransferDatabase fixture, QueueLaneRef denied, bool allow)
    {
        var current = Principal(fixture);
        var grants = new ScopeGrant[]
        {
            new(fixture.SourcePartition.DatabaseId, fixture.SourceQueue.Queue,
                denied == fixture.SourceQueue && !allow ? Capability.QueueInspect : Capability.All),
            new(fixture.DestinationPartition.DatabaseId, fixture.DestinationQueue.Queue,
                denied == fixture.DestinationQueue && !allow ? Capability.QueueInspect : Capability.All)
        };
        fixture.AddPrincipal(current with { Grants = [.. grants], PolicyEpoch = checked(current.PolicyEpoch + RemoteTransferRepairColdProtocol.FirstGeneration) });
    }

    internal static byte[] Outcome(RemoteTransferDatabase fixture, Guid command, PartitionRef partition)
        => fixture.Store.Read(view =>
        {
            var selected = CommandOutcomeKeyResolver.Select(view, RemoteTransferRepairColdProtocol.Subject,
                command, new(CommandOutcomeScopeKind.Partition, partition));
            return view.ReadOwnedValue(selected.Key) ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);
        });

    internal static RemoteTransferCoordinationHint Hint(RemoteTransferDatabase fixture)
        => RemoteTransferPendingDiscovery.Read(fixture.Database, RemoteTransferRepairColdProtocol.Subject,
            null, CancellationToken.None).Hint ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);

    internal static OperationResult Apply(RemoteTransferDatabase fixture, CommandRequest request)
        => fixture.Apply(OperationKind.Batch, request, RemoteTransferRepairColdProtocol.Subject);

    internal static RemoteTransferRepairState State(RemoteTransferDatabase fixture, Guid transfer)
        => fixture.Store.Read(view => view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(fixture.SourceQueue, transfer)))?.Repairs
            ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);

    internal static RemoteTransferCoordinationReadRequest Request(RemoteTransferCoordinationHint hint,
        string intent, QueueTransferRepairStage stage, string? receipt)
        => new(RemoteTransferRepairProtocol.ReadPurpose, hint.Source, hint.Destination, hint.TransferId, intent,
            hint.AcceptGeneration, RemoteTransferRepairIdentity.CommandId(hint, stage), stage,
            hint.AcceptPolicyGeneration, hint.CompleteGeneration, receipt);

    internal static RemoteTransferCoordinationReadResult Read(RemoteTransferDatabase fixture, RemoteTransferCoordinationReadRequest request)
        => fixture.Database.ReadRemoteTransferCoordination(RemoteTransferRepairColdProtocol.Subject, request, CancellationToken.None);
}
