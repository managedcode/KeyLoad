using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSettledCapabilitySerializationTests
{
    private const int EmptyCount = 0;
    private const long InitialSequence = 0;
    [Test]
    public async Task GeneratedAppendedCapabilityPreservesFullOriginalConfigureSourceAndCanonicalChildReceipt()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var values = NativeAnnSettledContractValues.Create(database);
        var session = Guid.NewGuid();
        var request = new AnnMaintenanceCapabilityRequest(values.Request, session,
            AnnMaintenanceCapabilityKind.SettleOriginalResult, null, null, values.Original);
        var restored = NativeSerialization.Deserialize<AnnMaintenanceCapabilityRequest>(NativeSerialization.Serialize(request));
        await Assert.That(restored.SessionId).IsEqualTo(session);
        await Assert.That(restored.Kind).IsEqualTo(AnnMaintenanceCapabilityKind.SettleOriginalResult);
        await Assert.That(restored.Page).IsNull();
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(restored.Maintenance)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Request)));
        await Assert.That(values.Request.MaintenanceProfile).IsNotNull();
        await Assert.That(restored.Maintenance.MaintenanceProfile).IsNotNull();
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(restored.Maintenance.MaintenanceProfile!)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Request.MaintenanceProfile!)));
        await Assert.That(restored.OriginalConfigure).IsNull();
        await Assert.That(restored.CheckpointIntent).IsNull();
        var admission = new AnnMaintenanceCapabilityRequest(values.Request, session,
            AnnMaintenanceCapabilityKind.AdmitOriginal, null, null, OriginalConfigure: values.Configure);
        var admitted = NativeSerialization.Deserialize<AnnMaintenanceCapabilityRequest>(NativeSerialization.Serialize(admission));
        await Assert.That(admitted.Kind).IsEqualTo(AnnMaintenanceCapabilityKind.AdmitOriginal);
        await Assert.That(admitted.FullOriginalResult).IsNull();
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(admitted.OriginalConfigure!)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Configure)));
        await Assert.That(admitted.OriginalConfigure!.Definition.AnnMaintenance).IsNotNull();
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(admitted.OriginalConfigure.Definition.AnnMaintenance!)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Request.MaintenanceProfile!)));
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(admitted.Maintenance)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Request)));
        await NativeAnnSettledResultAssertions.ExactAsync(values.Original, restored.FullOriginalResult!);
        var capability = new AnnMaintenanceCapabilityResult(session, values.Original.Source,
            values.Original.Source!.ThroughSequence, values.Original.Count, values.Intent,
            values.Original.IndexSha256, values.Original.Checkpoint!.Checkpoint, true, values.Original);
        var reply = NativeSerialization.Deserialize<AnnMaintenanceCapabilityResult>(NativeSerialization.Serialize(capability));
        await Assert.That(reply.Pending).IsTrue();
        await Assert.That(reply.SessionId).IsEqualTo(session);
        await Assert.That(reply.Source).IsEqualTo(capability.Source);
        await Assert.That(reply.ThroughSequence).IsEqualTo(capability.ThroughSequence);
        await Assert.That(reply.CheckpointAfter).IsEqualTo(capability.CheckpointAfter);
        await Assert.That(reply.Count).IsEqualTo(capability.Count);
        await Assert.That(reply.IndexSha256).IsEqualTo(capability.IndexSha256);
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(reply.OriginalCheckpointIntent!)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Intent)));
        await NativeAnnSettledResultAssertions.ExactAsync(values.Original, reply.OriginalResult!);
    }

    [Test]
    public async Task ExistingConstructorCallsRoundTripNullAppendedFieldsAndPendingWhileKindOrdinalsStayStable()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var values = NativeAnnSettledContractValues.Create(database, includeMaintenanceProfile: false);
        await Assert.That(values.Request.MaintenanceProfile).IsNull();
        await Assert.That(values.Configure.Definition.AnnMaintenance).IsNull();
        var configure = NativeSerialization.Deserialize<ProjectionConsumerInfo>(NativeSerialization.Serialize(values.Configure));
        await Assert.That(configure.Definition.AnnMaintenance).IsNull();
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(configure)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Configure)));
        var stage = new AnnMaintenanceCapabilityRequest(values.Request, Guid.NewGuid(),
            AnnMaintenanceCapabilityKind.StagePage, null, values.Intent);
        var retainedStage = NativeSerialization.Deserialize<AnnMaintenanceCapabilityRequest>(NativeSerialization.Serialize(stage));
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(retainedStage.CheckpointIntent!)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Intent)));
        AnnMaintenanceCapabilityKind[] kinds = [AnnMaintenanceCapabilityKind.Begin, AnnMaintenanceCapabilityKind.Load,
            AnnMaintenanceCapabilityKind.ApplyPage, AnnMaintenanceCapabilityKind.StagePage, AnnMaintenanceCapabilityKind.Verify,
            AnnMaintenanceCapabilityKind.Publish, AnnMaintenanceCapabilityKind.Abort, AnnMaintenanceCapabilityKind.Release,
            AnnMaintenanceCapabilityKind.AdmitOriginal, AnnMaintenanceCapabilityKind.SettleOriginalResult];
        for (var index = EmptyCount; index < kinds.Length; index++)
        {
            await Assert.That((int)kinds[index]).IsEqualTo(index);
            var request = new AnnMaintenanceCapabilityRequest(values.Request, Guid.NewGuid(), kinds[index], null, null);
            var restored = NativeSerialization.Deserialize<AnnMaintenanceCapabilityRequest>(NativeSerialization.Serialize(request));
            await Assert.That(restored.Kind).IsEqualTo(kinds[index]);
            await Assert.That(restored.Maintenance.MaintenanceProfile).IsNull();
            await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(restored.Maintenance)))
                .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(values.Request)));
            await Assert.That(restored.FullOriginalResult).IsNull();
            await Assert.That(restored.OriginalConfigure).IsNull();
        }
        var original = new AnnMaintenanceCapabilityResult(Guid.NewGuid(), null, InitialSequence, EmptyCount, null, null, InitialSequence, true);
        var reply = NativeSerialization.Deserialize<AnnMaintenanceCapabilityResult>(NativeSerialization.Serialize(original));
        await Assert.That(reply.OriginalResult).IsNull();
        await Assert.That(reply.Pending).IsTrue();
    }
}
