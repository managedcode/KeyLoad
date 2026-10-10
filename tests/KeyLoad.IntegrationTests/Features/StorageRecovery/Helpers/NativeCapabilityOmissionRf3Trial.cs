using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NativeCapabilityOmissionRf3Trial
{
    internal static Task RunAsync(bool official, CancellationToken callerCancellation)
        => RunOwnedAsync(official, purpose: false, callerCancellation);
    internal static Task RunPurposeAsync(bool official, CancellationToken callerCancellation)
        => RunOwnedAsync(official, purpose: true, callerCancellation);
    private static async Task RunOwnedAsync(bool official, bool purpose, CancellationToken callerCancellation)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, timeout.Token);
        var failures = new List<Exception>();
        var evidence = new NativeCapabilityOperationFailureEvidence();
        var phase = "LocalImage";
        LocalRf3ImageTestSession? localImage = null;
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            localImage = await LocalRf3ImageTestSession.StartIfSelectedAsync(lifetime.Token).ConfigureAwait(false);
            phase = "WaveStart";
            var startup = new KeyLoad.IntegrationTests.Features.Messaging.RemoteTransferStartupCallbacks(
                (_, _) => { }, () => Task.CompletedTask, (failure, _) => evidence.Original(phase, official, [failure],
                    timeout.Token, lifetime.Token, callerCancellation), () => Task.CompletedTask);
            wave = await TwoRf3MembershipWave.StartNativeCapabilityOmissionAsync(lifetime.Token, localImage?.Selection,
                value => phase = value, startup).ConfigureAwait(false);
            phase = "SeedCreate";
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, lifetime.Token).ConfigureAwait(false);
            await ExecuteAsync(wave, seed, official, purpose, value => phase = value, lifetime.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count > 0)
        {
            ServerFailureObserver.Observe(() => evidence.Original(phase, official, failures, timeout.Token, lifetime.Token, callerCancellation), failures);
            wave?.RetainRoots();
        }
        if (seed is { } ownedSeed)
        { await ServerFailureObserver.ObserveAsync(() => ownedSeed.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        var beforeWaveCleanup = failures.Count;
        if (wave is { } ownedWave)
        { await ServerFailureObserver.ObserveAsync(() => ownedWave.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        var waveCleanupSucceeded = wave is not null && failures.Count == beforeWaveCleanup;
        if (localImage is { } ownedImage)
        { await ServerFailureObserver.ObserveAsync(() => ownedImage.DisposeAsync(removeImage: waveCleanupSucceeded).AsTask(), failures).ConfigureAwait(false); }
        if (failures.Count > 0)
        { ServerFailureObserver.Observe(() => evidence.Terminal(phase, official, failures, timeout.Token, lifetime.Token, callerCancellation), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        bool official, bool purpose, Action<string> enter, CancellationToken token)
    {
        var controls = wave.nativeDiscoveryOmission ?? throw new InvalidOperationException(NativeCapabilityOmissionRf3Protocol.MissingState);
        var command = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents,
            NativeCapabilityOmissionRf3Protocol.DocumentId, NativeCapabilityOmissionRf3Protocol.DocumentJson));
        enter("BeforeCut");
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token).ConfigureAwait(false);
        enter("Arm");
        if (purpose)
        { controls.ArmPurpose(official, command.CommandId); }
        else
        { controls.Arm(official); }
        enter("RestartArmed");
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        enter("PublicCaller");
        if (purpose)
        { await NativeSignedPurposeRf3Callers.RequireRefusedAsync(seed, command, official, token).ConfigureAwait(false); }
        else
        { await NativeCapabilityOmissionRf3Callers.RequireRefusedAsync(seed, command, official, token).ConfigureAwait(false); }
        enter("CausalWitness");
        if (purpose)
        { await controls.RequirePurposeCausalRefusalAsync().ConfigureAwait(false); }
        else
        { await controls.RequireCausalRefusalAsync().ConfigureAwait(false); }
        enter("RefusedCut");
        var refused = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token).ConfigureAwait(false);
        enter("NoEffects");
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, refused).ConfigureAwait(false);
        enter("Repair");
        await controls.RepairAfterOwnersJoinedAsync(wave, token).ConfigureAwait(false);
        enter("RestartHealthy");
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        enter("Healthy");
        await NativeCapabilityOmissionRf3Healthy.CompleteAsync(wave, seed, command, token, coldCuts: purpose ? 2 : 1).ConfigureAwait(false);
    }
}
