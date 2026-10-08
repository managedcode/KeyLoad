using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledPartitionMovementGrantTests
{
    private const int PeerKeyBytes = 32;
    private const string MissingObservation = "The native movement phase did not return its original observation.";

    [Test]
    public async Task ActualPersistedGrantFencesSourceAndRetainsOriginalMixedReceiptAcrossColdReopen()
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
                (string[] Source, string[] Target, long Position, long Index,
                    PartitionMovePhaseResult Prepared, DateTimeOffset ExpiresAt)? observed = null;
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var admission = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                        runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership, runtime.PartitionMovement,
                        source.Database.EvaluationClock);
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        observed = await ControlledPartitionMovementGrantScenario.ExecuteAsync(source, target,
                            corpus, runtime, admission, ControlledPartitionMovementPrepareRequest.CallerAddress(listeners),
                            seeded.Receipt, initialPosition, token);
                    }, failures);
                }, failures);
                ServerFailureObserver.ThrowIfAny(failures);
                var captured = observed ?? throw new InvalidOperationException(MissingObservation);
                source.Reopen();
                target.Reopen();
                await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, captured.Source,
                    captured.Target, captured.Position, captured.Index);
                await seeded.Authority.RemainsControlOwnedAsync(source, target);
                await ControlledPartitionMovementBlobAssertions.ReadAsync(source, seeded.Blob, token);
                await ControlledPartitionMovementModelAssertions.ReadAsync(source, seeded.RecordedAt, captured.Position, token);
                await ControlledMovementConfiguredFenceAssertions.WriteBlockedAsync(source, seeded.RecordedAt, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
