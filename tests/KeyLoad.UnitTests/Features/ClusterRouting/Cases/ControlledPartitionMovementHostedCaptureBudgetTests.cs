using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises actual signed outer replies at eight KiB and retains original receipts through two cold native cohorts.</summary>
internal sealed class ControlledPartitionMovementHostedCaptureBudgetTests
{
    private const int EightKiB = 8192;
    private const int ColdCuts = 2;
    private const int FirstNode = 0;
    private const int FirstColdCut = 0;

    [Test]
    public async Task ActualEightKiBHostedOuterRepliesRefuseBeforeEffectsThenCaptureAllPagesReleaseAndRetainTwoColdReceipts()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owners = await ControlledPartitionMovementNativeHosts.CreateWithMaximumBatchBytesAsync(EightKiB, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ControlledPartitionMovementNativeRegistration.VerifyAsync(owners);
                foreach (var node in owners.Nodes)
                {
                    await Assert.That(node.Database.Limits.MaxBatchBytes).IsEqualTo(EightKiB);
                }
                var input = owners.CaptureInput();
                var restored = ControlledPartitionMovementNativeHostSettings.Restore(input, owners.Root).CaptureInput();
                for (var node = FirstNode; node < input.Arguments.Length; node++)
                {
                    await Assert.That(restored.Arguments[node].SequenceEqual(input.Arguments[node])).IsTrue();
                }
                var seed = await ControlledPartitionMovementNativeSeed.ExecuteAsync(owners.Source, token);
                var seedEntry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(owners.Source,
                    ControlledPartitionMovementCorpus.SeedId, token);
                var expectedSeed = await ControlledPartitionMovementNativeSeed.ReceiptAsync(owners.Source, seed, seedEntry);
                var seedOperation = seedEntry.Operation ?? throw new InvalidOperationException(nameof(seedEntry.Operation));
                await ControlledPartitionMovementNativeModels.ReadAsync(owners.Source, seedOperation.EvaluatedAt, token);
                var blob = await ControlledPartitionMovementNativeBlob.PublishAsync(owners.Source, token);
                var prepared = await ControlledPartitionMovementNativePrepareFlow.ExecuteAsync(owners, token);
                var settled = await ControlledPartitionMovementNativeHostedCapture.ExecuteAsync(owners, prepared.Phase, token);
                var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(owners.Source,
                    ControlledPartitionMovementCaptureRequest.CaptureCommandId, token);
                var operation = entry.Operation ?? throw new InvalidOperationException(nameof(entry.Operation));
                for (var cold = FirstColdCut; cold < ColdCuts; cold++)
                {
                    await owners.RestartAsync(token);
                    await ControlledPartitionMovementNativeRegistration.VerifyAsync(owners);
                    foreach (var node in owners.Nodes)
                    {
                        await Assert.That(node.Database.Limits.MaxBatchBytes).IsEqualTo(EightKiB);
                    }
                    var outcome = owners.Source.Database.ResolveOutcome(operation);
                    await Assert.That(outcome.Error).IsNull();
                    await Assert.That(outcome.SafeDetail).IsNull();
                    await Assert.That(JsonDefaults.Serialize(outcome.Get<PartitionMovePhaseResult>()).AsSpan()
                        .SequenceEqual(JsonDefaults.Serialize(settled))).IsTrue();
                    await Assert.That(entry.Index).IsEqualTo(settled.Journal.AppliedPosition);
                    var replay = await owners.Source.SubmitAsync(OperationKind.Batch, ControlledPartitionMovementCorpus.SeedId,
                        PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(ControlledPartitionMovementCorpus.Seed()), token);
                    await ControlledPartitionMovementNativeSeed.ReceiptAsync(replay, expectedSeed);
                    await ControlledPartitionMovementNativeBlobAssertions.ReplayAsync(owners.Source, blob.Request, blob.Expected, token);
                    await ControlledPartitionMovementNativeModels.ReadAsync(owners.Source, seedOperation.EvaluatedAt, token);
                }
            }, failures);
            if (failures.Count != 0)
            {
                owners.RetainRoots();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
