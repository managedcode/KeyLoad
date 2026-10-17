using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsHostedProfile(TimeProvider clock, IOptions<GrainRoutingOptions> routing,
    IGrainRequestPhaseObserver? observer, IGrainRequestPurposeObserver? purposeObserver,
    bool nativeText, bool nativeAnn, DatabaseLimits? limits)
{
    internal const string Root = "root";
    private const string Credential = "root.unit-test-credential-32-characters";
    internal TimeProvider Clock { get; } = clock;
    internal IOptions<GrainRoutingOptions> Routing { get; } = routing;
    internal IGrainRequestPhaseObserver? Observer { get; } = observer;
    internal IGrainRequestPurposeObserver? PurposeObserver { get; } = purposeObserver;
    internal bool NativeText { get; } = nativeText;
    internal bool NativeAnn { get; } = nativeAnn;
    internal NativeRuntimeTestOptions Timing { get; } = new();
    private IServiceProvider? LocalServiceProvider
    {
        get; set;
    }

    internal DatabaseEngine CreateDatabase(ZoneTreeStore store, ReplicaConfiguration configuration)
    {
        var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(limits),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), null,
            UnitExecutionOptions.MovementCheckpoints(), Clock);
        var original = create(store, null);
        TestDatabaseEngineFactory.Bootstrap(original, Root, Credential, physicalCatalog: false);
        PhysicalShardTestBootstrap.Bootstrap(original, Root, configuration.VoterIds);
        var owner = store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)?.DefaultShard)
            ?? throw new InvalidOperationException(RequestCqrsHostedProtocol.CatalogAbsent);
        return create(store, owner);
    }

    internal void BindLocalServices(IServiceProvider actual) => LocalServiceProvider = actual;
    internal IServiceProvider LocalServices => LocalServiceProvider
        ?? throw new InvalidOperationException(RequestCqrsHostedProtocol.LocalNotInitialized);
    internal IPhysicalRequestPlacement Placement => LocalServices.GetRequiredService<IPhysicalRequestPlacement>();
    internal void Register(ISiloBuilder silo) => RequestCqrsHostedRegistration.Register(silo, this);
}
