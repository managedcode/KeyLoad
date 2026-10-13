using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains original Prepare operations through the real six-owner native transport and two cold cuts.</summary>
internal sealed class ControlledPartitionMovementNativePrepareTests
{
    private const int ColdCuts = 2;
    private const int FirstColdCut = 0;

    [Test]
    public async Task ActualNativePrepareRefusesInvalidProofThenRetainsMixedReceiptConflictAndTwoColdOwners()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owners = await ControlledPartitionMovementNativeHosts.CreateAsync(
                TestContext.Current!.Execution.CancellationToken);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var cancellation = TestContext.Current!.Execution.CancellationToken;
                await ControlledPartitionMovementNativeRegistration.VerifyAsync(owners);
                var seed = await ControlledPartitionMovementNativeSeed.ExecuteAsync(owners.Source, cancellation);
                var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(owners.Source,
                    ControlledPartitionMovementCorpus.SeedId, cancellation);
                var operation = entry.Operation ?? throw new InvalidOperationException("The original seed entry has no operation.");
                var expectedSeed = await ControlledPartitionMovementNativeSeed.ReceiptAsync(owners.Source, seed, entry);
                await ControlledPartitionMovementNativeModels.ReadAsync(owners.Source, operation.EvaluatedAt, cancellation);
                var blob = await ControlledPartitionMovementNativeBlob.PublishAsync(owners.Source, cancellation);
                var prepared = await ControlledPartitionMovementNativePrepareFlow.ExecuteAsync(owners, cancellation);
                await ControlledPartitionMovementNativeNonce.ProveAsync(owners, prepared.Original, prepared.Phase, cancellation);
                CommitReceipt? healthy = null;
                for (var cold = FirstColdCut; cold < ColdCuts; cold++)
                {
                    await owners.RestartAsync(cancellation);
                    await ControlledPartitionMovementNativeRegistration.VerifyAsync(owners);
                    await ControlledPartitionMovementNativePrepareFlow.ReadOutcomeAsync(owners,
                        prepared.Original, prepared.Phase, cancellation);
                    var replay = await owners.Source.SubmitAsync(OperationKind.Batch, ControlledPartitionMovementCorpus.SeedId,
                        PhysicalShardCatalogFixture.RootPrincipalId,
                        NativeSerialization.Serialize(ControlledPartitionMovementCorpus.Seed()), cancellation);
                    await ControlledPartitionMovementNativeSeed.ReceiptAsync(replay, expectedSeed);
                    await ControlledPartitionMovementNativeBlobAssertions.ReplayAsync(owners.Source,
                        blob.Request, blob.Expected, cancellation);
                    await ControlledPartitionMovementNativeModels.ReadAsync(owners.Source, operation.EvaluatedAt, cancellation);
                    if (cold == FirstColdCut)
                    { healthy = await ControlledPartitionMovementNativeHealthy.SubmitAsync(owners.Source, cancellation); }
                    else
                    {
                        await ControlledPartitionMovementNativeHealthy.ReplayAsync(owners.Source,
                        healthy ?? throw new InvalidOperationException("The original healthy receipt is missing."), cancellation);
                    }
                }
            }, failures);
            if (failures.Count != 0)
            { owners.RetainRoots(); }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
