using System.Text.Json.Serialization;
using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Server.Features.OrleansHosting;
using KeyLoad.Storage.ZoneTree;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class ServerConfiguration
{
    internal static async Task<WebApplication> BuildAsync(string[] args,
        IDatabaseAuthenticationExecutionObserver? authenticationExecutionObserver = null,
        Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? lifecycleParticipantFactory = null,
        IGrainRequestPhaseObserver? phaseObserver = null, IGrainRequestPurposeObserver? purposeObserver = null,
        Action<IServiceCollection, NodeOptions>? configureServices = null,
        CancellationToken startupCancellation = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(ServerConfiguration).Assembly.GetName().Name
        });
        await ConfigureBuilderAsync(builder, authenticationExecutionObserver,
            lifecycleParticipantFactory, phaseObserver, purposeObserver, configureServices, startupCancellation);
        var app = builder.Build();
        try
        {
            ConfigureApplication(app);
            return app;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal static async Task ConfigureBuilderAsync(WebApplicationBuilder builder,
        IDatabaseAuthenticationExecutionObserver? authenticationExecutionObserver = null,
        Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? lifecycleParticipantFactory = null,
        IGrainRequestPhaseObserver? phaseObserver = null, IGrainRequestPurposeObserver? purposeObserver = null,
        Action<IServiceCollection, NodeOptions>? configureServices = null,
        CancellationToken startupCancellation = default)
    {
        startupCancellation.ThrowIfCancellationRequested();
        builder.AddServiceDefaults();
        builder.Services.AddRuntimeOptions(builder.Configuration);
        var node = ServerNodeOptionsComposition.Read(builder.Configuration);
        var address = await OrleansNodeAddressResolver.ResolveAsync(node.Value, startupCancellation);
        builder.Services.AddSingleton<IOptions<NodeOptions>>(node);
        ServerConnectionTransportComposition.Configure(builder);
        ConfigureTransportOptions(builder);
        Register(builder.Services);
        PostgresComposition.Register(builder.Services, builder.Configuration);
        RegisterObservers(builder.Services, authenticationExecutionObserver, phaseObserver, purposeObserver);
        OrleansSiloConfiguration.Register(builder, node, address);
        builder.Services.AddHostedService(provider => provider.GetRequiredService<OrleansNodeLifecycle>());
        builder.Services.AddOptions<HostOptions>().Configure<IOptions<ServerExecutionOptions>>((host, execution) =>
        {
            host.ShutdownTimeout = execution.Value.ShutdownTimeout;
            host.ServicesStartConcurrently = false;
            host.ServicesStopConcurrently = false;
        });
        if (lifecycleParticipantFactory is not null)
        {
            builder.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(lifecycleParticipantFactory);
        }
        DatabaseAuthenticationComposition.Register(builder.Services);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddFluentUIComponents();
        builder.Services.AddKeyLoadClientExecutionOptions(builder.Configuration);
        builder.Services.AddOptions<AdminConsoleOptions>()
            .Bind(builder.Configuration.GetSection(AdminConsoleOptions.SectionName))
            .Validate(options => options.IsValid(), AdminConsoleOptions.Invalid).ValidateOnStart();
        builder.Services.AddScoped<AdminConsoleConnection>();
        builder.Services.AddScoped<AdminConsoleSession>();
        FileStorageComposition.Register(builder);
        McpServerComposition.Register(builder);
        configureServices?.Invoke(builder.Services, node.Value);
    }

    private static void RegisterObservers(IServiceCollection services,
        IDatabaseAuthenticationExecutionObserver? authenticationExecutionObserver,
        IGrainRequestPhaseObserver? phaseObserver, IGrainRequestPurposeObserver? purposeObserver)
    {
        if (authenticationExecutionObserver is not null)
        {
            services.AddSingleton(authenticationExecutionObserver);
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

    internal static void ConfigureApplication(WebApplication app)
    {
        var runtime = app.Services.GetRequiredService<ServerRuntimeOptions>();
        runtime.ValidateBeforePhysicalOwnership();
        var phases = runtime.DatabasePhaseExecution.Value;
        DatabasePhaseTelemetry.Initialize(phases.Enabled, phases.StripeCount, phases.MaximumCasAttempts,
            TimeProvider.System);
        app.UseMiddleware<AdminHttpMetricsMiddleware>();
        app.Use(next => new ServerErrorMiddleware(next,
            app.Services.GetRequiredService<ILogger<ServerErrorMiddleware>>(), app.Services.GetRequiredService<TimeProvider>()).InvokeAsync);
        AdminStaticAssets.UseHeaders(app);
        app.UseRouting();
        app.UseAntiforgery();
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
        DatabaseReadinessEndpoints.Map(app);
        OrleansHealthEndpoints.Map(app);
        AdminStaticAssets.Map(app);
        app.MapStaticAssets().AllowAnonymous();
        app.MapRazorComponents<AdminApp>().AddInteractiveServerRenderMode().AllowAnonymous();
        app.MapKeyLoadApi();
        app.MapControllers();
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

    private static void Register(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServerConnectionRegistry>();
        services.AddSingleton<AdminHttpMetrics>();
        services.AddSingleton<IAuthorizationPolicy, AuthorizationPolicy>();
        services.AddSingleton(provider => new CommandAdmissionGovernor(
            provider.GetRequiredService<IOptions<CommandAdmissionLimits>>()));
        services.AddSingleton(provider => new HttpAdmissionGovernor(
            provider.GetRequiredService<IOptions<HttpAdmissionLimits>>()));
        services.AddSingleton<PartitionHostApplicationOwner>();
        services.AddSingleton(provider => ActivatorUtilities.CreateInstance<OrleansNode>(provider));
    }

}
