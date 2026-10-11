using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

/// <summary>Retains the real exclusive publication lease while the original purpose owners attach read-only.</summary>
internal static class NativePurposeEnrollmentRestart
{
    internal static async Task RestartAsync(TwoRf3MembershipWave wave,
        NativeCapabilityOmissionRf3Fixture controls, CancellationToken token)
    {
        var path = Path.Combine(controls.Root, NativeDiscoveryOmissionProtocol.LockFile);
        EnsureOriginalLock(path);
        var before = await NodeEpochRf3Inventory.CaptureAsync(controls.Root, token).ConfigureAwait(false);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var lease = OpenWriter(path);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(lease.Length).IsEqualTo(0L);
                RequireWriterExclusion(path);
                await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
                RequireWriterExclusion(path);
                await Assert.That(lease.Length).IsEqualTo(0L);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var after = await NodeEpochRf3Inventory.CaptureAsync(controls.Root, token).ConfigureAwait(false);
            await Assert.That(before.Equivalent(after)).IsTrue();
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void EnsureOriginalLock(string path)
    {
        using var lease = OpenWriter(path);
        if (lease.Length != NativeDiscoveryOmissionProtocol.EmptyCount)
        { throw new IOException(NativeDiscoveryOmissionProtocol.Invalid); }
    }

    private static FileStream OpenWriter(string path)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = IntegrationRoutingOptions.ProbeExecution().Value.FileBufferBytes,
            UnixCreateMode = RequestCqrsProbeFileValidation.PrivateFileMode
        });
    }

    private static void RequireWriterExclusion(string path)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            using var forbidden = TryOpenWriter(path);
            if (forbidden is not null)
            { failures.Add(new InvalidOperationException("The original publication writer lease did not exclude another writer.")); }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static FileStream? TryOpenWriter(string path)
    {
        try
        { return OpenWriter(path); }
        catch (IOException)
        { return null; }
    }
}
