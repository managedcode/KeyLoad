using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsWaitForIndexValidationTests
{
    private const string AppliedOwnershipEpochField = "AppliedOwnershipEpoch";
    private const string ResultCutPositionField = "ResultCutPosition";
    private const string AppliedIncarnationField = "AppliedIncarnation";
    private const string AppliedAtomicPartitionIdField = "AppliedAtomicPartitionId";
    private const string AppliedPositionField = "AppliedPosition";
    private const string StorePositionField = "StorePosition";
    private const string PolicyEpochField = "PolicyEpoch";
    private const string SchemaVersionField = "SchemaVersion";
    private const string CommandIdField = "CommandId";
    private const string ActivationIdField = "ActivationId";
    private const string NativeRequestDigestField = "NativeRequestDigest";
    private const string PartitionField = "Partition";

    [Test]
    public async Task DeclaredWaitWitnessRoundTripsAllFieldsAndKeepsIndependentHorizons()
    {
        var input = new RequestCqrsWaitForIndexValidationInput();
        await Assert.That(input.Value.AppliedPosition).IsGreaterThan(input.Value.StorePosition);
        await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
        await Assert.That(input.Read(input.PadTo(input.Settings.MaximumRecordBytes))).IsEqualTo(input.Value);
        RequestCqrsWaitForIndexReadCutInventory.RequireInventory([input.Value], input.Markers);
        RequestCqrsWaitForIndexReadCutInventory.RequireArm(input.Value, input.ReadArm(input.Arm));
        var name = RequestCqrsWaitForIndexReadCutInventory.Name(input.Value.ArmId, input.Value.RequestId);
        await Assert.That(RequestCqrsWaitForIndexReadCutInventory.IsName(name)).IsTrue();
        await Assert.That(RequestCqrsWaitForIndexReadCutInventory.IsName(name.ToUpperInvariant())).IsFalse();
    }

    [Test]
    public async Task MissingUnknownDuplicateEscapedAndOversizedRecordsRefuse()
    {
        var input = new RequestCqrsWaitForIndexValidationInput();
        var invalidUtf8 = input.Write();
        invalidUtf8[0] = 0xff;
        byte[][] invalid =
        [
            input.Change(value => value.Remove(AppliedOwnershipEpochField)),
            input.Change(value => value[ResultCutPositionField] = 1),
            input.DuplicateOrEscapeVersion(duplicate: true),
            input.DuplicateOrEscapeVersion(duplicate: false),
            invalidUtf8,
            input.PadTo(checked(input.Settings.MaximumRecordBytes + 1))
        ];
        foreach (var bytes in invalid)
        {
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => input.Read(bytes));
            await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
        }
    }

    [Test]
    public async Task MismatchedTokenIdentityAndInvalidAuthorityRefuse()
    {
        var input = new RequestCqrsWaitForIndexValidationInput();
        Action<System.Text.Json.Nodes.JsonObject>[] changes =
        [
            value => value[AppliedIncarnationField] = Guid.NewGuid().ToString(),
            value => value[AppliedAtomicPartitionIdField] = Guid.NewGuid().ToString(),
            value => value[AppliedPositionField] = -1,
            value => value[AppliedOwnershipEpochField] = -1,
            value => value[StorePositionField] = -1,
            value => value[PolicyEpochField] = 0,
            value => value[SchemaVersionField] = 0,
            value => value[CommandIdField] = Guid.NewGuid().ToString(),
            value => value[ActivationIdField] = "not-an-activation",
            value => value[NativeRequestDigestField] = "bad",
            value => value[PartitionField] = null
        ];
        foreach (var change in changes)
        {
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => input.Read(input.Change(change)));
            await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
        }
    }

    [Test]
    public async Task OrphanForeignSessionDuplicateAndWrongOwnerMarkersRefuse()
    {
        var input = new RequestCqrsWaitForIndexValidationInput();
        var original = input.Markers;
        RequestCqrsProbeMarkerRecord[][] invalid =
        [
            [original[0], original[2]],
            [original[1], original[2]],
            [original[0], original[1]],
            [.. original, original[2]],
            [original[0] with { SessionId = Guid.NewGuid().ToString("N") }, original[1], original[2]],
            [original[0], original[1] with { Voter = "n2" }, original[2]],
            [original[0], original[1], original[2] with { SiloAddress = "127.0.0.1:11112@2" }],
            [original[0], original[1], original[2] with { RequestId = Guid.NewGuid() }]
        ];
        foreach (var markers in invalid)
        {
            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsWaitForIndexReadCutInventory.RequireInventory([input.Value], markers));
            RequestCqrsWaitForIndexReadCutInventory.RequireInventory([input.Value], original);
        }
        _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
            RequestCqrsWaitForIndexReadCutInventory.RequireInventory([], original));
        await Assert.That(input.Read(input.Write())).IsEqualTo(input.Value);
    }

    [Test]
    public async Task DifferentArmContractAndCapturedPhaseCannotQualifyAWait()
    {
        var input = new RequestCqrsWaitForIndexValidationInput();
        var arm = input.Arm;
        RequestCqrsProbeArmRecord[] invalid =
        [
            arm with { SessionId = Guid.NewGuid().ToString("N") },
            arm with { ArmId = Guid.NewGuid() },
            arm with { PrincipalId = "c1-probe-other-input" },
            arm with { ReadKind = GrainReadKind.NodeStatus },
            arm with { Phase = RequestCqrsProbePhase.WaitForIndexReadCutCaptured },
            arm with { TargetVoter = "n2" },
            arm with { CommandId = Guid.NewGuid() }
        ];
        foreach (var candidate in invalid)
        {
            _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsWaitForIndexReadCutInventory.RequireArm(input.Value, candidate));
            RequestCqrsWaitForIndexReadCutInventory.RequireArm(input.Value, arm);
        }
        _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
            input.ReadArm(arm with { Phase = RequestCqrsProbePhase.WaitForIndexReadCutCaptured }));
        await Assert.That(input.ReadArm(arm)).IsEqualTo(arm);
    }
}
