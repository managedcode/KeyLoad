using System.Text.Json.Serialization;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Security;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.ServiceDefaults;
using KeyLoad.Storage.ZoneTree;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class ServerConfiguration
{
    internal static async Task<WebApplication> BuildAsync(string[] args, IGrainPartitionMovementSealedOperationObserver? sealedObserver = null,
        NativeLocalhostClusteringOptions? localhostClustering = null,
        IDatabaseAuthenticationExecutionObserver? authenticationExecutionObserver = null,
        IPartitionMovementNativeObserver? partitionMovementNativeObserver = null,
        Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? lifecycleParticipantFactory = null,
        IGrainRequestPhaseObserver? phaseObserver = null, IGrainRequestPurposeObserver? purposeObserver = null,
        CancellationToken startupCancellation = default)
    {
        startupCancellation.ThrowIfCancellationRequested();
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddRuntimeOptions(builder.Configuration);
        var node = ServerNodeOptionsComposition.Read(builder.Configuration);
        var address = await OrleansNodeAddressResolver.ResolveAsync(node.Value, startupCancellation).ConfigureAwait(false);
        builder.Services.AddSingleton<IOptions<NodeOptions>>(node);
        ServerConnectionTransportComposition.Configure(builder);
        ConfigureTransportOptions(builder);
        Register(builder.Services, localhostClustering);
        PostgresNativeComposition.Register(builder.Services, builder.Configuration);
        RegisterObservers(builder.Services, sealedObserver, authenticationExecutionObserver,
            partitionMovementNativeObserver, phaseObserver, purposeObserver);
        OrleansSiloConfiguration.Register(builder, node, address, localhostClustering);
        if (lifecycleParticipantFactory is not null)
        {
            builder.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(lifecycleParticipantFactory);
        }
        DatabaseAuthenticationComposition.Register(builder.Services);
        McpServerComposition.Register(builder);
        var app = builder.Build();
        try
        {
            var runtime = app.Services.GetRequiredService<ServerRuntimeOptions>();
            runtime.ValidateBeforePhysicalOwnership();
            var phases = runtime.DatabasePhaseExecution.Value;
            DatabasePhaseTelemetry.Initialize(phases.Enabled, phases.StripeCount, phases.MaximumCasAttempts,
                app.Services.GetRequiredService<TimeProvider>());
            ConfigureApplication(app);
            return app;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static void RegisterObservers(IServiceCollection services,
        IGrainPartitionMovementSealedOperationObserver? sealedObserver,
        IDatabaseAuthenticationExecutionObserver? authenticationExecutionObserver,
        IPartitionMovementNativeObserver? partitionMovementNativeObserver,
        IGrainRequestPhaseObserver? phaseObserver, IGrainRequestPurposeObserver? purposeObserver)
    {
        if (sealedObserver is not null)
        {
            services.AddSingleton(sealedObserver);
        }
        if (authenticationExecutionObserver is not null)
        {
            services.AddSingleton(authenticationExecutionObserver);
        }
        if (partitionMovementNativeObserver is not null)
        {
            services.AddSingleton(partitionMovementNativeObserver);
        }
        if (phaseObserver is not null)
        {
            services.AddSingleton<IGrainRequestPhaseObserver>(phaseObserver);
        }
        if (purposeObserver is not null)
        {
            services.AddSingleton<IGrainRequestPurposeObserver>(purposeObserver);
        }
    }

    private static void ConfigureApplication(WebApplication app)
    {
        app.UseMiddleware<AdminHttpMetricsMiddleware>();
        app.Use(next => new ServerErrorMiddleware(next,
            app.Services.GetRequiredService<ILogger<ServerErrorMiddleware>>(), app.Services.GetRequiredService<TimeProvider>()).InvokeAsync);
        app.UseRouting();
        app.MapWhen(context => !DatabaseAuthenticationComposition.IsProtected(context.Request),
            branch =>
            {
                branch.UseRouting();
                branch.UseEndpoints(_ =>
                {
                });
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

    private static void ConfigureTransportOptions(WebApplicationBuilder builder)
    {
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

    private static void Register(IServiceCollection services, NativeLocalhostClusteringOptions? localhostClustering)
    {
        if (localhostClustering is not null)
        {
            services.AddSingleton(localhostClustering);
        }
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServerConnectionRegistry>();
        services.AddSingleton<AdminHttpMetrics>();
        services.AddSingleton<OperationalAnalyticsRuntime>();
        services.AddSingleton<UserAnalyticsRuntime>();
        services.AddSingleton<INativeUserAnalytics>(provider => provider.GetRequiredService<UserAnalyticsRuntime>());
        services.AddSingleton(provider => ActivatorUtilities.CreateInstance<AdminNodeObserver>(provider,
            provider.GetRequiredService<PartitionHostApplicationOwner>().Borrow()));
        services.AddSingleton<IAuthorizationPolicy, AuthorizationPolicy>();
        services.AddSingleton(provider => new CommandAdmissionGovernor(
            provider.GetRequiredService<IOptions<CommandAdmissionLimits>>()));
        services.AddSingleton(provider => new HttpAdmissionGovernor(
            provider.GetRequiredService<IOptions<HttpAdmissionLimits>>()));
        services.AddSingleton<PartitionHostApplicationOwner>();
        services.AddSingleton<INodeAdministration>(provider => ActivatorUtilities.CreateInstance<NodeAdministration>(provider,
            provider.GetRequiredService<PartitionHostApplicationOwner>().Borrow()));
        PhysicalOwnerRegistrationServices.Add(services);
        services.AddSingleton(NativeDiscoveryOmissionComposition.CreateNode);
    }

}
