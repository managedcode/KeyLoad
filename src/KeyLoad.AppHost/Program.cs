using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var root = Path.GetFullPath(builder.Configuration["KeyLoad:DataRoot"] ?? Path.Combine(builder.AppHostDirectory, "../../data/cluster"));
var ephemeral = builder.Configuration.GetValue<bool>("KeyLoad:Ephemeral");
if (!Directory.Exists(root) && !OperatingSystem.IsWindows()) Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
else Directory.CreateDirectory(root);
var profilePath = Path.Combine(root, "local-profile.json");
LocalProfile profile;
if (File.Exists(profilePath)) profile = JsonSerializer.Deserialize<LocalProfile>(File.ReadAllBytes(profilePath))!;
else
{
    profile = new(Guid.NewGuid(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "root." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
    File.WriteAllBytes(profilePath, JsonSerializer.SerializeToUtf8Bytes(profile));
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(profilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
var signing = builder.AddParameter("signing-key", profile.SigningKey, secret: true);
var peerSecret = builder.AddParameter("peer-secret", profile.PeerSecret, secret: true);
var admin = builder.AddParameter("admin-key", profile.AdminKey, secret: true);
var nodes = Enumerable.Range(1, 3).Select(number => builder.AddProject<Projects.KeyLoad_Server>($"node{number}", launchProfileName: null)
    .WithHttpEndpoint(port: ephemeral ? null : 5100 + number, name: "http", isProxied: false)
    .WithEndpoint(port: ephemeral ? null : 11110 + number, name: "silo", scheme: "tcp", env: "KeyLoad__SiloPort", isProxied: false)
    .WithEnvironment("KeyLoad__DataDirectory", Path.Combine(root, $"node{number}"))
    .WithEnvironment("KeyLoad__ClusterId", "keyload-" + profile.Incarnation.ToString("N"))
    .WithEnvironment("KeyLoad__Incarnation", profile.Incarnation.ToString())
    .WithEnvironment("KeyLoad__SigningKey", signing).WithEnvironment("KeyLoad__PeerSecret", peerSecret)
    .WithEnvironment("KeyLoad__AdminKey", admin).WithEnvironment("KeyLoad__AllowLoopbackHttp", "true")
    .WithEnvironment("Logging__LogLevel__Default", "Warning")
    .WithEnvironment("Logging__LogLevel__DotNext.Net.Cluster.Consensus.Raft", "Information")
    .WithHttpHealthCheck("/health/ready", endpointName: "http")).ToArray();
foreach (var resource in nodes)
{
    resource.WithEnvironment("KeyLoad__PublicEndpoint", resource.GetEndpoint("http"));
    for (var index = 0; index < nodes.Length; index++) resource.WithEnvironment($"KeyLoad__Peers__{index}", nodes[index].GetEndpoint("http"));
}
await builder.Build().RunAsync();
public sealed record LocalProfile(Guid Incarnation, string SigningKey, string PeerSecret, string AdminKey);
