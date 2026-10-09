using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobFailedCutRf3Assertions
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementControlledBlobCommandRf3Proof proof, PartitionMovementPublicParentRf3NativeCut[] before,
        PartitionMovementPublicParentRf3NativeCut[] after)
    {
        var identity = new PartitionControlCommandIdentity(CommandOutcomeScopeKind.Partition, seed.Partition,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, proof.CommandId);
        var source = after.First(cut => cut.Header is not null);
        var command = PartitionMovementCapturePointerRf3Fault.Read<PartitionControlCommandRecord>(source,
            PartitionControlCommandKeys.Authority(identity));
        var grantId = ControlledDocumentTechnicalIdentity.Derive(command.OriginalOperation!, ControlledDocumentTechnicalIdentity.Authorize);
        var grant = PartitionMovementCapturePointerRf3Fault.Read<PartitionMovePhaseGrant>(source,
            PartitionMoveGrantStorage.Key(seed.Partition, grantId));
        await Assert.That(grant.PhaseCommandId).IsEqualTo(command.EffectId);
        var settlement = grant.Settlement ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(settlement.CommandId).IsEqualTo(command.EffectId);
        await Assert.That(grant.AbortDisposition).IsNull();
        await PartitionMovementControlledBlobFailedCutRf3Phases.RequireGrantAsync(seed, source, command, grant);
        foreach (var previous in before)
        {
            var current = after.Single(cut => cut.Node == previous.Node);
            await RequireOwnerAsync(wave, seed, command, grant, identity, previous, current).ConfigureAwait(false);
        }
    }

    private static async Task RequireOwnerAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionControlCommandRecord command, PartitionMovePhaseGrant grant, PartitionControlCommandIdentity identity,
        PartitionMovementPublicParentRf3NativeCut before, PartitionMovementPublicParentRf3NativeCut after)
    {
        var entries = PartitionMovementCapturePointerRf3Fault.AppliedEntries(wave, before, after);
        var source = after.Header is not null;
        var expected = PartitionMovementControlledBlobFailedCutRf3Phases.Expected(command, source);
        var seen = new HashSet<Guid>();
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        var membership = new PartitionMovementNativeMembershipRf3Cut(before, after, allowed);
        foreach (var entry in entries)
        {
            allowed.Add(Convert.ToHexString(KeySpace.AppliedBytes));
            if (entry.Operation is not { } operation)
            { continue; }
            if (await membership.TryApplyAsync(operation).ConfigureAwait(false))
            { continue; }
            membership.ObserveClock(operation);
            await Assert.That(operation.Kind).IsEqualTo(OperationKind.PartitionMovementPhase);
            await Assert.That(operation.PrincipalId).IsEqualTo(source ? grant.OperatorPrincipalId : PartitionStoreProtocol.AdministratorId);
            await Assert.That(expected.ContainsKey(operation.Id)).IsTrue();
            await Assert.That(seen.Add(operation.Id)).IsTrue();
            var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
            await Assert.That(phase.Stage).IsEqualTo(expected[operation.Id]);
            await SqlRf3Protocol.EqualAsync(seed.Partition, phase.Partition);
            await Assert.That(phase.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
            await PartitionMovementControlledBlobFailedCutRf3Phases.RequireOutcomeAsync(after, operation, phase, entry.Index, command);
            allowed.Add(Convert.ToHexString(KeySpace.ClockBytes));
            allowed.Add(Convert.ToHexString(KeySpace.PartitionOutcome(seed.Partition, operation.PrincipalId, operation.Id)));
            allowed.Add(Convert.ToHexString(CommandOutcomePartitionLocatorSerialization.ScopedKey(seed.Partition, operation.PrincipalId, operation.Id)));
        }
        await Assert.That(seen.SetEquals(expected.Keys)).IsTrue();
        await membership.RequireCompleteAsync().ConfigureAwait(false);
        if (source)
        { AddSourceKeys(allowed, seed, identity, grant); }
        await PartitionMovementCapturePointerRf3Cut.RequireExactRemainingRowsAsync(before, after, allowed);
        await Assert.That(after.StorePosition - before.StorePosition).IsEqualTo((long)entries.Length);
        await SqlRf3Protocol.EqualAsync(before.Header, after.Header);
        await SqlRf3Protocol.EqualAsync(before.Pending, after.Pending);
        await SqlRf3Protocol.EqualAsync(before.LastIssued, after.LastIssued);
        await Assert.That(after.OutstandingMoveGrants).IsEqualTo(before.OutstandingMoveGrants);
        await Assert.That(after.OutstandingPrincipalGrants).IsEqualTo(before.OutstandingPrincipalGrants);
        await Assert.That(after.OutstandingDatabaseGrants).IsEqualTo(before.OutstandingDatabaseGrants);
    }

    private static void AddSourceKeys(HashSet<string> allowed, PartitionMovementPublicParentRf3Seed seed,
        PartitionControlCommandIdentity identity, PartitionMovePhaseGrant grant)
    {
        foreach (var key in new[] { PartitionControlCommandKeys.Authority(identity), PartitionControlCommandKeys.Original(identity),
            CommandOutcomePartitionLocatorSerialization.ScopedKey(seed.Partition, identity.PrincipalId, identity.CommandId),
            PartitionMoveGrantStorage.Key(seed.Partition, grant.GrantId),
            PartitionMoveGrantStorage.MoveKey(seed.Partition, seed.FirstRequest.MoveId, grant.GrantId) })
        { allowed.Add(Convert.ToHexString(key)); }
    }
}
