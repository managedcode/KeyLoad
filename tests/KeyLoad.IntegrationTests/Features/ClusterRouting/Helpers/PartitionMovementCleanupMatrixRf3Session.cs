using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>A new session never changes the original failed claim, marker or cleanup accounting.</summary>
internal static class PartitionMovementCleanupMatrixRf3Session
{
    internal static async Task RenewAsync(TwoRf3MembershipWave wave, RequestCqrsProbeFixture original,
        PartitionMovementCleanupMatrixRf3Fault[] faults, CancellationToken cancellationToken)
    {
        _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        wave.AssertAllNodeLocksReleased();
        var evidence = Path.GetFullPath(Path.Combine(ClusterFixtureProtocol.ArtifactDirectory,
            ClusterFixtureProtocol.QualificationDirectory, original.SessionId + PartitionMovementCleanupMatrixProtocol.EvidenceSuffix));
        if (Directory.Exists(evidence) || File.Exists(evidence))
        { throw new InvalidOperationException(PartitionMovementCleanupMatrixProtocol.FaultMismatch); }
        _ = await NodeEpochRf3Inventory.CopyTreeAsync(original.Root, evidence, cancellationToken).ConfigureAwait(false);
        var failures = new List<Exception>();
        foreach (var fault in faults)
        { ServerFailureObserver.Observe(() => fault.RestoreAfterJoin(wave, original), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        await RequireOriginalCleanupRefusalAsync(original).ConfigureAwait(false);
        var replacement = RequestCqrsProbeFixture.Create(wave.OwnedDataRoot, Guid.NewGuid());
        if (replacement.SessionId == original.SessionId || replacement.Root == original.Root)
        { throw new InvalidOperationException(PartitionMovementCleanupMatrixProtocol.FaultMismatch); }
        wave.queryControls = replacement;
        await wave.RestartJoinedAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireOriginalCleanupRefusalAsync(RequestCqrsProbeFixture original)
    {
        original.StopAdmission();
        original.RetainEvidence();
        var refusal = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(original.DisposeAfterResourcesJoinedAsync, refusal).ConfigureAwait(false);
        await Assert.That(refusal.Count).IsEqualTo(OneOriginalRefusal);
        await Assert.That(refusal[FirstRefusal] is InvalidOperationException).IsTrue();
        await Assert.That(refusal[FirstRefusal].Message).IsEqualTo(RequestCqrsProbeFixtureProtocol.UnsettledGates);
    }
    private const int OneOriginalRefusal = 1;
    private const int FirstRefusal = 0;
}
