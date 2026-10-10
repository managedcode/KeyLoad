using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class RequestCqrsCohortObservationWholeFlowTests(RequestCqrsCohortRuntimeFixture runtime)
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(20);

    [Test]
    public async Task SignedReadinessRefusalPreservesOriginalIdentityThenFreshMajorityAdmits()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await RequestCqrsCohortObservationOwner.RunAsync(runtime, ReadinessAsync, deadline.Token);
    }

    private static async Task ReadinessAsync(RequestCqrsCohortScenario scenario, CancellationToken token)
    {
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.InsufficientFreshReady, 2, 0, 2, 0, 1);
        var first = scenario.Discovery(RequestCqrsCohortScenario.FirstRemote, 1);
        var second = scenario.Discovery(RequestCqrsCohortScenario.SecondRemote, 2);
        await scenario.Client.EnsureCompatibleCohortAsync(token);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.Compatible, 2, 2, 0, 0, 3);
        scenario.PublishRemoteOneRecord(first with { TransportReady = false });
        scenario.PublishRemoteTwoRecord(second with { TransportReady = false });
        await RequestCqrsCohortObservationAssertions.RefuseResolutionAsync(scenario,
            RequestCqrsCohortScenario.FirstRemote, ReplicaTransportProtocol.InvalidDiscovery, token);
        await RequestCqrsCohortObservationAssertions.RefuseResolutionAsync(scenario,
            RequestCqrsCohortScenario.SecondRemote, ReplicaTransportProtocol.InvalidDiscovery, token);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.InsufficientFreshReady, 2, 2, 0, 0, 1);
        await RequestCqrsCohortObservationAssertions.RefuseAdmissionAsync(scenario,
            ReplicaProtocol.NoLeader, token);
        await RequestCqrsCohortObservationAssertions.RepairAsync(scenario, first, second, token);
    }

    [Test]
    public async Task SignedFirstInvalidPeerOverridesReadyMajorityThenExactRecordsRepairAdmission()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await RequestCqrsCohortObservationOwner.RunAsync(runtime, FirstInvalidPeerAsync, deadline.Token);
    }

    private static async Task FirstInvalidPeerAsync(RequestCqrsCohortScenario scenario, CancellationToken token)
    {
        var first = scenario.Discovery(RequestCqrsCohortScenario.FirstRemote, 1);
        var second = scenario.Discovery(RequestCqrsCohortScenario.SecondRemote, 2);
        await RequestCqrsCohortObservationAssertions.ResolveAsync(scenario, first, token);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.Compatible, 2, 1, 1, 0, 2);
        scenario.PublishRemoteTwoRecord(second with { RuntimeJournalReaderContract = StoreReaderContract.Unspecified });
        await RequestCqrsCohortObservationAssertions.RefuseResolutionAsync(scenario,
            RequestCqrsCohortScenario.SecondRemote, ReplicaTransportProtocol.IncompatibleCohort, token);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.PeerReaderMismatch, 2, 2, 0, 0, 2);
        await RequestCqrsCohortObservationAssertions.RefuseAdmissionAsync(scenario,
            ReplicaTransportProtocol.IncompatibleCohort, token);
        scenario.PublishRemoteOneRecord(first with { ApplicationRpcVersion = GrainRoutingProtocol.RequestInterfaceVersion - 1 });
        await RequestCqrsCohortObservationAssertions.RefuseResolutionAsync(scenario,
            RequestCqrsCohortScenario.FirstRemote, ReplicaTransportProtocol.IncompatibleCohort, token);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.PeerProtocolMismatch, 1, 1, 0, 0, 1);
        await RequestCqrsCohortObservationAssertions.RefuseAdmissionAsync(scenario,
            ReplicaTransportProtocol.IncompatibleCohort, token);
        await RequestCqrsCohortObservationAssertions.RepairAsync(scenario, first, second, token);
    }

    [Test]
    public async Task OriginalSignedCacheNaturallyExpiresThenUnavailableRefusalAndFreshHealthyAdmission()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await RequestCqrsCohortObservationOwner.RunAsync(runtime, ExpiryAsync, deadline.Token);
    }

    private static async Task ExpiryAsync(RequestCqrsCohortScenario scenario, CancellationToken token)
    {
        var first = scenario.Discovery(RequestCqrsCohortScenario.FirstRemote, 1);
        var second = scenario.Discovery(RequestCqrsCohortScenario.SecondRemote, 2);
        await scenario.Client.EnsureCompatibleCohortAsync(token);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.Compatible, 2, 2, 0, 0, 3);
        scenario.RemoteOne.SetUnavailable();
        scenario.RemoteTwo.SetUnavailable();
        await Task.Delay(scenario.Configuration.LowerElectionTimeout, TimeProvider.System, token);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.InsufficientFreshReady, 2, 0, 0, 2, 1);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.InsufficientFreshReady, 2, 0, 2, 0, 1);
        var firstRequests = scenario.RemoteOne.Requests;
        var secondRequests = scenario.RemoteTwo.Requests;
        await RequestCqrsCohortObservationAssertions.RefuseAdmissionAsync(scenario,
            ReplicaProtocol.NoLeader, token);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(firstRequests + 1);
        await Assert.That(scenario.RemoteTwo.Requests).IsEqualTo(secondRequests + 1);
        await RequestCqrsCohortObservationAssertions.RepairAsync(scenario, first, second, token);
    }
}
