using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeSixSiloMembershipReadinessFixture
{
    private const short Silos = 6;
    private const int GroupVoters = 3;
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions = Options.Create(new OrleansMembershipOptions());
    internal TestCluster Cluster { get; } = new TestClusterBuilder(Silos).Build();

    internal async Task RunAsync(CancellationToken token)
    {
        await Cluster.DeployAsync(token).ConfigureAwait(false);
        var handles = Cluster.Silos.ToArray();
        var names = handles.Select(silo => silo.Name).ToArray();
        var options = new NodeOptions
        {
            Peers = names.Take(GroupVoters).ToArray(),
            PhysicalGroupTrust = new PhysicalGroupTrustOptions
            {
                ForeignVoterIds = names.Skip(GroupVoters).ToArray(),
                RegisterPhysicalOwners = false,
                RemoteDocumentReads = false,
                RemotePartitionQueries = false
            }
        };
        var original = Cluster.Primary ?? throw new InvalidOperationException("The deployed native cluster has no primary silo.");
        var bounds = membershipOptions.Value;
        bounds.Validate();
        var initial = Read(options, original.SiloAddress, bounds, token);
        await NativeSixSiloMembershipReadinessAssertions.CompleteAsync(initial, handles).ConfigureAwait(false);
        var wrong = options with
        { PhysicalGroupTrust = options.PhysicalGroupTrust with { ForeignVoterIds = ["unconfigured-native-silo", .. names.Skip(GroupVoters + 1)] } };
        await Assert.That(Read(wrong, original.SiloAddress, bounds, token)).IsNull();
        await Assert.That(Read(options, original.SiloAddress, new OrleansMembershipOptions { MaximumRows = Silos - 1 }, token)).IsNull();
        await Assert.That(Read(options, original.SiloAddress, new OrleansMembershipOptions { MaximumAddressBytes = 1 }, token)).IsNull();
        await NativeSixSiloMembershipReadinessAssertions.CompleteAsync(Read(options, original.SiloAddress, bounds, token), handles).ConfigureAwait(false);
        await NativeSixSiloMembershipReadinessAssertions.CancelledAsync(
            cancelled => Read(options, original.SiloAddress, bounds, cancelled)).ConfigureAwait(false);
        await NativeSixSiloMembershipReadinessAssertions.CompleteAsync(Read(options, original.SiloAddress, bounds, token), handles).ConfigureAwait(false);
        var oldAddress = original.SiloAddress;
        var restarted = await Cluster.RestartSiloAsync(original).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The native restart returned no silo handle.");
        await Cluster.WaitForLivenessToStabilizeAsync(false).ConfigureAwait(false);
        await Assert.That(restarted.Name).IsEqualTo(original.Name);
        await Assert.That(restarted.SiloAddress.Equals(oldAddress)).IsFalse();
        await Assert.That(Read(options, oldAddress, bounds, token)).IsNull();
        var current = await WaitForCurrentAsync(options, restarted.SiloAddress, bounds, token).ConfigureAwait(false);
        await Assert.That(Read(options, oldAddress, bounds, token)).IsNull();
        await NativeSixSiloMembershipReadinessAssertions.CompleteAsync(current, Cluster.Silos.ToArray()).ConfigureAwait(false);
        await Assert.That(current!.ActiveFingerprint).IsNotEqualTo(initial!.ActiveFingerprint);
    }

    internal MembershipReadinessSnapshot? Read(NodeOptions options, SiloAddress localAddress,
        OrleansMembershipOptions bounds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var primary = Cluster.Primary ?? throw new InvalidOperationException("The deployed native cluster has no primary silo.");
        var oracle = Cluster.GetSiloServiceProvider(primary.SiloAddress).GetRequiredService<ISiloStatusOracle>();
        var membership = new ReplicaNativeDiscoveryMembership(oracle, Options.Create(new OrleansMembershipOptions()));
        return ReplicaMembershipReadiness.ReadView(membership.ReadActive(), options, localAddress, bounds, token);
    }

    private async Task<MembershipReadinessSnapshot> WaitForCurrentAsync(NodeOptions options,
        SiloAddress address, OrleansMembershipOptions bounds, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(bounds.StartupTimeout, clock);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        while (true)
        {
            if (Read(options, address, bounds, execution.Token) is { } current)
            { return current; }
            await Task.Delay(bounds.StartupRetryDelay, clock, execution.Token).ConfigureAwait(false);
        }
    }
}
