using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.UnitTests.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsHostedRegistration
{
    internal static void Register(ISiloBuilder silo, RequestCqrsHostedProfile profile)
    {
        var services = silo.Services;
        services.AddSingleton(provider => provider.GetRequiredService<RequestCqrsCohortNativeNode>().Hosted
            ?? throw new InvalidOperationException(RequestCqrsHostedProtocol.OwnerAbsent));
        services.AddSingleton(provider => provider.GetRequiredService<RequestCqrsCohortNativeNode>().Journal.Database);
        services.AddSingleton<Func<ICommitCoordinator>>(provider =>
        {
            var actual = provider.GetRequiredService<RequestCqrsHostedNodeResources>().Coordinator;
            return () => actual;
        });
        services.AddSingleton<Func<NativeRequestWorkOwner>>(provider =>
        {
            var actual = provider.GetRequiredService<RequestCqrsHostedNodeResources>().Work;
            return () => actual;
        });
        services.AddSingleton(provider => provider.GetRequiredService<RequestCqrsHostedNodeResources>().PhysicalOwner);
        services.AddSingleton<IPhysicalRequestPlacement, PhysicalDocumentRequestPlacement>();
        services.AddSingleton(provider => provider.GetRequiredService<RequestCqrsHostedNodeResources>().Codec);
        services.AddSingleton(profile.Routing);
        services.AddSingleton(UnitExecutionOptions.Messaging());
        services.AddSingleton(provider => provider.GetRequiredService<DatabaseEngine>().OperationLimitsOptions);
        services.Configure<global::Orleans.Configuration.GrainCollectionOptions>(
            settings => settings.CollectionAge = profile.Timing.OrdinaryCollectionAge);
        services.AddSingleton<NativeConnectionOwnerIdentity>();
        services.AddSingleton(new RequestCqrsCapabilityLedger());
        services.AddSingleton<IConfigureGrainTypeComponents>(provider => new RequestCqrsGrainComponentConfigurator(
            provider.GetRequiredService<GrainClassMap>(), provider));
        if (profile.Observer is IReplicaTransportObservation transportObservation)
        {
            services.AddSingleton<IReplicaTransportObservation>(transportObservation);
        }
        RegisterMaintenance(services, profile);
        RequestCqrsHostedSerialization.Silo(silo);
    }

    private static void RegisterMaintenance(IServiceCollection services, RequestCqrsHostedProfile profile)
    {
        if (profile.NativeText)
        {
            services.AddOptions<TextIndexMaintenanceOptions>()
                .Validate(value => value.IsValid(), TextIndexMaintenanceOptions.ValidationMessage).ValidateOnStart();
            services.AddSingleton<Func<INativeTextMaintenance>>(provider =>
            {
                var actual = provider.GetRequiredService<RequestCqrsHostedNodeResources>().Text!.Owner;
                return () => actual;
            });
        }
        if (profile.NativeAnn)
        {
            services.AddOptions<AnnMaintenanceOptions>()
                .Validate(value => value.IsValid(), AnnMaintenanceOptions.ValidationMessage).ValidateOnStart();
            services.AddSingleton<Func<INativeAnnMaintenance>>(provider =>
            {
                var actual = provider.GetRequiredService<RequestCqrsHostedNodeResources>().Ann!.Owner;
                return () => actual;
            });
        }
    }
}
