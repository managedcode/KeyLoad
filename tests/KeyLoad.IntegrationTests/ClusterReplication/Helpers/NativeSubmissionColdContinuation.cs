using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using P = KeyLoad.IntegrationTests.Features.ClusterReplication.NativeSubmissionColdProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionColdContinuation
{
    internal static async Task RunAsync(NativeSubmissionFaultRf3Owner owner, CommandRequest original,
        RequestCqrsFaultRetryResult originalRetry, IReadOnlyList<NodeEpochRf3NodeObservation> originalTopology,
        CancellationToken token)
    {
        var profile = owner.Profile ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var prior = owner.Discovery;
        CommandRequest? healthy = null;
        CommitReceipt? healthyReceipt = null;
        AtomicPartitionPlacementResolution? placement = null;
        for (var cold = P.FirstCold; cold < P.ColdOwners; cold++)
        {
            await owner.ResumeAfterInspectionAsync(token).ConfigureAwait(false);
            var wave = owner.Wave ?? throw NativeSubmissionFaultRf3Owner.Missing();
            var current = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, token).ConfigureAwait(false);
            await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(originalTopology, current).ConfigureAwait(false);
            var discovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(wave.App, profile, token).ConfigureAwait(false);
            await RequestCqrsRf3DiscoveryOracle.AssertReplacementAsync(prior, discovery).ConfigureAwait(false);
            owner.Discovery = discovery;
            prior = discovery;
            await owner.ConnectCallerAsync(NativeSubmissionFaultRf3Assertions.Leader(current).Name, token).ConfigureAwait(false);
            var caller = owner.Caller ?? throw NativeSubmissionFaultRf3Owner.Missing();
            var admin = owner.Administrator ?? throw NativeSubmissionFaultRf3Owner.Missing();
            await caller.AssertFreshRoutingReadyAsync(token).ConfigureAwait(false);
            await NativeSubmissionColdAssertions.OriginalAsync(caller, original, originalRetry, token).ConfigureAwait(false);
            await NativeSubmissionColdAssertions.CurrentAuthorityAsync(caller, original.Partition, token).ConfigureAwait(false);
            var currentPlacement = await NativeSubmissionColdAssertions.PlacementAsync(admin, original, originalRetry.SdkReceipt, token)
                .ConfigureAwait(false);
            if (placement is not null)
            {
                await Assert.That(JsonDefaults.Serialize(currentPlacement).SequenceEqual(JsonDefaults.Serialize(placement))).IsTrue();
            }
            placement = currentPlacement;
            if (cold == P.FirstCold)
            {
                healthy = new(Guid.NewGuid(), original.Partition,
                    [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, P.HealthyDocument, P.HealthyJson, P.AbsentRevision)]);
                healthyReceipt = await NativeSubmissionColdHealthy.CommitAsync(caller, admin, healthy, originalRetry.SdkReceipt, placement, token)
                    .ConfigureAwait(false);
            }
            var savedHealthy = healthyReceipt ?? throw NativeSubmissionFaultRf3Owner.Missing();
            await NativeSubmissionColdAssertions.ReplayAsync(caller, healthy ?? throw NativeSubmissionFaultRf3Owner.Missing(),
                savedHealthy, token).ConfigureAwait(false);
            await RequestCqrsFaultReplayContinuation.DocumentAsync(caller,
                new(original.Partition, RequestCqrsRf3Protocol.AdminCollection, P.HealthyDocument), P.HealthyRevision, P.HealthyJson, token)
                .ConfigureAwait(false);
            await NativeSubmissionColdAssertions.OriginalAsync(caller, original, originalRetry, token).ConfigureAwait(false);
            await NodeEpochRf3StatusOracle.EventuallyCaughtUpAsync(wave.App, profile, savedHealthy.Token.Position, token)
                .ConfigureAwait(false);
            await owner.StopForOutcomeInspectionAsync().ConfigureAwait(false);
        }
    }
}
