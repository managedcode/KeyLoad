using System.Net.Http.Json;
using System.Text.Json;
using DotNext.IO.Log;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using DotNext.Net.Cluster.Consensus.Raft.Membership;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.ServiceDefaults;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ErrorCode = KeyLoad.ErrorCode;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var node = builder.Configuration.GetSection("KeyLoad").Get<NodeOptions>() ?? new();
node.Validate();
var directory = Path.GetFullPath(node.DataDirectory);
Directory.CreateDirectory(directory);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["publicEndPoint"] = node.PublicEndpoint.TrimEnd('/') + "/raft", ["coldStart"] = "false",
    ["lowerElectionTimeout"] = "750", ["upperElectionTimeout"] = "1500", ["requestTimeout"] = "00:00:05",
    ["rpcTimeout"] = "00:00:03", ["partitioning"] = "false"
});
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 33_554_432);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.MaxDepth = 64;
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddSingleton(node);
builder.Services.AddSingleton<IAtomicStore>(_ => new ZoneTreeStore(new(directory + "/database")
{ Incarnation = node.Incarnation, SigningKey = Convert.FromBase64String(node.SigningKey) }));
builder.Services.AddSingleton<IAuthorizationPolicy, AuthorizationPolicy>();
builder.Services.AddSingleton<DatabaseEngine>();
builder.Services.AddSingleton<KeyLoad.Query.QueryEngine>();
builder.Services.AddSingleton<KeyLoad.Query.SearchEngine>();
builder.Services.AddSingleton(new PeerSecurity(Convert.FromBase64String(node.PeerSecret)));
builder.Services.AddSingleton<IHttpMessageHandlerFactory>(sp => sp.GetRequiredService<PeerSecurity>());
builder.Services.UsePersistentConfigurationStorage(directory + "/voters.bin");
builder.Services.AddSingleton<ReplicatedStateMachine>();
builder.Services.AddSingleton<IStateMachine>(sp => sp.GetRequiredService<ReplicatedStateMachine>());
builder.Services.AddSingleton<IPersistentState>(sp => new DurableRaftLog(new WriteAheadLog.Options
{
    Location = directory + "/raft", FlushInterval = TimeSpan.Zero, FlushOnCommit = true,
    HashAlgorithm = WriteAheadLog.IntegrityHashAlgorithm.Crc64
}, sp.GetRequiredService<IStateMachine>()));
builder.Services.AddSingleton<IAuditTrail<IRaftLogEntry>>(sp => sp.GetRequiredService<IPersistentState>());
builder.JoinCluster();
builder.Services.AddSingleton<ClusterCoordinator>();
builder.Services.AddSingleton<ICommitCoordinator>(sp => sp.GetRequiredService<ClusterCoordinator>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClusterCoordinator>());
builder.Services.AddSingleton<OrleansNode>();
var app = builder.Build();

// Seed only a fresh durable voter configuration, before the transport resolves the consensus cluster.
var voters = app.Services.GetRequiredService<IClusterConfigurationStorage<UriEndPoint>>();
await ClusterBootstrap.SeedAsync(voters, node.Peers);
var database = app.Services.GetRequiredService<DatabaseEngine>();
database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
    DatabaseEngine.Credential("root", "root", node.AdminKey));
app.Use(async (context, next) =>
{
    try
    {
        if (context.Request.Path.StartsWithSegments("/raft") || context.Request.Path.StartsWithSegments("/internal"))
        {
            if (!await context.RequestServices.GetRequiredService<PeerSecurity>().ValidateAsync(context.Request, context.RequestAborted))
            { context.Response.StatusCode = 401; return; }
        }
        else if (!context.Request.Path.StartsWithSegments("/health"))
        {
            // Authenticate from a trusted verifier after a quorum read; client claims are never accepted.
            await context.RequestServices.GetRequiredService<ICommitCoordinator>().ReadBarrierAsync(context.RequestAborted);
            var key = context.Request.Headers.Authorization.ToString();
            if (!key.StartsWith("Bearer ", StringComparison.Ordinal)) throw Errors.Fail(ErrorCode.Unauthenticated, "An API key is required.");
            context.Items["principal"] = database.Authenticate(key[7..], DateTimeOffset.UtcNow);
        }
        await next(context);
    }
    catch (KeyLoadException exception)
    {
        context.Response.StatusCode = exception.StatusCode;
        await context.Response.WriteAsJsonAsync(exception.ToProblem(), JsonDefaults.Options, context.RequestAborted);
    }
    catch (Exception exception) when (exception is JsonException or BadHttpRequestException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(Errors.Problem(ErrorCode.Validation, "The request contains invalid protocol JSON."), JsonDefaults.Options, context.RequestAborted);
    }
});
app.UseConsensusProtocolHandler();
app.MapDefaultEndpoints();
app.MapGet("/health/ready", async (ICommitCoordinator coordinator, OrleansNode orleans, CancellationToken cancellationToken) =>
{
    if (orleans.Grains is null) return Results.StatusCode(503);
    try { await coordinator.ReadBarrierAsync(cancellationToken); return Results.Ok(new { Status = "ready", Voters = node.Peers.Length }); }
    catch (KeyLoadException) { return Results.StatusCode(503); }
});
app.MapPost("/internal/commands", (ReplicatedOperation operation, ClusterCoordinator coordinator, CancellationToken cancellationToken)
    => coordinator.AcceptForwardedAsync(operation, cancellationToken));
app.MapGet("/internal/read-barrier", (ClusterCoordinator coordinator, CancellationToken cancellationToken)
    => coordinator.LeaderReadBarrierAsync(cancellationToken));
app.MapKeyLoadApi();
await app.StartAsync();
var consensus = app.Services.GetRequiredService<IRaftCluster>();
await consensus.Readiness.WaitAsync(app.Lifetime.ApplicationStopping);
while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
{
    try { await app.Services.GetRequiredService<ICommitCoordinator>().ReadBarrierAsync(app.Lifetime.ApplicationStopping); break; }
    catch (KeyLoadException exception) when (exception.Code == ErrorCode.OwnershipLost)
    { await Task.Delay(250, app.Lifetime.ApplicationStopping); }
}
var orleansNode = app.Services.GetRequiredService<OrleansNode>();
await orleansNode.StartAsync(app.Lifetime.ApplicationStopping);
try { await app.WaitForShutdownAsync(); }
finally
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await orleansNode.StopAsync(timeout.Token);
    await app.StopAsync(timeout.Token);
    await app.DisposeAsync();
}

public partial class Program;
