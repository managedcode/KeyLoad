using System.Diagnostics;
using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3OfflineUpgrade
{
    private const string Prepare = "prepare-native-node";
    private const string Verify = "verify-native-node";
    private const string Publish = "publish-native-node";
    private const string EnvironmentPrefix = "KeyLoad__";
    private const string Dotnet = "dotnet";
    private const string MissingServer = "The already-built current Release server is required.";

    internal static Task<NodeEpochRf3OfflineResult> RunAsync(string operation, string source,
        string destination, NodeEpochRf3Profile profile, string nodeName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (operation is not (Prepare or Verify or Publish) || !ClusterFixtureProtocol.IsNodeName(nodeName))
        { throw new ArgumentException("The offline operation and node must belong to the closed RF3 contract.", nameof(operation)); }
        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var server = Path.Combine(root, "src", "KeyLoad.Server", "bin", "Release", "net10.0", "KeyLoad.Server.dll");
        if (!File.Exists(server))
        { throw new InvalidOperationException(MissingServer); }
        var start = new ProcessStartInfo(Dotnet)
        { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { server, operation, "--source=" + Path.GetFullPath(source),
            "--destination=" + Path.GetFullPath(destination) })
        { start.ArgumentList.Add(value); }
        Configure(start, profile, nodeName);
        return NodeEpochRf3OfflineProcess.RunAsync(start, cancellationToken);
    }

    private static void Configure(ProcessStartInfo start, NodeEpochRf3Profile profile, string nodeName)
    {
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith(EnvironmentPrefix,
            StringComparison.OrdinalIgnoreCase)).ToArray())
        { _ = start.Environment.Remove(key); }
        start.Environment[EnvironmentPrefix + "Incarnation"] = profile.Incarnation.ToString("D", CultureInfo.InvariantCulture);
        start.Environment[EnvironmentPrefix + "ClusterId"] = "keyload-" + profile.Incarnation.ToString("N", CultureInfo.InvariantCulture);
        start.Environment[EnvironmentPrefix + "SigningKey"] = profile.SigningKey;
        start.Environment[EnvironmentPrefix + "PeerSecret"] = profile.PeerSecret;
        start.Environment[EnvironmentPrefix + "AdminKey"] = profile.AdminKey;
        start.Environment[EnvironmentPrefix + "PublicEndpoint"] = "http://" + nodeName + ":8080";
        start.Environment[EnvironmentPrefix + "SiloAddress"] = nodeName;
        start.Environment[EnvironmentPrefix + "AllowPrivateNetworkHttp"] = "true";
        start.Environment[EnvironmentPrefix + "SnapshotThreshold"]
            = NodeEpochRf3Protocol.SnapshotThreshold.ToString(CultureInfo.InvariantCulture);
        for (var index = 0; index < ClusterFixtureProtocol.NodeCount; index++)
        {
            start.Environment[EnvironmentPrefix + "Peers__" + index.ToString(CultureInfo.InvariantCulture)]
                = "http://" + ClusterFixtureProtocol.NodeName(index + 1) + ":8080";
        }
    }
}
