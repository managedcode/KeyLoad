using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWireWaveLifecycle
{
    internal static void Prepare(TwoRf3MembershipWave wave, string root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!wave.nativeBlobWireProbeSelected) { return; }
        if (!wave.registerPhysicalOwners || !wave.remoteDocumentReads || !wave.remotePartitionQueries
            || !wave.protectedDocuments || !wave.queryProbe || wave.nativeMembershipDiagnostics
            || wave.nativeDiscoveryOmissionSelected || wave.queryControls is null)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        wave.nativeBlobWireControls ??= new(root, wave.queryControls.SessionId);
    }

    internal static string[] Arguments(TwoRf3MembershipWave wave, string[] original)
    {
        if (!wave.nativeBlobWireProbeSelected) { return original; }
        var owner = wave.nativeBlobWireControls ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        return [.. original, "--KeyLoadTests:NativeBlobWireProbe:Root=" + owner.Root,
            "--KeyLoadTests:NativeBlobWireProbe:SessionId=" + owner.SessionId];
    }

    internal static Task PublishOwnerAsync(TwoRf3MembershipWave wave,
        IEnumerable<ParameterResource> parameters, CancellationToken token)
        => wave.nativeBlobWireControls?.PublishOwnerAsync(parameters, wave.Profile, token) ?? Task.CompletedTask;

    internal static void StopAdmission(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (wave.nativeBlobWireControls is { } owner)
        { KeyLoad.Server.ServerFailureObserver.Observe(owner.StopAdmission, failures); }
    }

    internal static void DeleteJoined(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (wave.applicationDisposed && wave.nodeLocksReleased && wave.nativeBlobWireControls is { } owner)
        { KeyLoad.Server.ServerFailureObserver.Observe(() => owner.DeleteAfterJoined(wave), failures); }
    }
}
