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
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["publicEndPoint"] = node.PublicEndpoint.TrimEnd('/') + "/raft", ["coldStart"] = "false",
    ["lowerElectionTimeout"] = node.LowerElectionTimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
    ["upperElectionTimeout"] = node.UpperElectionTimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
    ["requestTimeout"] = TimeSpan.FromMilliseconds(node.RaftRequestTimeoutMilliseconds).ToString("c", System.Globalization.CultureInfo.InvariantCulture),
    ["rpcTimeout"] = TimeSpan.FromMilliseconds(node.RaftRpcTimeoutMilliseconds).ToString("c", System.Globalization.CultureInfo.InvariantCulture),
    ["partitioning"] = "false"
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
builder.Services.AddSingleton(new CommandAdmissionGovernor(node.CommandAdmission));
builder.Services.AddSingleton<IAtomicStore>(_ => new ZoneTreeStore(new(directory + "/database")
{ Incarnation = node.Incarnation, SigningKey = Convert.FromBase64String(node.SigningKey) }));
builder.Services.AddSingleton<IAuthorizationPolicy, AuthorizationPolicy>();
builder.Services.AddSingleton<DatabaseEngine>();
builder.Services.AddSingleton<KeyLoad.Query.QueryEngine>();
builder.Services.AddSingleton<KeyLoad.Query.SearchEngine>();
builder.Services.AddSingleton(sp => new PeerSecurity(Convert.FromBase64String(node.PeerSecret),
    connectTimeout: TimeSpan.FromMilliseconds(node.PeerConnectTimeoutMilliseconds), logger: sp.GetRequiredService<ILogger<PeerSecurity>>()));
builder.Services.AddSingleton<IHttpMessageHandlerFactory>(sp => sp.GetRequiredService<PeerSecurity>());
builder.Services.UsePersistentConfigurationStorage(directory + "/voters.bin");
builder.Services.AddSingleton(sp => new ReplicatedStateMachine(sp.GetRequiredService<DatabaseEngine>(),
    new DirectoryInfo(directory + "/snapshots"), node.SnapshotThreshold));
builder.Services.AddSingleton<IStateMachine>(sp => sp.GetRequiredService<ReplicatedStateMachine>());
builder.Services.AddSingleton<IPersistentState>(sp => new DurableRaftLog(new WriteAheadLog.Options
{
    Location = directory + "/raft", FlushInterval = Timeout.InfiniteTimeSpan,
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
await app.Services.GetRequiredService<ReplicatedStateMachine>().RestoreAsync(app.Lifetime.ApplicationStopping);
app.Use(async (context, next) =>
{
    try
    {
        if (context.Request.Path.StartsWithSegments("/raft") || context.Request.Path.StartsWithSegments("/internal"))
        {
            var bodyBudget = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (bodyBudget is { IsReadOnly: false }) bodyBudget.MaxRequestBodySize = context.RequestServices.GetRequiredService<PeerSecurity>().MaxBodyBytes;
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
        if (context.Request.Path.StartsWithSegments("/raft")) app.Logger.LogWarning("Raft request rejected: {ErrorCode}", exception.Code);
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
app.MapGet("/internal/state", (DatabaseEngine database, IRaftCluster cluster, ReplicatedStateMachine stateMachine, OrleansNode orleans) => new
{
    NodeId = database.Store.Identity.NodeId, cluster.AuditTrail.Term, cluster.AuditTrail.LastCommittedEntryIndex,
    cluster.AuditTrail.LastEntryIndex, cluster.AuditTrail.Version, MaterializedPosition = database.LastApplied,
    database.Store.Identity.ReadGeneration, SnapshotIndex = ((IStateMachine)stateMachine).Snapshot?.Index,
    Leader = cluster.Leader?.EndPoint.ToString(), RoutingReady = orleans.Grains is not null
});
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
