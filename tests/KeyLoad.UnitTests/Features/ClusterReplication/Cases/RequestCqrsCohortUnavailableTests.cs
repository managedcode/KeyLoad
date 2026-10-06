using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class RequestCqrsCohortUnavailableTests(RequestCqrsCohortRuntimeFixture runtime)
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(20);

    [Test]
    public async Task StoppedSocketsRemoveFreshObservationsAndRequireACompatibleMajority()
    {
        using var deadline = new CancellationTokenSource(TestBound, TimeProvider.System);
        await using (var unavailable = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token))
        {
            await unavailable.Client.EnsureCompatibleCohortAsync(deadline.Token);
            await Assert.That(unavailable.Client.HasCompatibleCohort).IsTrue();

            await unavailable.RemoteTwo.StopListeningAsync(deadline.Token);
            var missingThird = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                unavailable.Client.ResolveAsync(RequestCqrsCohortScenario.SecondRemote, true, deadline.Token));
            await AssertUnavailableAsync(missingThird);
            await Assert.That(unavailable.Client.HasCompatibleCohort).IsTrue();
            await unavailable.Client.EnsureCompatibleCohortAsync(deadline.Token);
            var survivingAddress = await unavailable.Client.ResolveAsync(
                RequestCqrsCohortScenario.FirstRemote, true, deadline.Token);
            await Assert.That(survivingAddress).IsEqualTo(
                SiloAddress.FromParsableString(runtime.RuntimeAddress(1)));
            await Assert.That(unavailable.Client.HasCompatibleCohort).IsTrue();

            await unavailable.RemoteOne.StopListeningAsync(deadline.Token);
            var failedRefresh = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                unavailable.Client.ResolveAsync(RequestCqrsCohortScenario.FirstRemote, true, deadline.Token));
            await AssertUnavailableAsync(failedRefresh);
            await Assert.That(unavailable.Client.HasCompatibleCohort).IsFalse();

            var deniedAdmission = await Assert.ThrowsExactlyAsync<KeyLoadException>(
                () => unavailable.Client.EnsureCompatibleCohortAsync(deadline.Token));
            await AssertUnavailableAsync(deniedAdmission);
            await Assert.That(unavailable.Client.HasCompatibleCohort).IsFalse();
        }

        await using var healthy = await RequestCqrsCohortScenario.StartAsync(runtime, deadline.Token);
        await healthy.Client.EnsureCompatibleCohortAsync(deadline.Token);
        var healthyAddress = await healthy.Client.ResolveAsync(
            RequestCqrsCohortScenario.FirstRemote, true, deadline.Token);
        await Assert.That(healthyAddress).IsEqualTo(SiloAddress.FromParsableString(runtime.RuntimeAddress(1)));
        await Assert.That(healthy.Client.HasCompatibleCohort).IsTrue();
    }

    private static async Task AssertUnavailableAsync(KeyLoadException? failure)
    {
        var observed = failure ?? throw new InvalidOperationException("The stopped socket was accepted.");
        await Assert.That(observed.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(observed.Message).IsEqualTo(ReplicaTransportProtocol.InvalidDiscovery);
    }
}
