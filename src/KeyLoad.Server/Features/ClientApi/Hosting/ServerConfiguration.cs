using System.Text.Json.Serialization;
using KeyLoad.Core;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Orleans;
using KeyLoad.Security;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.ServiceDefaults;
using KeyLoad.Storage.ZoneTree;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class ServerConfiguration
{
    internal static WebApplication Build(string[] args, IGrainPartitionMovementSealedOperationObserver? sealedObserver = null,
        IControlledBlobWireBorrowObserver? blobWireObserver = null,
        NativeLocalhostClusteringOptions? localhostClustering = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddRuntimeOptions(builder.Configuration);
        ServerConnectionTransportComposition.Configure(builder);
        builder.Services.AddOptions<KestrelServerOptions>().Configure<IOptions<ServerExecutionOptions>>(
            (server, configured) => server.Limits.MaxRequestBodySize = configured.Value.MaximumBodyBytes);
        builder.Services.AddOptions<KestrelServerOptions>().Configure<ServerConnectionRegistry>(
            (server, connections) => server.ConfigureEndpointDefaults(listen =>
            {
                listen.Use(next => connection => connections.RunAsync(connection, () => next(connection)));
                ((Microsoft.AspNetCore.Connections.IMultiplexedConnectionBuilder)listen).Use(
                    next => connection => connections.RunAsync(connection, () => next(connection)));
            }));
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.AddOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>()
            .Configure<IOptions<ServerExecutionOptions>>((options, configured) =>
                ConfigureJson(options.SerializerOptions, configured.Value.MaximumJsonDepth));
        Register(builder.Services, blobWireObserver, localhostClustering);
        if (sealedObserver is not null)
        { builder.Services.AddSingleton(sealedObserver); }
        DatabaseAuthenticationComposition.Register(builder.Services);
        McpServerComposition.Register(builder);
        var app = builder.Build();
        var runtime = app.Services.GetRequiredService<ServerRuntimeOptions>();
        runtime.ValidateBeforePhysicalOwnership();
        var phases = runtime.DatabasePhaseExecution.Value;
        DatabasePhaseTelemetry.Initialize(phases.Enabled, phases.StripeCount, phases.MaximumCasAttempts,
            app.Services.GetRequiredService<TimeProvider>());
        app.UseMiddleware<AdminHttpMetricsMiddleware>();
        app.Use(next => new ServerErrorMiddleware(next,
            app.Services.GetRequiredService<ILogger<ServerErrorMiddleware>>(), app.Services.GetRequiredService<TimeProvider>()).InvokeAsync);
        app.UseRouting();
        app.MapWhen(context => !DatabaseAuthenticationComposition.IsProtected(context.Request),
            branch =>
            {
                branch.UseRouting();
                branch.UseEndpoints(_ => { });
            });
        app.Use(next => new DatabaseIdentityMiddleware(next).InvokeAsync);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDefaultEndpoints();
        ReplicaReadinessEndpoints.Map(app);
        ReplicaMembershipHealthEndpoints.Map(app);
        NativeSiloDiscoveryDiagnosticEndpoints.Map(app);
        AdminStaticAssets.Map(app);
        app.MapKeyLoadApi();
        app.MapMcp(McpFramingProtocol.Path);
        return app;
    }

    internal static ServerRuntimeOptions ReadOfflineRuntimeOptions(string destination)
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Configuration[string.Join(ConfigurationPath.KeyDelimiter,
            ServerProtocol.ConfigurationSection, nameof(NodeOptions.DataDirectory))] = destination;
        var services = new ServiceCollection();
        services.AddRuntimeOptions(builder.Configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ServerRuntimeOptions>();
        options.ValidateBeforePhysicalOwnership();
        return options;
    }

    internal static IOptions<ZoneTreeStorageExecutionOptions> ReadOfflineStorageExecution()
    {
        var builder = WebApplication.CreateBuilder([]);
        var factory = new OptionsFactory<ZoneTreeStorageExecutionOptions>(
            [new ConfigureFromConfigurationOptions<ZoneTreeStorageExecutionOptions>(
                builder.Configuration.GetSection(ZoneTreeStorageExecutionOptions.SectionName))], [],
            [new ValidateOptions<ZoneTreeStorageExecutionOptions>(Options.DefaultName,
                options => options.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage)]);
        var configured = new OptionsManager<ZoneTreeStorageExecutionOptions>(factory);
        _ = configured.Value;
        return configured;
    }

    private static void ConfigureJson(System.Text.Json.JsonSerializerOptions options, int maximumDepth)
    {
        options.PropertyNameCaseInsensitive = false;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.MaxDepth = maximumDepth;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        foreach (var converter in JsonDefaults.Options.Converters)
        {
            options.Converters.Add(converter);
        }
    }

    private static void Register(IServiceCollection services, IControlledBlobWireBorrowObserver? blobWireObserver,
        NativeLocalhostClusteringOptions? localhostClustering)
    {
        if (localhostClustering is not null)
        { services.AddSingleton(localhostClustering); }
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServerConnectionRegistry>();
        services.AddSingleton<AdminHttpMetrics>();
        services.AddSingleton<AdminNodeObserver>();
        services.AddSingleton<IAuthorizationPolicy, AuthorizationPolicy>();
        services.AddSingleton(provider => new CommandAdmissionGovernor(
            provider.GetRequiredService<IOptions<CommandAdmissionLimits>>()));
        services.AddSingleton(provider => new HttpAdmissionGovernor(
            provider.GetRequiredService<IOptions<HttpAdmissionLimits>>()));
        services.AddSingleton<PartitionHost>();
        services.AddSingleton(provider => provider.GetRequiredService<PartitionHost>().Database);
        services.AddSingleton<INodeAdministration, NodeAdministration>();
        PhysicalOwnerRegistrationServices.Add(services);
        services.AddSingleton(provider => NativeDiscoveryOmissionComposition.CreateNode(provider, blobWireObserver));
        services.AddSingleton(provider => new GrainRequestCodec(provider.GetRequiredService<DatabaseEngine>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<GrainRoutingOptions>>())
        {
            PurposeObserver = provider.GetRequiredService<OrleansNode>().DiscoveryOmission?.PurposeObserver
        });
    }

}
