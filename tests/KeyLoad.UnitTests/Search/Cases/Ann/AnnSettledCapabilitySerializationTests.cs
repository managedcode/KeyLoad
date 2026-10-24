using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Search;

internal sealed class AnnSettledCapabilitySerializationTests
{
    private const int EmptyCount = 0;
    private const long InitialSequence = 0;
    [Test]
    public async Task GeneratedAppendedCapabilityPreservesFullOriginalConfigureSourceAndCanonicalChildReceipt()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var values = AnnSettledContractValues.Create(database);
        var session = Guid.NewGuid();
        var request = new AnnMaintenanceCapabilityRequest(values.Request, session,
            AnnMaintenanceCapabilityKind.SettleOriginalResult, null, null, values.Original);
        var restored = OrleansSerialization.Deserialize<AnnMaintenanceCapabilityRequest>(OrleansSerialization.Serialize(request));
        await Assert.That(restored.SessionId).IsEqualTo(session);
        await Assert.That(restored.Kind).IsEqualTo(AnnMaintenanceCapabilityKind.SettleOriginalResult);
        await Assert.That(restored.Page).IsNull();
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(restored.Maintenance)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Request)));
        await Assert.That(values.Request.MaintenanceProfile).IsNotNull();
        await Assert.That(restored.Maintenance.MaintenanceProfile).IsNotNull();
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(restored.Maintenance.MaintenanceProfile!)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Request.MaintenanceProfile!)));
        await Assert.That(restored.OriginalConfigure).IsNull();
        await Assert.That(restored.CheckpointIntent).IsNull();
        var admission = new AnnMaintenanceCapabilityRequest(values.Request, session,
            AnnMaintenanceCapabilityKind.AdmitOriginal, null, null, OriginalConfigure: values.Configure);
        var admitted = OrleansSerialization.Deserialize<AnnMaintenanceCapabilityRequest>(OrleansSerialization.Serialize(admission));
        await Assert.That(admitted.Kind).IsEqualTo(AnnMaintenanceCapabilityKind.AdmitOriginal);
        await Assert.That(admitted.FullOriginalResult).IsNull();
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(admitted.OriginalConfigure!)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Configure)));
        await Assert.That(admitted.OriginalConfigure!.Definition.AnnMaintenance).IsNotNull();
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(admitted.OriginalConfigure.Definition.AnnMaintenance!)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Request.MaintenanceProfile!)));
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(admitted.Maintenance)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Request)));
        await AnnSettledResultAssertions.ExactAsync(values.Original, restored.FullOriginalResult!);
        var capability = new AnnMaintenanceCapabilityResult(session, values.Original.Source,
            values.Original.Source!.ThroughSequence, values.Original.Count, values.Intent,
            values.Original.IndexSha256, values.Original.Checkpoint!.Checkpoint, true, values.Original);
        var reply = OrleansSerialization.Deserialize<AnnMaintenanceCapabilityResult>(OrleansSerialization.Serialize(capability));
        await Assert.That(reply.Pending).IsTrue();
        await Assert.That(reply.SessionId).IsEqualTo(session);
        await Assert.That(reply.Source).IsEqualTo(capability.Source);
        await Assert.That(reply.ThroughSequence).IsEqualTo(capability.ThroughSequence);
        await Assert.That(reply.CheckpointAfter).IsEqualTo(capability.CheckpointAfter);
        await Assert.That(reply.Count).IsEqualTo(capability.Count);
        await Assert.That(reply.IndexSha256).IsEqualTo(capability.IndexSha256);
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(reply.OriginalCheckpointIntent!)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Intent)));
        await AnnSettledResultAssertions.ExactAsync(values.Original, reply.OriginalResult!);
    }

    [Test]
    public async Task ExistingConstructorCallsRoundTripNullAppendedFieldsAndPendingWhileKindOrdinalsStayStable()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var values = AnnSettledContractValues.Create(database, includeMaintenanceProfile: false);
        await Assert.That(values.Request.MaintenanceProfile).IsNull();
        await Assert.That(values.Configure.Definition.AnnMaintenance).IsNull();
        var configure = OrleansSerialization.Deserialize<ProjectionConsumerInfo>(OrleansSerialization.Serialize(values.Configure));
        await Assert.That(configure.Definition.AnnMaintenance).IsNull();
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(configure)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Configure)));
        var stage = new AnnMaintenanceCapabilityRequest(values.Request, Guid.NewGuid(),
            AnnMaintenanceCapabilityKind.StagePage, null, values.Intent);
        var retainedStage = OrleansSerialization.Deserialize<AnnMaintenanceCapabilityRequest>(OrleansSerialization.Serialize(stage));
        await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(retainedStage.CheckpointIntent!)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Intent)));
        AnnMaintenanceCapabilityKind[] kinds = [AnnMaintenanceCapabilityKind.Begin, AnnMaintenanceCapabilityKind.Load,
            AnnMaintenanceCapabilityKind.ApplyPage, AnnMaintenanceCapabilityKind.StagePage, AnnMaintenanceCapabilityKind.Verify,
            AnnMaintenanceCapabilityKind.Publish, AnnMaintenanceCapabilityKind.Abort, AnnMaintenanceCapabilityKind.Release,
            AnnMaintenanceCapabilityKind.AdmitOriginal, AnnMaintenanceCapabilityKind.SettleOriginalResult];
        for (var index = EmptyCount; index < kinds.Length; index++)
        {
            await Assert.That((int)kinds[index]).IsEqualTo(index);
            var request = new AnnMaintenanceCapabilityRequest(values.Request, Guid.NewGuid(), kinds[index], null, null);
            var restored = OrleansSerialization.Deserialize<AnnMaintenanceCapabilityRequest>(OrleansSerialization.Serialize(request));
            await Assert.That(restored.Kind).IsEqualTo(kinds[index]);
            await Assert.That(restored.Maintenance.MaintenanceProfile).IsNull();
            await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(restored.Maintenance)))
                .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(values.Request)));
            await Assert.That(restored.FullOriginalResult).IsNull();
            await Assert.That(restored.OriginalConfigure).IsNull();
        }
        var original = new AnnMaintenanceCapabilityResult(Guid.NewGuid(), null, InitialSequence, EmptyCount, null, null, InitialSequence, true);
        var reply = OrleansSerialization.Deserialize<AnnMaintenanceCapabilityResult>(OrleansSerialization.Serialize(original));
        await Assert.That(reply.OriginalResult).IsNull();
        await Assert.That(reply.Pending).IsTrue();
    }
}
