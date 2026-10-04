using System.Security.Cryptography;
using System.Text.Json.Serialization;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.ServiceDefaults;

namespace KeyLoad.Server;

internal static class ServerConfiguration
{
    internal static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        var node = ReadNode(builder.Configuration);
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = ServerProtocol.KestrelMaximumBodyBytes);
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
        Register(builder.Services, node);
        McpServerComposition.Register(builder, node);
        var app = builder.Build();
        app.UseMiddleware<AdminHttpMetricsMiddleware>();
        app.Use(next => new ServerErrorMiddleware(next,
            app.Services.GetRequiredService<ILogger<ServerErrorMiddleware>>()).InvokeAsync);
        app.Use(next => new DatabaseIdentityMiddleware(next).InvokeAsync);
        app.MapDefaultEndpoints();
        ReplicaDiscoveryEndpoints.Map(app);
        AdminStaticAssets.Map(app);
        app.MapKeyLoadApi();
        app.MapMcp(McpFramingProtocol.Path);
        return app;
    }

    internal static NodeOptions ReadOfflineNode(string destination)
    {
        var builder = WebApplication.CreateBuilder([]);
        var node = (builder.Configuration.GetSection(ServerProtocol.ConfigurationSection).Get<NodeOptions>() ?? new())
            with
        { DataDirectory = destination };
        node.Validate();
        return node;
    }

    private static NodeOptions ReadNode(ConfigurationManager configuration)
    {
        var node = configuration.GetSection(ServerProtocol.ConfigurationSection).Get<NodeOptions>() ?? new();
        node.Validate();
        return node;
    }

    private static void ConfigureJson(System.Text.Json.JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = false;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.MaxDepth = ServerProtocol.MaximumJsonDepth;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        foreach (var converter in JsonDefaults.Options.Converters)
        {
            options.Converters.Add(converter);
        }
    }

    private static void Register(IServiceCollection services, NodeOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AdminHttpMetrics>();
        services.AddSingleton<AdminNodeObserver>();
        services.AddSingleton<IAuthorizationPolicy, AuthorizationPolicy>();
        services.AddSingleton(new CommandAdmissionGovernor(options.CommandAdmission));
        services.AddSingleton(new HttpAdmissionGovernor(options.HttpAdmission));
        services.AddSingleton<PartitionHost>();
        services.AddSingleton(provider => provider.GetRequiredService<PartitionHost>().Database);
        services.AddSingleton<INodeAdministration, NodeAdministration>();
        services.AddSingleton<OrleansNode>();
        services.AddSingleton<GrainRequestCodec>();
        services.AddSingleton(provider => CreatePeerSecurity(options, provider.GetRequiredService<TimeProvider>()));
    }

    private static PeerSecurity CreatePeerSecurity(NodeOptions options, TimeProvider clock)
    {
        var credential = Convert.FromBase64String(options.PeerSecret);
        try
        { return new(credential, clock, TimeSpan.FromMilliseconds(options.PeerConnectTimeoutMilliseconds)); }
        finally
        { CryptographicOperations.ZeroMemory(credential); }
    }
}
