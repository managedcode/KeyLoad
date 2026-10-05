using System.Security.Cryptography;
using System.Text.Json.Serialization;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.ServiceDefaults;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class ServerConfiguration
{
    internal static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddRuntimeOptions(builder.Configuration);
        builder.Services.AddOptions<KestrelServerOptions>().Configure<IOptions<ServerExecutionOptions>>(
            (server, configured) => server.Limits.MaxRequestBodySize = configured.Value.MaximumBodyBytes);
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.AddOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>()
            .Configure<IOptions<ServerExecutionOptions>>((options, configured) =>
                ConfigureJson(options.SerializerOptions, configured.Value.MaximumJsonDepth));
        Register(builder.Services);
        McpServerComposition.Register(builder);
        var app = builder.Build();
        app.Services.GetRequiredService<ServerRuntimeOptions>().ValidateBeforePhysicalOwnership();
        app.UseMiddleware<AdminHttpMetricsMiddleware>();
        app.Use(next => new ServerErrorMiddleware(next,
            app.Services.GetRequiredService<ILogger<ServerErrorMiddleware>>()).InvokeAsync);
        app.Use(next => new DatabaseIdentityMiddleware(next).InvokeAsync);
        app.MapDefaultEndpoints();
        ReplicaDiscoveryEndpoints.Map(app);
        ReplicaMembershipHealthEndpoints.Map(app);
        ReplicaMembershipAuthorityEndpoints.Map(app);
        AdminStaticAssets.Map(app);
        app.MapKeyLoadApi();
        app.MapMcp(McpFramingProtocol.Path);
        return app;
    }

    internal static NodeOptions ReadOfflineNode(string destination)
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Configuration[string.Join(ConfigurationPath.KeyDelimiter,
            ServerProtocol.ConfigurationSection, nameof(NodeOptions.DataDirectory))] = destination;
        var services = new ServiceCollection();
        services.AddRuntimeOptions(builder.Configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<NodeOptions>>().Value;
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
        services.AddSingleton<OrleansNode>();
        services.AddSingleton<ReplicaMembershipAuthorityOwner>(static _ => new());
        services.AddSingleton<ReplicaMembershipAuthorityEndpoint>(provider => new(
            provider.GetRequiredService<IOptions<NodeOptions>>(),
            provider.GetRequiredService<ReplicaMembershipAuthorityOwner>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<IOptions<ReplicaExecutionOptions>>()));
        services.AddSingleton<GrainRequestCodec>();
        services.AddSingleton(provider => CreatePeerSecurity(
            provider.GetRequiredService<IOptions<NodeOptions>>().Value, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<PeerDiscoveryOptions>>()));
    }

    private static PeerSecurity CreatePeerSecurity(NodeOptions options, TimeProvider clock, IOptions<PeerDiscoveryOptions> discovery)
    {
        var credential = Convert.FromBase64String(options.PeerSecret);
        try
        { return new(credential, clock, discovery); }
        finally
        { CryptographicOperations.ZeroMemory(credential); }
    }
}
