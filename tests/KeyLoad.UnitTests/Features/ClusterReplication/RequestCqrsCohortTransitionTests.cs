using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
internal sealed class RequestCqrsCohortTransitionTests(RequestCqrsCohortRuntimeFixture runtime)
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(20);
    private const string IncompatibleDetail = ReplicaTransportProtocol.IncompatibleCohort;
    private const string InvalidDiscoveryDetail = ReplicaTransportProtocol.InvalidDiscovery;

    [Test]
    public async Task AuthenticatedCacheTransitionsExpireAndAdmitOneCompatibleRemote()
    {
        using var deadline = new CancellationTokenSource(TestBound);
        await using var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        await scenario.RemoteTwo.StopListeningAsync(deadline.Token);
        await scenario.Client.EnsureCompatibleCohortAsync(deadline.Token);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsTrue();
        await Assert.That(scenario.RemoteOne.Requests).IsGreaterThan(0);
        await VerifySubstitutedBytesAreNotCachedAsync(scenario, runtime.RuntimeAddress(1), deadline.Token);
        await VerifySignedWrongIdentityIsNotCachedAsync(scenario, runtime.RuntimeAddress(1), deadline.Token);
        await VerifyMismatchReplacementAsync(scenario, deadline.Token);

        scenario.RemoteOne.SetUnavailable();
        var beforeFailedRefresh = scenario.RemoteOne.Requests;
        var unavailable = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, deadline.Token));
        await AssertFailureAsync(unavailable, InvalidDiscoveryDetail);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(beforeFailedRefresh + 1);
        var beforeOrdinaryRetry = scenario.RemoteOne.Requests;
        var removedPositive = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, false, deadline.Token));
        await AssertFailureAsync(removedPositive, InvalidDiscoveryDetail);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(beforeOrdinaryRetry + 1);

        scenario.PublishCompatibleRemoteOne(1);
        var firstAddress = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, deadline.Token);
        await Assert.That(firstAddress).IsEqualTo(SiloAddress.FromParsableString(runtime.RuntimeAddress(1)));
        await Task.Delay(RequestCqrsCohortScenario.LowerElectionTimeout + TimeSpan.FromMilliseconds(50), deadline.Token);
        scenario.PublishCompatibleRemoteOne(2);
        var beforeExpiryRefresh = scenario.RemoteOne.Requests;
        var expiredAddress = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, false, deadline.Token);
        await Assert.That(expiredAddress).IsEqualTo(SiloAddress.FromParsableString(runtime.RuntimeAddress(2)));
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(beforeExpiryRefresh + 1);
    }

    [Test]
    public async Task ReachableAuthenticatedThirdMismatchRejectsAnExistingMajority()
    {
        using var deadline = new CancellationTokenSource(TestBound);
        await using var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        scenario.PublishRemoteTwo(2, GrainRoutingProtocol.RequestInterfaceVersion - 1,
            ReplicaTransportProtocol.Version);

        var mismatch = await Assert.ThrowsExactlyAsync<KeyLoadException>(
            () => scenario.Client.EnsureCompatibleCohortAsync(deadline.Token));
        await AssertFailureAsync(mismatch, IncompatibleDetail);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(1);
        await Assert.That(scenario.RemoteTwo.Requests).IsEqualTo(1);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsFalse();

        var cachedMismatch = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.SecondRemote, false, deadline.Token));
        await AssertFailureAsync(cachedMismatch, IncompatibleDetail);
        await Assert.That(scenario.RemoteTwo.Requests).IsEqualTo(1);
    }

    private static async Task VerifySubstitutedBytesAreNotCachedAsync(
        RequestCqrsCohortScenario scenario, string expectedAddress, CancellationToken cancellationToken)
    {
        scenario.TamperRemoteOnePayload();
        var beforeTampered = scenario.RemoteOne.Requests;
        var tampered = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, cancellationToken));
        var observed = tampered ?? throw new InvalidOperationException("The tampered discovery was accepted.");
        await Assert.That(observed.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(observed.Message).IsEqualTo(InvalidDiscoveryDetail);
        scenario.PublishCompatibleRemoteOne(1);
        var beforeRecovery = scenario.RemoteOne.Requests;
        var address = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, false, cancellationToken);
        await Assert.That(address).IsEqualTo(SiloAddress.FromParsableString(expectedAddress));
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(beforeRecovery + 1);
        await Assert.That(beforeRecovery).IsEqualTo(beforeTampered + 1);
    }

    private static async Task VerifySignedWrongIdentityIsNotCachedAsync(
        RequestCqrsCohortScenario scenario, string expectedAddress, CancellationToken cancellationToken)
    {
        scenario.PublishRemoteOneRecord(scenario.Discovery(RequestCqrsCohortScenario.SecondRemote, 1));
        var beforeWrongIdentity = scenario.RemoteOne.Requests;
        var wrongIdentity = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, cancellationToken));
        await AssertFailureAsync(wrongIdentity, InvalidDiscoveryDetail);
        scenario.PublishCompatibleRemoteOne(1);
        var beforeValidRefresh = scenario.RemoteOne.Requests;
        var resolved = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, false, cancellationToken);
        await Assert.That(resolved).IsEqualTo(SiloAddress.FromParsableString(expectedAddress));
        await Assert.That(beforeValidRefresh).IsEqualTo(beforeWrongIdentity + 1);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(beforeValidRefresh + 1);
    }

    private async Task VerifyMismatchReplacementAsync(RequestCqrsCohortScenario scenario,
        CancellationToken cancellationToken)
    {
        await VerifySignedMismatchAsync(scenario,
            GrainRoutingProtocol.RequestInterfaceVersion - 1, ReplicaTransportProtocol.Version, cancellationToken);
        await VerifySignedMismatchAsync(scenario,
            GrainRoutingProtocol.RequestInterfaceVersion, ReplicaTransportProtocol.Version - 1, cancellationToken);

        scenario.PublishCompatibleRemoteOne(2);
        var replacement = await scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, cancellationToken);
        await Assert.That(replacement).IsEqualTo(SiloAddress.FromParsableString(runtime.RuntimeAddress(2)));
        await Assert.That(scenario.Client.HasCompatibleCohort).IsTrue();
    }

    private static async Task VerifySignedMismatchAsync(RequestCqrsCohortScenario scenario,
        int requestVersion, int envelopeVersion, CancellationToken cancellationToken)
    {
        scenario.PublishRemoteOne(1, requestVersion, envelopeVersion);
        var mismatch = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, cancellationToken));
        await AssertFailureAsync(mismatch, IncompatibleDetail);
        var mismatchRequests = scenario.RemoteOne.Requests;
        var cachedMismatch = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, false, cancellationToken));
        await AssertFailureAsync(cachedMismatch, IncompatibleDetail);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(mismatchRequests);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsFalse();
    }

    private static async Task AssertFailureAsync(KeyLoadException? failure, string detail)
    {
        var observed = failure ?? throw new InvalidOperationException("The expected discovery failure was absent.");
        await Assert.That(observed.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(observed.Message).IsEqualTo(detail);
    }
}
