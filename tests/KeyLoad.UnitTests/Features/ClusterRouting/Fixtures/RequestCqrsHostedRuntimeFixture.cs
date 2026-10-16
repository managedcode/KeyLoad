using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterReplication;
using KeyLoad.UnitTests.Features.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;
using TUnit.Core.Interfaces;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsHostedRuntimeFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly RequestCqrsHostedProfile profile;
    private readonly RequestCqrsCohortRuntimeFixture runtime;
    internal RequestCqrsHostedRuntimeFixture(TimeProvider? clock = null, GrainRoutingOptions? routing = null,
        IGrainRequestPhaseObserver? observer = null, IGrainRequestPurposeObserver? purposeObserver = null,
        bool nativeTextMaintenance = false, bool nativeAnnMaintenance = false, DatabaseLimits? limits = null)
    {
        profile = new(clock ?? TimeProvider.System, UnitRoutingOptions.Routing(routing), observer, purposeObserver,
            nativeTextMaintenance, nativeAnnMaintenance, limits);
        runtime = new(hosted: profile);
    }
    internal TestCluster Cluster => runtime.Cluster;
    internal Guid ConnectionId => Services.GetRequiredService<NativeConnectionOwnerIdentity>().Id;
    internal NativeRuntimeTestOptions Timing => profile.Timing;
    internal TimeProvider Clock => profile.Clock;
    internal IOptions<GrainRoutingOptions> RoutingOptions => profile.Routing;
    internal IServiceProvider Services => profile.LocalServices;
    private RequestCqrsHostedNodeResources Local => runtime.Node(RequestCqrsCohortScenario.LocalVoter).Hosted
        ?? throw new InvalidOperationException(RequestCqrsHostedProtocol.LocalOwnerAbsent);
    internal GrainRequestCodec Codec => Local.Codec;
    internal NativeRequestWorkOwner RequestWork => Local.Work;
    internal NativeTextMaintenanceTestRuntime? NativeMaintenance => Local.Text;
    internal NativeAnnMaintenanceTestRuntime? NativeAnnMaintenance => Local.Ann;
    internal RequestCqrsHostedDatabase Database { get; private set; } = null!;

    public Task InitializeAsync() => InitializeAsync(CancellationToken.None);
    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            await runtime.InitializeAsync(startup.Token).ConfigureAwait(false);
            _ = await ReplicaBarrierDeadlineFlow.LeaderAsync(runtime, startup.Token).ConfigureAwait(false);
            await Local.Coordinator.ReadBarrierAsync(startup.Token).ConfigureAwait(false);
            Database = new(Local, runtime.Node(RequestCqrsCohortScenario.LocalVoter).Journal);
        }
        catch (Exception original)
        {
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
    }

    public ValueTask DisposeAsync() => runtime.DisposeAsync();
}
