
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspireContinuation
{
    internal static async Task ExecuteAsync(ControlledRetireAspireFixture fixture,
        PartitionMovementPublicParentRf3Seed seed, ControlledRetireAspireSeedState state,
        ControlledRetireAspireOriginal original, Guid arm, PartitionMoveResult expected, CancellationToken token)
    {
        await fixture.RestartAsync(token);
        await seed.ReconnectAsync(fixture.Wave, token);
        var cuts = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, arm, original.Operation.Id,
            token, expected.InstalledReceipt);
        await ControlledRetireAspirePending.RetainedAsync(original, cuts);
        state = await ControlledRetireAspireModels.NativeAsync(seed, state, cuts);
        await ControlledRetireAspireSeed.ReplayAsync(seed, state, token);
        await ControlledRetireAspireProducer.ExpiredAsync(original, token);
        await ControlledRetireAspirePublicAssertions.CompleteAsync(seed, seed.FirstRequest with
        {
            Mode = PartitionMoveMode.Resume
        }, expected, token);
        await ControlledRetireAspireSeed.ReplayAsync(seed, state, token);
        var healthy = await ControlledRetireAspirePublicAssertions.HealthyAsync(seed, token);
        var healthyCuts = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, arm,
            original.Operation.Id, token, healthy.Receipt.Token);
        await ControlledRetireAspirePublicAssertions.HealthyNativeAsync(seed, healthy, healthyCuts);
        _ = await fixture.StopAndReadAsync(seed.FirstRequest, original.Operation.Id, token);
        await fixture.RestartAsync(token);
        await seed.ReconnectAsync(fixture.Wave, token);
        await ControlledRetireAspirePublicAssertions.CompleteAsync(seed, seed.FirstRequest, expected, token);
        cuts = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, arm, original.Operation.Id,
            token, expected.InstalledReceipt);
        state = await ControlledRetireAspireModels.NativeAsync(seed, state, cuts);
        await ControlledRetireAspireSeed.ReplayAsync(seed, state, token);
        await ControlledRetireAspirePublicAssertions.HealthyReplayAsync(seed, healthy, token);
    }
}
