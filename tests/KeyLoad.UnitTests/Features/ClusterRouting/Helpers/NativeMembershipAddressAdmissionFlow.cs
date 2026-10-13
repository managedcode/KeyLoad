using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeMembershipAddressAdmissionFlow
{
    private const int GroupVoters = 3;
    private const int FirstSilo = 0;
    private const int OtherSilo = 1;
    private const int ConcurrentCalls = 12;
    private const int AdmissionSeconds = 15;

    internal static async Task RunAsync(
        Func<NativeSixSiloMembershipReadinessFixture, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new NativeSixSiloMembershipReadinessFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.DeployAsync(cancellationToken).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(AdmissionSeconds), TimeProvider.System);
                using var admission = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
                await operation(fixture, admission.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ResolveAsync(NativeSixSiloMembershipReadinessFixture fixture, CancellationToken token)
    {
        var handles = fixture.Cluster.Silos.ToArray();
        var membership = Membership(fixture);
        var before = membership.ReadActive();
        token.ThrowIfCancellationRequested();
        await Assert.That(membership.IsCurrent(handles[FirstSilo].Name, handles[OtherSilo].SiloAddress)).IsFalse();
        await HealthyAsync(fixture, membership, handles, token).ConfigureAwait(false);
        await Assert.That(membership.IsCurrent(handles[FirstSilo].Name, handles[OtherSilo].SiloAddress)).IsFalse();
        await HealthyAsync(fixture, membership, handles, token).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(before).AsSpan().SequenceEqual(JsonDefaults.Serialize(membership.ReadActive()))).IsTrue();
    }

    internal static async Task CancelledAsync(NativeSixSiloMembershipReadinessFixture fixture, CancellationToken token)
    {
        var handles = fixture.Cluster.Silos.ToArray();
        var membership = Membership(fixture);
        var before = membership.ReadActive();
        var options = OptionsFor(handles);
        await NativeSixSiloMembershipReadinessAssertions.CancelledAsync(
            cancelled => ReplicaMembershipReadiness.ReadView(before, options,
                handles[FirstSilo].SiloAddress, new OrleansMembershipOptions(), cancelled)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(before).AsSpan().SequenceEqual(JsonDefaults.Serialize(membership.ReadActive()))).IsTrue();
        await HealthyAsync(fixture, membership, handles, token).ConfigureAwait(false);
    }

    internal static async Task ConcurrentAsync(NativeSixSiloMembershipReadinessFixture fixture, CancellationToken token)
    {
        var handles = fixture.Cluster.Silos.ToArray();
        var membership = Membership(fixture);
        var before = membership.ReadActive();
        var calls = Enumerable.Range(FirstSilo, ConcurrentCalls).Select(async _ =>
        {
            await Task.Yield();
            await HealthyAsync(fixture, membership, handles, token).ConfigureAwait(false);
        }).ToArray();
        var joined = Task.WhenAll(calls);
        try
        { await joined.ConfigureAwait(false); }
        catch (Exception primary) { throw joined.Exception ?? new AggregateException(primary); }
        await Assert.That(calls.Length).IsEqualTo(ConcurrentCalls);
        await Assert.That(JsonDefaults.Serialize(before).AsSpan().SequenceEqual(JsonDefaults.Serialize(membership.ReadActive()))).IsTrue();
        await HealthyAsync(fixture, membership, handles, token).ConfigureAwait(false);
    }

    private static async Task HealthyAsync(NativeSixSiloMembershipReadinessFixture fixture,
        ReplicaNativeDiscoveryMembership membership, SiloHandle[] handles, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var handle = handles[FirstSilo];
        var actual = membership.Resolve(handle.Name);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.ToParsableString()).IsEqualTo(handle.SiloAddress.ToParsableString());
        await Assert.That(membership.IsCurrent(handle.Name, handle.SiloAddress)).IsTrue();
        await NativeSixSiloMembershipReadinessAssertions.CompleteAsync(
            fixture.Read(OptionsFor(handles), handle.SiloAddress, new OrleansMembershipOptions(), token), handles).ConfigureAwait(false);
    }

    private static ReplicaNativeDiscoveryMembership Membership(NativeSixSiloMembershipReadinessFixture fixture)
    {
        var primary = fixture.Cluster.Primary ?? throw new InvalidOperationException("The native cluster has no primary silo.");
        var oracle = fixture.Cluster.GetSiloServiceProvider(primary.SiloAddress).GetRequiredService<ISiloStatusOracle>();
        return new(oracle, Options.Create(new OrleansMembershipOptions()));
    }

    private static NodeOptions OptionsFor(SiloHandle[] handles)
        => new()
        {
            Peers = handles.Take(GroupVoters).Select(handle => handle.Name).ToArray(),
            PhysicalGroupTrust = new PhysicalGroupTrustOptions
            {
                ForeignVoterIds = handles.Skip(GroupVoters).Select(handle => handle.Name).ToArray(),
                RegisterPhysicalOwners = false,
                RemoteDocumentReads = false,
                RemotePartitionQueries = false
            }
        };
}
