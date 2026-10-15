using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeRuntimeConstructorCycleArguments
{
    private const string NodePrefix = "--KeyLoad:";
    private const string ConnectionPrefix = "--ConnectionStrings:";
    private const string Assignment = "=";
    private const string KeySeparator = ":";
    private const string GuidFormat = "D";
    private const string ClusterPrefix = "constructor-cycle-";
    private const string RootPrefix = "root.";
    private const string DevelopmentStorage = "UseDevelopmentStorage=true";
    private const string Enabled = "true";
    private const string PublicUrls = "--urls=http://127.0.0.1:0";
    private const string LogWarning = "--Logging:LogLevel:Default=Warning";
    private static readonly string[] Voters =
        ["http://127.0.0.1:19071", "http://127.0.0.1:19072", "http://127.0.0.1:19073"];

    internal static string[] Create(string directory)
    {
        var values = new List<string>
        {
            PublicUrls, LogWarning,
            ConnectionPrefix + OrleansHostingOptions.ConnectionName + Assignment + DevelopmentStorage,
            Setting(nameof(NodeOptions.DataDirectory), directory),
            Setting(nameof(NodeOptions.PublicEndpoint), Voters[0]),
            Setting(nameof(NodeOptions.AllowLoopbackHttp), Enabled),
            Setting(nameof(NodeOptions.ClusterId), ClusterPrefix + Guid.NewGuid().ToString(GuidFormat)),
            Setting(nameof(NodeOptions.PhysicalShardId), Guid.NewGuid().ToString(GuidFormat)),
            Setting(nameof(NodeOptions.Incarnation), Guid.NewGuid().ToString(GuidFormat)),
            Setting(nameof(NodeOptions.SigningKey), Secret()),
            Setting(nameof(NodeOptions.PeerSecret), Secret()),
            Setting(nameof(NodeOptions.AdminKey), RootPrefix + Secret())
        };
        for (var index = 0; index < Voters.Length; index++)
        {
            values.Add(Setting(nameof(NodeOptions.Peers) + KeySeparator
                + index.ToString(System.Globalization.CultureInfo.InvariantCulture), Voters[index]));
        }
        return [.. values];
    }

    private static string Setting(string name, string value) => NodePrefix + name + Assignment + value;
    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(ReplicaTransportProtocol.SecretBytes));
}
