using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementCaptureTests
{
    private const int PeerKeyBytes = 32;
    private const long CapturedNativeIndex = 19;

    [Test]
    public async Task AuthenticatedNativeCapturePagesSettleOriginalReceiptAndReopenCapturedControl()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var listeners = new ControlledPartitionMovementLoopbackListeners();
            var corpus = new ControlledPartitionMovementLoopbackCorpus(listeners);
            using var source = new ControlledPartitionMovementNode(corpus.Control.Owner);
            using var target = new ControlledPartitionMovementNode(corpus.Destination.Owner);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var token = TestContext.Current!.Execution.CancellationToken;
                var initialPosition = source.Store.Position;
                var seeded = await ControlledPartitionMovementPrepareSeed.ExecuteAsync(source, target, corpus, token);
                var runtime = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus,
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes)),
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes)), control: true);
                string[] sourceImage = [];
                string[] targetImage = [];
                var expectedPosition = checked(initialPosition + CapturedNativeIndex);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var admission = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                        runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership, runtime.PartitionMovement,
                        source.Database.EvaluationClock);
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        await ControlledPartitionMovementCaptureScenario.ExecuteAsync(source, target,
                            corpus, runtime, admission, ControlledPartitionMovementPrepareRequest.CallerAddress(listeners),
                            seeded.Receipt, initialPosition, token);
                        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(CapturedNativeIndex);
                        await Assert.That(source.Store.Position).IsEqualTo(expectedPosition);
                        sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
                        targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
                    }, failures);
                }, failures);
                ServerFailureObserver.ThrowIfAny(failures);
                source.Reopen();
                target.Reopen();
                await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, sourceImage,
                    targetImage, expectedPosition, CapturedNativeIndex);
                await seeded.Authority.RemainsControlOwnedAsync(source, target);
                await ControlledPartitionMovementBlobAssertions.ReadAsync(source, seeded.Blob, token);
                await ControlledPartitionMovementModelAssertions.ReadAsync(source, seeded.RecordedAt, expectedPosition, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
