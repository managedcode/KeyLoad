using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class RequestCqrsColdStartupAcquisitionTests(RequestCqrsCohortRuntimeFixture runtime)
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(20);

    [Test]
    public async Task ColdFollowerAcquiresItsOwnSignedCurrentCohortWithoutUnrelatedOutboundWork()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await RequestCqrsCohortObservationOwner.RunAsync(runtime, ColdAsync, deadline.Token);
    }

    private static async Task ColdAsync(RequestCqrsCohortScenario scenario, CancellationToken token)
    {
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.InsufficientFreshReady, 2, 0, 2, 0, 1);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(0);
        await Assert.That(scenario.RemoteTwo.Requests).IsEqualTo(0);
        var acquired = await scenario.Client.AcquireCompatibleCohortAsync(token);
        await Assert.That(acquired.Code).IsEqualTo(ReplicaCohortAdmissionCode.Compatible);
        await Assert.That(acquired.Ready).IsEqualTo(3);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(1);
        await Assert.That(scenario.RemoteTwo.Requests).IsEqualTo(1);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.Compatible, 2, 2, 0, 0, 3);
        await RequestCqrsCohortObservationAssertions.ResolveAsync(scenario,
            scenario.Discovery(RequestCqrsCohortScenario.FirstRemote, 1), token);
        await RequestCqrsCohortObservationAssertions.ResolveAsync(scenario,
            scenario.Discovery(RequestCqrsCohortScenario.SecondRemote, 2), token);
    }

    [Test]
    public async Task OriginalUnavailableAndIncompatibleRefusalsAndCallerCancellationPrecedeHealthyAdmission()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await RequestCqrsCohortObservationOwner.RunAsync(runtime, RefusalAndRepairAsync, deadline.Token);
    }

    private static async Task RefusalAndRepairAsync(RequestCqrsCohortScenario scenario, CancellationToken token)
    {
        var first = scenario.Discovery(RequestCqrsCohortScenario.FirstRemote, 1);
        var second = scenario.Discovery(RequestCqrsCohortScenario.SecondRemote, 2);
        scenario.RemoteOne.SetUnavailable();
        scenario.RemoteTwo.SetUnavailable();
        await RequestCqrsCohortObservationAssertions.RefuseAdmissionAsync(scenario, ReplicaProtocol.NoLeader, token);
        var absent = await scenario.Client.AcquireCompatibleCohortAsync(token);
        await Assert.That(absent.Code).IsEqualTo(ReplicaCohortAdmissionCode.InsufficientFreshReady);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.InsufficientFreshReady, 2, 0, 2, 0, 1);
        scenario.PublishRemoteOneRecord(first);
        var majority = await scenario.Client.AcquireCompatibleCohortAsync(token);
        await Assert.That(majority.Code).IsEqualTo(ReplicaCohortAdmissionCode.Compatible);
        await Assert.That(majority.Ready).IsEqualTo(2);
        scenario.PublishRemoteTwoRecord(second with { ApplicationRpcVersion = GrainRoutingProtocol.RequestInterfaceVersion - 1 });
        var original = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => scenario.Client.AcquireCompatibleCohortAsync(token));
        var refused = original ?? throw new InvalidOperationException("The original incompatible cohort was admitted.");
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(refused.Message).IsEqualTo(ReplicaTransportProtocol.IncompatibleCohort);
        scenario.PublishRemoteTwoRecord(second);
        await RequestCqrsCohortObservationAssertions.ResolveAsync(scenario, second, token);
        await RequireCancellationThenHealthyAsync(scenario, token);
    }

    private static async Task RequireCancellationThenHealthyAsync(RequestCqrsCohortScenario scenario, CancellationToken token)
    {
        var firstRequests = scenario.RemoteOne.Requests;
        var secondRequests = scenario.RemoteTwo.Requests;
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => scenario.Client.AcquireCompatibleCohortAsync(cancelled.Token));
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(firstRequests);
        await Assert.That(scenario.RemoteTwo.Requests).IsEqualTo(secondRequests);
        var healthy = await scenario.Client.AcquireCompatibleCohortAsync(token);
        await Assert.That(healthy.Code).IsEqualTo(ReplicaCohortAdmissionCode.Compatible);
        await RequestCqrsCohortObservationAssertions.RequireAsync(scenario,
            ReplicaCohortAdmissionCode.Compatible, 2, 2, 0, 0, 3);
    }
}
