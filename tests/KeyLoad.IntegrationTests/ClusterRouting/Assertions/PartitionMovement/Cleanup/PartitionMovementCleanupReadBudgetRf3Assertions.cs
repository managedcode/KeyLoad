using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupReadBudgetRf3Assertions
{
    private const int FirstTarget = 0;
    private const int PartCount = 2;
    private const int FullPart = 65536;
    private const int TailPart = 4;
    private const int OneByte = 1;
    private const byte FirstByte = 0x41;
    private const byte TailByte = 0x42;
    private const char Separator = ':';
    internal static async Task<long> LimitAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut[] prepared)
    {
        await NodesAsync(prepared);
        var targets = prepared.Skip(TwoRf3MembershipProtocol.MembersPerGroup).ToArray();
        var parts = Rows(targets[FirstTarget], KeySpace.Partition(PartitionRecordFamilies.BlobPart, seed.Partition));
        await Assert.That(parts.Length).IsEqualTo(PartCount);
        await Assert.That(parts.Select(Value).Any(value => value.Length == FullPart && value.All(item => item == FirstByte))).IsTrue();
        await Assert.That(parts.Select(Value).Any(value => value.Length == TailPart && value.All(item => item == TailByte))).IsTrue();
        var denied = checked(parts.Max(Bytes) - OneByte);
        var bulkPagePrefix = KeySpace.Partition(PartitionMoveProtocol.TargetPageSpace, seed.Partition);
        var partPrefix = KeySpace.Partition(PartitionRecordFamilies.BlobPart, seed.Partition);
        foreach (var target in targets)
        {
            await SqlRf3Protocol.EqualAsync(parts, Rows(target, partPrefix));
            var metadata = target.Rows.Where(row => !HasPrefix(row, partPrefix) && !HasPrefix(row, bulkPagePrefix));
            await Assert.That(metadata.Sum(Bytes)).IsLessThan(denied);
        }
        new DatabaseLimits { MaxQueryReadBytes = denied }.Validate();
        return denied;
    }

    internal static async Task FailureAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut[] prepared, PartitionMovementPublicParentRf3NativeCut[] failed)
    {
        await NodesAsync(failed);
        foreach (var control in failed.Take(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            await Assert.That(control.Pending).IsNull();
            var original = control.OriginalPhase!;
            await Assert.That(original.Stage).IsEqualTo(PartitionMovePeerStage.Abort);
            await OutcomeAsync(original.OriginalResult!);
            await Assert.That(original.ObservationCheckpointReceipt).IsNotNull();
            var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(original.OriginalPhase!.Body.Span);
            await Assert.That(body.Role).IsEqualTo(PartitionMoveCleanupRole.Target);
            await Assert.That(body.Control.Phase).IsEqualTo(PartitionMovePhase.Aborting);
            await Assert.That(PartitionMoveCleanupFamilies.All[body.FamilyOrdinal]).IsEqualTo(PartitionRecordFamilies.BlobPart);
            var fence = PartitionMoveSourceFenceStorage.Key(seed.Partition);
            await SqlRf3Protocol.EqualAsync(Rows(prepared.Single(cut => cut.Node == control.Node), fence), Rows(control, fence));
        }
        var source = failed.First(cut => cut.OriginalPhase is not null).OriginalPhase!;
        var prefix = KeySpace.Partition(PartitionRecordFamilies.BlobPart, seed.Partition);
        foreach (var target in failed.Skip(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            await SqlRf3Protocol.EqualAsync(Rows(prepared.Single(cut => cut.Node == target.Node), prefix), Rows(target, prefix));
            var issued = target.OriginalReceiverIssuance!;
            await Assert.That(issued.OriginalStage).IsEqualTo(PartitionMovePeerStage.Abort);
            await Assert.That(issued.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
            await Assert.That(issued.Partition).IsEqualTo(seed.Partition);
            await Assert.That(issued.OriginalPhaseCommandId).IsEqualTo(source.OriginalPhaseCommandId);
            await Assert.That(issued.OriginalPageOrdinal).IsEqualTo(source.PageOrdinal);
            await Assert.That(issued.OriginalBodyDigest).IsEqualTo(source.OriginalBodyDigest);
            await Assert.That(issued.OriginalPhaseIdentityDigest).IsEqualTo(source.OriginalPhaseIdentityDigest);
            await Assert.That(issued.OriginalRequestNonce).IsEqualTo(source.OriginalRequestNonce);
            await Assert.That(issued.OriginalExpiresAt).IsEqualTo(source.OriginalExpiresAt);
            await SqlRf3Protocol.EqualAsync(source.OriginalReceiverOwner, issued.ReceiverOwner);
            await OutcomeAsync(target.OriginalNativeResult!);
        }
    }

    private static async Task OutcomeAsync(OperationResult actual)
    {
        await Assert.That(actual.Json).IsNull();
        await Assert.That(actual.Error).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(actual.SafeDetail).IsEqualTo(PartitionMoveProtocol.Capacity);
        await Assert.That(actual.NativeValue).IsNull();
    }

    private static byte[] Value(string row)
    {
        var separator = row.IndexOf(Separator, StringComparison.Ordinal);
        return Convert.FromHexString(row[(separator + OneByte)..]);
    }

    private static string[] Rows(PartitionMovementPublicParentRf3NativeCut cut, byte[] prefix)
        => cut.Rows.Where(row => HasPrefix(row, prefix)).ToArray();

    private static bool HasPrefix(string row, byte[] prefix)
        => row.StartsWith(Convert.ToHexString(prefix), StringComparison.Ordinal);

    private static long Bytes(string row)
    {
        var separator = row.IndexOf(Separator, StringComparison.Ordinal);
        return checked(Convert.FromHexString(row[..separator]).Length + Convert.FromHexString(row[(separator + OneByte)..]).Length);
    }

    private static async Task NodesAsync(PartitionMovementPublicParentRf3NativeCut[] cuts)
    {
        await Assert.That(cuts.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        await Assert.That(cuts.Select(cut => cut.Node).Order(StringComparer.Ordinal).SequenceEqual(
            TwoRf3MembershipProtocol.Nodes.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
    }
}
