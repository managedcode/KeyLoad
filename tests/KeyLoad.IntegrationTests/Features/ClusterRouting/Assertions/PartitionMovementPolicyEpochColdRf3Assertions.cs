using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPolicyEpochColdRf3Assertions
{
    internal static Task RequireDemotedAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveResult terminal)
        => RequirePolicyAsync(cuts, PartitionMovementPublicParentRf3Administrator.Definition(
            PartitionMovementPolicyEpochColdRf3Operations.DemotedEpoch, false), terminal);

    internal static async Task RequireRestoredAsync(PartitionMovementPublicParentRf3NativeCut[] original,
        PartitionMovementPublicParentRf3NativeCut[] restored, PartitionMoveResult terminal)
    {
        await RequirePolicyAsync(restored, PartitionMovementPublicParentRf3Administrator.Definition(
            PartitionMovementPolicyEpochColdRf3Operations.RestoredEpoch, true), terminal);
        foreach (var before in original.Where(cut => cut.Header is not null))
        {
            var after = restored.Single(cut => cut.Node == before.Node);
            await SqlRf3Protocol.EqualAsync(before.Header, after.Header);
            await SqlRf3Protocol.EqualAsync(before.LastIssued, after.LastIssued);
            await SqlRf3Protocol.EqualAsync(before.SettledStagePages, after.SettledStagePages);
        }
    }

    internal static async Task RequirePolicyAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PrincipalRecord expected, PartitionMoveResult terminal)
    {
        foreach (var cut in cuts)
        {
            await SqlRf3Protocol.EqualAsync(expected, PartitionMovementCapturePointerRf3Fault.Read<PrincipalRecord>(
                cut, KeySpace.Principal(expected.Id)));
            if (cut.Header is not { } header)
            { continue; }
            await Assert.That(header.OperatorPrincipalId).IsEqualTo(expected.Id);
            await Assert.That(header.InitialPolicyEpoch).IsEqualTo(
                PartitionMovementPublicParentRf3Administrator.InitialDefinition().PolicyEpoch);
            await SqlRf3Protocol.EqualAsync(terminal, header.TerminalResult);
            await Assert.That(header.TerminalObservationReceipt).IsNotNull();
            await Assert.That(header.PendingOriginalPhaseCommandId).IsNull();
            await Assert.That(cut.OutstandingMoveGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
            await Assert.That(cut.OutstandingPrincipalGrants).IsEqualTo((long?)PartitionMoveProtocol.EmptyCount);
            await Assert.That(cut.OutstandingDatabaseGrants).IsEqualTo((long)PartitionMoveProtocol.EmptyCount);
        }
    }

    internal static async Task RequireReplayRowsAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementPublicParentRf3NativeCut[] before,
        PartitionMovementPublicParentRf3NativeCut[] after)
    {
        foreach (var original in before)
        {
            var actual = after.Single(cut => cut.Node == original.Node);
            var entries = PartitionMovementCapturePointerRf3Fault.AppliedEntries(wave, original, actual);
            foreach (var entry in entries.Where(entry => entry.Operation is not null))
            {
                var operation = entry.Operation!;
                await Assert.That(operation.Kind).IsEqualTo(OperationKind.Batch);
                await Assert.That(operation.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
                var command = seed.Originals.Single(item => item.Command.CommandId == operation.Id).Command;
                await SqlRf3Protocol.EqualAsync(command, NativeCommandPayload.Read<CommandRequest>(operation));
            }
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (entries.Length != PartitionMoveProtocol.EmptyCount)
            { allowed.Add(Convert.ToHexString(KeySpace.AppliedBytes)); }
            await PartitionMovementCapturePointerRf3Cut.RequireExactRemainingRowsAsync(original, actual, allowed);
            await Assert.That(actual.StorePosition - original.StorePosition).IsEqualTo((long)entries.Length);
            if (original.Header is not null)
            {
                await RequireOriginalUserOutcomesAsync(seed, original, actual);
                await PartitionMovementBlobOriginalOutcomeRf3Assertions.RequireAsync(seed, original, actual);
            }
        }
    }

    private static async Task RequireOriginalUserOutcomesAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut before, PartitionMovementPublicParentRf3NativeCut after)
    {
        foreach (var original in seed.Originals)
        {
            var key = KeySpace.PartitionOutcome(seed.Partition,
                PartitionMovementPublicParentRf3Administrator.PrincipalId, original.Command.CommandId);
            var retained = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(before, key);
            await Assert.That(retained.PolicyEpoch).IsEqualTo(
                PartitionMovementPublicParentRf3Administrator.InitialDefinition().PolicyEpoch);
            await Assert.That(retained.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
            await SqlRf3Protocol.EqualAsync(seed.Partition, retained.Partition);
            await Assert.That(retained.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
            await Assert.That(retained.Result.Error).IsNull();
            await SqlRf3Protocol.EqualAsync(original.Receipt, retained.Result.Get<CommitReceipt>());
            await SqlRf3Protocol.EqualAsync(retained,
                PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(after, key));
        }
    }
}
