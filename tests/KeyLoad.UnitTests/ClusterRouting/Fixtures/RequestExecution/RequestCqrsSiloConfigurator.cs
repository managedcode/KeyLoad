using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Communication.Orleans.Extensions;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsSiloConfigurator : ISiloConfigurator
{
    public RequestCqrsSiloConfigurator()
    {
    }

    public void Configure(ISiloBuilder siloBuilder)
    {
        var fixture = RequestCqrsFixtureOwners.Resolve(siloBuilder.Configuration);
        siloBuilder.AddActivityPropagation();
        if (fixture.NativeMaintenance is { } native)
        {
            siloBuilder.Services.AddOptions<TextIndexMaintenanceOptions>()
                .Bind(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()
                    .GetSection(TextIndexMaintenanceOptions.SectionName))
                .Validate(options => options.IsValid(), TextIndexMaintenanceOptions.ValidationMessage)
                .ValidateOnStart();
            INativeTextMaintenance textMaintenance = native.Owner;
            siloBuilder.Services.AddSingleton<Func<INativeTextMaintenance>>(() => textMaintenance);
        }
        if (fixture.NativeAnnMaintenance is { } ann)
        {
            siloBuilder.Services.AddOptions<AnnMaintenanceOptions>()
                .Bind(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()
                    .GetSection(AnnMaintenanceOptions.SectionName))
                .Validate(options => options.IsValid(), AnnMaintenanceOptions.ValidationMessage)
                .ValidateOnStart();
            INativeAnnMaintenance annMaintenance = ann.Owner;
            siloBuilder.Services.AddSingleton<Func<INativeAnnMaintenance>>(() => annMaintenance);
        }
        siloBuilder.Services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestContextState).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        siloBuilder.Services.AddSingleton(fixture.Database.Database);
        ICommitCoordinator coordinator = fixture.UsesNativeAdmission
            ? new RequestCqrsNativeCommitCoordinator(fixture.Database)
            : new EmbeddedCoordinator(fixture.Database.Database);
        siloBuilder.Services.AddSingleton<Func<ICommitCoordinator>>(() => coordinator);
        siloBuilder.Services.AddSingleton(fixture.Clock);
        siloBuilder.Services.AddSingleton(fixture.RoutingOptions);
        siloBuilder.Services.AddSingleton(UnitExecutionOptions.Messaging());
        var requestWork = fixture.RequestWork;
        siloBuilder.Services.AddSingleton<Func<NativeRequestWorkOwner>>(() => requestWork);
        siloBuilder.Services.AddSingleton(fixture.Codec);
        siloBuilder.Services.AddSingleton<NativeConnectionOwnerIdentity>();
        siloBuilder.Services.Configure<global::Orleans.Configuration.GrainCollectionOptions>(
            settings => settings.CollectionAge = fixture.Timing.OrdinaryCollectionAge);
        siloBuilder.Services.AddSingleton(new RequestCqrsCapabilityLedger());
        siloBuilder.Services.AddSingleton<IConfigureGrainTypeComponents>(services =>
            new RequestCqrsGrainComponentConfigurator(services.GetRequiredService<GrainClassMap>(), services));
        siloBuilder.AddOrleansGraph(configureGraph: graph =>
        {
            graph.AllowClientCallGrain<IRequestCqrsIdentityProbeGrain>();
            graph.AllowClientCallGrain<IManagementGrain>();
            graph.AllowClientCallGrain<IConnectionGrain>()
                .AddGrainTransition<IConnectionGrain, IConnectionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(IConnectionGrain.ExecuteStreamAsync)).And()
                .AddGrainTransition<IConnectionGrain, ICommandPartitionGrain>()
                .MethodByName(nameof(IConnectionGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And();
        });
        siloBuilder.UseOrleansCommunication();
    }
}
