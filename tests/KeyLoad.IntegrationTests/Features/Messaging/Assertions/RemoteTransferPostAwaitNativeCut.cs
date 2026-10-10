using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Storage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record RemoteTransferPostAwaitNativeCut(string Outcome, string Receipt, string Capacity,
    CommitReceipt OriginalReceipt)
{
    private const long OriginalRecords = 1;

    internal static RemoteTransferPostAwaitNativeCut Read(PartitionMovementLateNativeOwners owners,
        RemoteTransferColdSeed seed, CommandRequest accept)
        => owners.Nodes[PartitionMovementLateNativeSettings.GroupSize].Partition.Database.Store.Read(view =>
        {
            var outcome = CommandOutcomeKeyResolver.Select(view, RemoteTransferDistinctProtocol.TechnicalSubject,
                accept.CommandId, new(CommandOutcomeScopeKind.Partition, seed.Scenario.DestinationPartition)).Outcome
                ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
            var receipt = view.GetRecord<RemoteTransferTargetReceiptRecord>(RemoteTransferStorage.TargetReceiptKey(
                seed.Scenario.SourceQueue, seed.TransferId, seed.Scenario.DestinationQueue))
                ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
            var capacity = RemoteTransferStorage.RequireTargetCounter(view, seed.Scenario.DestinationQueue);
            if (capacity.StoredRecords != OriginalRecords
                || capacity.StoredBytes != NativeSerialization.Serialize(receipt).LongLength)
            { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
            if (outcome.Result.Error is not null || outcome.RemoteTransferAuthority is not { } stamp
                || stamp.LogicalPrincipalId != seed.Identity.Principal.Id
                || stamp.LogicalPolicyEpoch != seed.Identity.Principal.PolicyEpoch
                || stamp.TechnicalPrincipalId != RemoteTransferDistinctProtocol.TechnicalSubject
                || stamp.TechnicalPolicyEpoch != outcome.PolicyEpoch
                || stamp.DestinationOwner.Owner.PhysicalShardId != owners.Settings.Destination
                || stamp.SourceOwner.Owner.PhysicalShardId != owners.Settings.Source
                || stamp.DestinationOwner.Owner.Incarnation != owners.Nodes[PartitionMovementLateNativeSettings.GroupSize].Partition.Configuration.Incarnation
                || outcome.Incarnation != owners.Nodes[PartitionMovementLateNativeSettings.GroupSize].Partition.Configuration.Incarnation
                || receipt.Source != seed.Scenario.SourceQueue || receipt.Destination != seed.Scenario.DestinationQueue
                || receipt.TransferId != seed.TransferId || receipt.RemoteOrigin?.LogicalPrincipalId != seed.Identity.Principal.Id)
            { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
            return new RemoteTransferPostAwaitNativeCut(Convert.ToHexString(NativeSerialization.Serialize(outcome)),
                Convert.ToHexString(NativeSerialization.Serialize(receipt)),
                Convert.ToHexString(NativeSerialization.Serialize(capacity)), outcome.Result.Get<CommitReceipt>());
        });

    internal static string ReadSource(PartitionMovementLateNativeOwners owners, RemoteTransferColdSeed seed,
        CommandRequest accept)
        => owners.Nodes[0].Partition.Database.Store.Read(view =>
        {
            if (CommandOutcomeKeyResolver.Select(view, seed.Identity.Principal.Id, accept.CommandId,
                new(CommandOutcomeScopeKind.Partition, seed.Scenario.DestinationPartition)).Outcome is not null)
            { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
            var intent = view.ReadOwnedValue(RemoteTransferStorage.IntentKey(seed.Scenario.SourceQueue, seed.TransferId))
                ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
            return Convert.ToHexString(intent) + Convert.ToHexString(NativeSerialization.Serialize(
                RemoteTransferStorage.RequireSourceCounter(view, seed.Scenario.SourceQueue)));
        });

    internal async Task RequireAsync(RemoteTransferPostAwaitNativeCut actual)
    {
        await Assert.That(actual.Outcome).IsEqualTo(Outcome);
        await Assert.That(actual.Receipt).IsEqualTo(Receipt);
        await Assert.That(actual.Capacity).IsEqualTo(Capacity);
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(OriginalReceipt, actual.OriginalReceipt);
    }
}
