using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PartitionMovementCleanupReadBudgetRf3Trial
{
    private readonly List<Exception> failures = [];
    private readonly List<PartitionMovementPublicParentRf3Seed> seeds = [];
    private TwoRf3MembershipWave? wave;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var trial = new PartitionMovementCleanupReadBudgetRf3Trial();
        await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(caller.Token), trial.failures);
        foreach (var seed in trial.seeds)
        {
            await ServerFailureObserver.ObserveAsync(() => seed.DisposeAsync().AsTask(), trial.failures);
        }
        if (trial.wave is { } resources)
        {
            await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), trial.failures);
        }
        ServerFailureObserver.ThrowIfAny(trial.failures);
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        var owned = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(token);
        wave = owned;
        var refused = await PartitionMovementPublicParentRf3Seed.CreateAsync(owned, token);
        seeds.Add(refused);
        var first = await PartitionMovementPublicParentRf3Seed.CreateSiblingAsync(owned, refused, token);
        seeds.Add(first);
        var second = await PartitionMovementPublicParentRf3Seed.CreateSiblingAsync(owned, refused, token);
        seeds.Add(second);
        await Assert.That(seeds.Select(seed => seed.Partition).Distinct().Count()).IsEqualTo(seeds.Count);
        await Assert.That(seeds.Select(seed => seed.FirstRequest.MoveId).Distinct().Count()).IsEqualTo(seeds.Count);
        await PartitionMovementFinalInstallFrameRf3Producer.HoldPreFinalAsync(owned, refused, token);
        var prepared = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(owned, refused.FirstRequest, token);
        var denied = await PartitionMovementCleanupReadBudgetRf3Assertions.LimitAsync(refused, prepared);
        await owned.capacity.ReconfigureMovementTargetReadBudgetAsync(owned, denied, token);
        await ReconnectAsync(owned, token);
        await PartitionMovementCleanupReadBudgetRf3Calls.RefusalsAsync(refused, token);
        var failed = await PartitionMovementCleanupReadBudgetRf3Cold.CaptureAsync(owned, refused, token);
        await PartitionMovementCleanupReadBudgetRf3Assertions.FailureAsync(refused, prepared, failed.Cuts);
        await owned.capacity.ReconfigureMovementTargetReadBudgetAsync(owned, new DatabaseLimits().MaxQueryReadBytes, token);
        await ReconnectAsync(owned, token);
        await PartitionMovementCleanupReadBudgetRf3Calls.RefusalsAsync(refused, token);
        await PartitionMovementCleanupReadBudgetRf3Cold.ReplayAsync(owned, refused, failed, seeds, token);
        await PartitionMovementCleanupReadBudgetRf3Cold.ReplayAsync(owned, refused, failed, seeds, token);
        await PhysicalShardPartitionMovementRf3Flow.RunAsync(owned, [first, second], token);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(refused, token);
    }

    private async Task ReconnectAsync(TwoRf3MembershipWave owned, CancellationToken token)
    {
        foreach (var seed in seeds)
        {
            await seed.ReconnectAsync(owned, token);
        }
    }
}
