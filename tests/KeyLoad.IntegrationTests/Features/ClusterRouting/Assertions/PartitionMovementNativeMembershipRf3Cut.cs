using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Accounts only actual native membership CAS and its own native outcome; every model row stays exact.</summary>
internal sealed class PartitionMovementNativeMembershipRf3Cut
{
    private const long InitialVersion = 0;
    private const long VersionStep = 1;
    private readonly PartitionMovementPublicParentRf3NativeCut after;
    private readonly HashSet<string> allowed;
    private readonly PrincipalRecord principal;
    private readonly Guid incarnation;
    private readonly byte[] membershipKey = KeyCodec.Encode(ReplicaMembershipProtocol.StorageSpace, ReplicaMembershipProtocol.TableKey);
    private MembershipRecord? expected;
    internal DateTimeOffset ExpectedClock { get; private set; }

    internal PartitionMovementNativeMembershipRf3Cut(PartitionMovementPublicParentRf3NativeCut before,
        PartitionMovementPublicParentRf3NativeCut after, HashSet<string> allowed)
    {
        this.after = after;
        this.allowed = allowed;
        var rows = PartitionMovementCapturePointerRf3Fault.Rows(before);
        if (rows.TryGetValue(Convert.ToHexString(membershipKey), out var bytes))
        { expected = NativeSerialization.Deserialize<MembershipRecord>(Convert.FromHexString(bytes)); }
        principal = PartitionMovementCapturePointerRf3Fault.Read<PrincipalRecord>(before,
            KeySpace.Principal(PartitionStoreProtocol.AdministratorId));
        incarnation = PartitionMovementCapturePointerRf3Fault.Read<PhysicalShardCatalog>(before,
            PhysicalShardCatalogRecordSerialization.CatalogKey()).DefaultShard.Incarnation;
        ExpectedClock = PartitionMovementCapturePointerRf3Fault.Read<DateTimeOffset>(before, KeySpace.ClockBytes);
    }

    internal void ObserveClock(ReplicatedOperation operation)
    {
        if (operation.EvaluatedAt > ExpectedClock)
        { ExpectedClock = operation.EvaluatedAt; }
        allowed.Add(Convert.ToHexString(KeySpace.ClockBytes));
    }

    internal async Task<bool> TryApplyAsync(ReplicatedOperation operation)
    {
        if (operation.Kind != OperationKind.Membership)
        { return false; }
        await Assert.That(operation.PrincipalId).IsEqualTo(PartitionStoreProtocol.AdministratorId);
        await Assert.That(principal.Id).IsEqualTo(operation.PrincipalId);
        await Assert.That(principal.ClusterAdministrator).IsTrue();
        var mutation = NativeCommandPayload.Read<MembershipMutation>(operation);
        await Assert.That(mutation.Key).IsEqualTo(ReplicaMembershipProtocol.TableKey);
        var key = KeySpace.GlobalOutcome(operation.PrincipalId, operation.Id);
        var outcome = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(after, key);
        await Assert.That(outcome.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(operation));
        await Assert.That(outcome.Incarnation).IsEqualTo(incarnation);
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Global);
        await Assert.That(outcome.Partition).IsNull();
        await Assert.That(outcome.BlobAuthority).IsNull();
        await Assert.That(outcome.CompositionAuthority).IsNull();
        await Assert.That(outcome.Result.Error).IsNull();
        var applied = mutation.ExpectedVersion == (expected?.Version ?? InitialVersion);
        await Assert.That(outcome.Result.Get<bool>()).IsEqualTo(applied);
        if (applied)
        { expected = new(checked((expected?.Version ?? InitialVersion) + VersionStep), mutation.Payload); }
        allowed.Add(Convert.ToHexString(key));
        allowed.Add(Convert.ToHexString(membershipKey));
        ObserveClock(operation);
        return true;
    }

    internal async Task RequireCompleteAsync()
    {
        var rows = PartitionMovementCapturePointerRf3Fault.Rows(after);
        var actual = rows.TryGetValue(Convert.ToHexString(membershipKey), out var bytes) ? bytes : null;
        var encoded = expected is null ? null : Convert.ToHexString(NativeSerialization.Serialize(expected));
        await Assert.That(actual).IsEqualTo(encoded);
        await Assert.That(PartitionMovementCapturePointerRf3Fault.Read<DateTimeOffset>(after, KeySpace.ClockBytes))
            .IsEqualTo(ExpectedClock);
    }
}
