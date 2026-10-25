using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RuntimeConstructorCycleArguments
{
    private const string NodePrefix = "--KeyLoad:";
    private const string Assignment = "=";
    private const string GuidFormat = "D";
    private const string ClusterPrefix = "constructor-cycle-";
    private const string RootPrefix = "root.";
    private const string PublicUrls = "--urls=http://127.0.0.1:0";
    private const string LogWarning = "--Logging:LogLevel:Default=Warning";
    private const int SigningKeyBytes = 32;

    internal static string[] Create(string directory)
        =>
        [
            PublicUrls, LogWarning,
            "--" + OrleansHostingOptions.SectionName + ":" + nameof(OrleansHostingOptions.SharedSecret) + Assignment + Secret(),
            Setting(nameof(NodeOptions.DataDirectory), directory),
            Setting(nameof(NodeOptions.ClusterId), ClusterPrefix + Guid.NewGuid().ToString(GuidFormat)),
            Setting(nameof(NodeOptions.Incarnation), Guid.NewGuid().ToString(GuidFormat)),
            Setting(nameof(NodeOptions.SigningKey), Secret()),
            Setting(nameof(NodeOptions.AdminKey), RootPrefix + Secret())
        ];

    private static string Setting(string name, string value) => NodePrefix + name + Assignment + value;
    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SigningKeyBytes));
}
