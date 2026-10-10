using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NativeCapabilityOmissionRf3Trial
{
    internal static async Task RunAsync(bool official, CancellationToken callerCancellation)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, timeout.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartNativeCapabilityOmissionAsync(lifetime.Token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, lifetime.Token).ConfigureAwait(false);
            await ExecuteAsync(wave, seed, official, lifetime.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count > 0)
        { wave?.RetainRoots(); }
        if (seed is { } ownedSeed)
        { await ServerFailureObserver.ObserveAsync(() => ownedSeed.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } ownedWave)
        { await ServerFailureObserver.ObserveAsync(() => ownedWave.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        bool official, CancellationToken token)
    {
        var controls = wave.nativeDiscoveryOmission ?? throw new InvalidOperationException(NativeCapabilityOmissionRf3Protocol.MissingState);
        var command = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents,
            NativeCapabilityOmissionRf3Protocol.DocumentId, NativeCapabilityOmissionRf3Protocol.DocumentJson));
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token).ConfigureAwait(false);
        controls.Arm(official);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        await NativeCapabilityOmissionRf3Callers.RequireRefusedAsync(seed, command, official, token).ConfigureAwait(false);
        await controls.RequireCausalRefusalAsync().ConfigureAwait(false);
        var refused = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token).ConfigureAwait(false);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, refused).ConfigureAwait(false);
        await controls.RepairAfterOwnersJoinedAsync(wave, token).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        await NativeCapabilityOmissionRf3Healthy.CompleteAsync(wave, seed, command, token).ConfigureAwait(false);
    }
}
