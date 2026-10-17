using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PartitionMovementNativeOutcomeCodecOwner
{
    private const int PeerKeyBytes = 32;

    internal static async Task ExecuteAsync(CancellationToken token)
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
                var seeded = await ControlledPartitionMovementPrepareSeed.ExecuteAsync(source, target, corpus, token);
                var runtime = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus,
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes)),
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes)), control: true);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var admission = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                        runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership, runtime.PartitionMovement,
                        source.Database.EvaluationClock);
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        await PartitionMovementNativeOutcomeCodecFlow.ExecuteAsync(source, target, corpus,
                            runtime, admission, ControlledPartitionMovementPrepareRequest.CallerAddress(listeners), token);
                    }, failures);
                }, failures);
                ServerFailureObserver.ThrowIfAny(failures);
                var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
                var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
                var position = source.Store.Position;
                var index = source.Journal.Log.State.LastIndex;
                source.Reopen();
                target.Reopen();
                await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target,
                    sourceImage, targetImage, position, index);
                await seeded.Authority.RemainsControlOwnedAsync(source, target);
                await ControlledPartitionMovementBlobAssertions.ReadAsync(source, seeded.Blob, token);
                await ControlledPartitionMovementModelAssertions.ReadAsync(source, seeded.RecordedAt, position, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
