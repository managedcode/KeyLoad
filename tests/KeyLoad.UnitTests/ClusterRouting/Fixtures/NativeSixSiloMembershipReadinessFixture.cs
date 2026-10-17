using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeSixSiloMembershipReadinessFixture : IAsyncDisposable
{
    private const string ClusterNotCreated = "The native cluster has not been created.";
    private const string NativeMembershipProvider = "Orleans.Runtime.MembershipService.AzureBasedMembershipTable";
    private const short Silos = 6;
    private const int GroupVoters = 3;
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions = Options.Create(new OrleansMembershipOptions());
    private TestCluster? cluster;
    private NativeSixMembershipInfrastructure? infrastructure;
    private Task? disposal;
    internal TestCluster Cluster => cluster ?? throw new InvalidOperationException(ClusterNotCreated);

    internal async Task RunAsync(CancellationToken token)
    {
        await DeployAsync(token).ConfigureAwait(false);
        var handles = Cluster.Silos.ToArray();
        await RequireNativeMembershipProviderAsync(handles).ConfigureAwait(false);
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
        var providerRows = await NativeMembershipProviderRows.ReadAsync(Cluster, original.SiloAddress, token).ConfigureAwait(false);
        await NativeMembershipProviderRows.RequireAsync(Cluster, handles, null, token).ConfigureAwait(false);
        var wrong = options with
        {
            PhysicalGroupTrust = options.PhysicalGroupTrust with
            {
                ForeignVoterIds = ["unconfigured-native-silo", .. names.Skip(GroupVoters + 1)]
            }
        };
        await Assert.That(Read(wrong, original.SiloAddress, bounds, token)).IsNull();
        await NativeMembershipProviderRows.RequireAsync(Cluster, handles, providerRows, token).ConfigureAwait(false);
        await Assert.That(Read(options, original.SiloAddress, new OrleansMembershipOptions { MaximumRows = Silos - 1 }, token)).IsNull();
        await NativeMembershipProviderRows.RequireAsync(Cluster, handles, providerRows, token).ConfigureAwait(false);
        await Assert.That(Read(options, original.SiloAddress, new OrleansMembershipOptions { MaximumAddressBytes = 1 }, token)).IsNull();
        await NativeMembershipProviderRows.RequireAsync(Cluster, handles, providerRows, token).ConfigureAwait(false);
        await NativeSixSiloMembershipReadinessAssertions.CompleteAsync(Read(options, original.SiloAddress, bounds, token), handles).ConfigureAwait(false);
        await NativeSixSiloMembershipReadinessAssertions.CancelledAsync(
            cancelled => Read(options, original.SiloAddress, bounds, cancelled)).ConfigureAwait(false);
        await NativeMembershipProviderRows.RequireAsync(Cluster, handles, providerRows, token).ConfigureAwait(false);
        await NativeSixSiloMembershipReadinessAssertions.CompleteAsync(Read(options, original.SiloAddress, bounds, token), handles).ConfigureAwait(false);
        var oldAddress = original.SiloAddress;
        var restarted = await Cluster.RestartSiloAsync(original).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The native restart returned no silo handle.");
        await Cluster.WaitForLivenessToStabilizeAsync(false).ConfigureAwait(false);
        await RequireNativeMembershipProviderAsync(Cluster.Silos).ConfigureAwait(false);
        await Assert.That(restarted.Name).IsEqualTo(original.Name);
        await Assert.That(restarted.SiloAddress.Equals(oldAddress)).IsFalse();
        await Assert.That(Read(options, oldAddress, bounds, token)).IsNull();
        var current = await WaitForCurrentAsync(options, restarted.SiloAddress, bounds, token).ConfigureAwait(false);
        await Assert.That(Read(options, oldAddress, bounds, token)).IsNull();
        await NativeSixSiloMembershipReadinessAssertions.CompleteAsync(current, Cluster.Silos.ToArray()).ConfigureAwait(false);
        await Assert.That(current!.ActiveFingerprint).IsNotEqualTo(initial!.ActiveFingerprint);
        await NativeMembershipProviderRows.RestartedAsync(Cluster, Cluster.Silos.ToArray(), oldAddress, token).ConfigureAwait(false);
    }

    internal async Task DeployAsync(CancellationToken token)
    {
        infrastructure = new NativeSixMembershipInfrastructure();
        using var deadline = new CancellationTokenSource(membershipOptions.Value.StartupTimeout, clock);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var connection = await infrastructure.StartAsync(startup.Token).ConfigureAwait(false);
        var builder = new TestClusterBuilder(Silos);
        builder.Properties[NativeSixAzureMembershipConfigurator.ConnectionKey] = connection;
        builder.AddSiloBuilderConfigurator<NativeSixAzureMembershipConfigurator>();
        cluster = builder.Build();
        await Cluster.DeployAsync(startup.Token).ConfigureAwait(false);
        await RequireNativeMembershipProviderAsync(Cluster.Silos).ConfigureAwait(false);
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
        try
        {
            while (true)
            {
                if (Read(options, address, bounds, execution.Token) is { } current)
                {
                    return current;
                }
                await Task.Delay(bounds.StartupRetryDelay, clock, execution.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException original) when (execution.IsCancellationRequested)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(() => WriteCurrentUnavailable(options, address), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void WriteCurrentUnavailable(NodeOptions options, SiloAddress expectedLocal)
    {
        var primary = Cluster.Primary ?? throw new InvalidOperationException("The deployed native cluster has no primary silo.");
        var oracle = Cluster.GetSiloServiceProvider(primary.SiloAddress).GetRequiredService<ISiloStatusOracle>();
        Console.Error.WriteLine($"NativeSixSiloMembership phase=RestartUnavailable primary={primary.SiloAddress.ToParsableString()} expectedLocal={expectedLocal.ToParsableString()} approximateActiveCount={oracle.GetApproximateSiloStatuses(onlyActive: true).Count}");
        Console.Error.WriteLine($"NativeSixSiloMembership expectedNames={string.Join(",", options.Peers.Concat(options.PhysicalGroupTrust.ForeignVoterIds))}");
        var native = new ReplicaNativeDiscoveryMembership(oracle, membershipOptions);
        foreach (var entry in native.ReadActive())
        {
            Console.Error.WriteLine($"NativeSixSiloMembership activeName={entry.SiloName} activeAddress={entry.SiloAddress}");
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return new(disposal ??= DisposeAsyncCore().AsTask());
    }

    private async ValueTask DisposeAsyncCore()
    {
        var failures = new List<Exception>();
        if (cluster is not null)
        {
            await ServerFailureObserver.ObserveAsync(cluster.StopAllSilosAsync, failures).ConfigureAwait(false);
            try
            {
                await cluster.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        if (infrastructure is not null)
        {
            try
            {
                await infrastructure.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task RequireNativeMembershipProviderAsync(IEnumerable<SiloHandle> handles)
    {
        foreach (var handle in handles)
        {
            var provider = Cluster.GetSiloServiceProvider(handle.SiloAddress)
                .GetRequiredService<global::Orleans.IMembershipTable>();
            await Assert.That(provider.GetType().FullName).IsEqualTo(NativeMembershipProvider);
        }
    }
}
