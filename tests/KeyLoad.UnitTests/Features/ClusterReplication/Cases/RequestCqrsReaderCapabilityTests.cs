using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class RequestCqrsReaderCapabilityTests(RequestCqrsCohortRuntimeFixture runtime)
{
    private const int UnknownReaderCapability = int.MaxValue;
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(20);

    [Test]
    public async Task ZeroAndUnknownReaderCapabilitiesRejectAndRecoverWithHealthyVoter()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await using var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        await VerifyNonCurrentMarkerAsync(scenario, StoreReaderContract.Unspecified, deadline.Token);
        await VerifyNonCurrentMarkerAsync(scenario, UnknownReaderCapability, deadline.Token);
    }

    [Test]
    public async Task ReachableNotReadyThirdVoterWithUnsupportedReaderRejectsMajority()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await using var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        var unsupported = scenario.Discovery(RequestCqrsCohortScenario.SecondRemote, 2,
            runtimeJournalReaderContract: StoreReaderContract.Unspecified)
        with
        { TransportReady = false };
        scenario.PublishRemoteTwoRecord(unsupported);

        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(
            () => scenario.Client.EnsureCompatibleCohortAsync(deadline.Token));
        await RequestCqrsReaderCapabilityAssertions.AssertIncompatibleAsync(failure);
        await Assert.That(scenario.RemoteOne.Requests).IsGreaterThan(0);
        await Assert.That(scenario.RemoteTwo.Requests).IsGreaterThan(0);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsFalse();
        var readsAfterAdmission = scenario.RemoteTwo.Requests;
        var cachedFailure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.SecondRemote, false, deadline.Token));
        await RequestCqrsReaderCapabilityAssertions.AssertIncompatibleAsync(cachedFailure);
        await Assert.That(scenario.RemoteTwo.Requests).IsEqualTo(readsAfterAdmission);

        scenario.PublishRemoteTwoRecord(scenario.Discovery(RequestCqrsCohortScenario.SecondRemote, 2,
            runtimeJournalReaderContract: StoreReaderContract.RuntimeJournal));
        var healthy = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.SecondRemote, true, deadline.Token);
        await Assert.That(healthy).IsEqualTo(SiloAddress.FromParsableString(runtime.RuntimeAddress(2)));
        await scenario.Client.EnsureCompatibleCohortAsync(deadline.Token);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsTrue();
    }

    private static async Task VerifyNonCurrentMarkerAsync(RequestCqrsCohortScenario scenario,
        int readerCapability, CancellationToken cancellationToken)
    {
        scenario.PublishRemoteOneRecord(scenario.Discovery(RequestCqrsCohortScenario.FirstRemote, 1,
            runtimeJournalReaderContract: readerCapability));
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(
            () => scenario.Client.EnsureCompatibleCohortAsync(cancellationToken));
        await RequestCqrsReaderCapabilityAssertions.AssertIncompatibleAsync(failure);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsFalse();
        var readsAfterAdmission = scenario.RemoteOne.Requests;
        var cachedFailure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, false, cancellationToken));
        await RequestCqrsReaderCapabilityAssertions.AssertIncompatibleAsync(cachedFailure);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(readsAfterAdmission);

        scenario.PublishCompatibleRemoteOne(1);
        var healthy = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, cancellationToken);
        await Assert.That(healthy).IsEqualTo(SiloAddress.FromParsableString(scenario.RuntimeAddress(1)));
        await scenario.Client.EnsureCompatibleCohortAsync(cancellationToken);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsTrue();
    }
}
