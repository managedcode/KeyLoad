using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPolicyBusyColdRf3Assertions
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3NativeCut[] before, PartitionMovementPublicParentRf3NativeCut[] after,
        Guid commandId, PrincipalRecord requested)
    {
        foreach (var original in before)
        {
            var actual = after.Single(cut => cut.Node == original.Node);
            var entries = PartitionMovementCapturePointerRf3Fault.AppliedEntries(wave, original, actual);
            var mutations = entries.Where(entry => entry.Operation is not null).ToArray();
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (entries.Length != PartitionMoveProtocol.EmptyCount)
            { allowed.Add(Convert.ToHexString(KeySpace.AppliedBytes)); }
            if (original.Header is not null)
            {
                await Assert.That(mutations.Length).IsGreaterThan(PartitionMoveProtocol.EmptyCount);
                var outstanding = original.OutstandingPrincipalGrants
                    ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
                await Assert.That(outstanding).IsGreaterThan((long)PartitionMoveProtocol.EmptyCount);
                foreach (var entry in mutations)
                { await RequireFailedCommandAsync(original, actual, entry, commandId, requested, allowed); }
                var clock = mutations.Select(entry => entry.Operation!.EvaluatedAt)
                    .Append(PartitionMovementCapturePointerRf3Fault.Read<DateTimeOffset>(original, KeySpace.ClockBytes)).Max();
                await Assert.That(PartitionMovementCapturePointerRf3Fault.Read<DateTimeOffset>(actual,
                    KeySpace.ClockBytes)).IsEqualTo(clock);
            }
            else
            { await Assert.That(mutations.Length).IsEqualTo(PartitionMoveProtocol.EmptyCount); }
            await PartitionMovementCapturePointerRf3Cut.RequireExactRemainingRowsAsync(original, actual, allowed);
            await Assert.That(actual.StorePosition - original.StorePosition).IsEqualTo((long)entries.Length);
            await SqlRf3Protocol.EqualAsync(original.Header, actual.Header);
            await SqlRf3Protocol.EqualAsync(original.Pending, actual.Pending);
            await SqlRf3Protocol.EqualAsync(original.LastIssued, actual.LastIssued);
            await SqlRf3Protocol.EqualAsync(original.OriginalControlGrant, actual.OriginalControlGrant);
        }
    }

    private static async Task RequireFailedCommandAsync(PartitionMovementPublicParentRf3NativeCut before,
        PartitionMovementPublicParentRf3NativeCut after, ReplicaEntry entry, Guid commandId,
        PrincipalRecord requested, HashSet<string> allowed)
    {
        var operation = entry.Operation!;
        await Assert.That(operation.Id).IsEqualTo(commandId);
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.ConfigurePrincipal);
        await SqlRf3Protocol.EqualAsync(requested, NativeCommandPayload.Read<ConfigurePrincipalRequest>(operation).Principal);
        var actualRoot = PartitionMovementCapturePointerRf3Fault.Read<PrincipalRecord>(before,
            KeySpace.Principal(operation.PrincipalId));
        await Assert.That(actualRoot.ClusterAdministrator).IsTrue();
        var key = KeySpace.GlobalOutcome(operation.PrincipalId, commandId);
        await Assert.That(PartitionMovementCapturePointerRf3Fault.Rows(before).ContainsKey(Convert.ToHexString(key))).IsFalse();
        var stored = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(after, key);
        await Assert.That(stored.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Global);
        await Assert.That(stored.Partition).IsNull();
        await Assert.That(stored.PolicyEpoch).IsEqualTo(actualRoot.PolicyEpoch);
        await Assert.That(stored.Fingerprint).IsEqualTo(JsonData.Fingerprint(new
        { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson }));
        await Assert.That(stored.Result.Error).IsEqualTo((ErrorCode?)ErrorCode.Conflict);
        await Assert.That(stored.Result.SafeDetail).IsEqualTo(PartitionMoveProtocol.PolicyBusy);
        var principal = PartitionMovementCapturePointerRf3Fault.Read<PrincipalRecord>(after, KeySpace.Principal(requested.Id));
        await SqlRf3Protocol.EqualAsync(PartitionMovementPublicParentRf3Administrator.InitialDefinition(), principal);
        allowed.Add(Convert.ToHexString(key));
        allowed.Add(Convert.ToHexString(KeySpace.ClockBytes));
    }
}
