using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.AnalyticsStore;

/// <summary>Stable actual database identity and independent execution loopback silo settings for the process fixture.</summary>
internal static class UserAnalyticsProcessOptions
{
    private const string SettingPrefix = "--KeyLoad:";
    private const string Assignment = "=";
    private const string Identity = "analytics-process";
    private const string PublicUrls = "--urls=http://127.0.0.1:0";
    private const string WarningLogLevel = "--Logging:LogLevel:Default=Warning";
    private const string DiagnosticOutput = "--Logging:Console:LogToStandardErrorThreshold=Trace";
    private const int TestSecretBytes = 32;
    private const byte TestKeyByte = 42;
    private const int OneSilo = 1;
    private const string IncarnationText = "b48b95ce-971b-4de0-bbca-4823deec6db3";

    internal static string[] Arguments(string root, int siloPort) => [PublicUrls, WarningLogLevel, DiagnosticOutput,
        "--" + OrleansHostingOptions.SectionName + ":" + nameof(OrleansHostingOptions.SharedSecret) + Assignment
            + Convert.ToBase64String(RandomNumberGenerator.GetBytes(TestSecretBytes)),
        Setting(nameof(NodeOptions.DataDirectory), Path.Combine(root, UserAnalyticsProcessProtocol.PhysicalDirectory)),
        Setting(nameof(NodeOptions.SiloName), Identity), Setting(nameof(NodeOptions.ClusterId), Identity),
        Setting(nameof(NodeOptions.SiloPort), siloPort.ToString(CultureInfo.InvariantCulture)),
        Setting(nameof(NodeOptions.SiloCount), OneSilo.ToString(CultureInfo.InvariantCulture)),
        Setting(nameof(NodeOptions.ReplicaCount), OneSilo.ToString(CultureInfo.InvariantCulture)),
        Setting(nameof(NodeOptions.Incarnation), IncarnationText),
        Setting(nameof(NodeOptions.SigningKey), Convert.ToBase64String(Enumerable.Repeat(TestKeyByte, TestSecretBytes).ToArray())),
        Setting(nameof(NodeOptions.AdminKey), CrashFixtureValues.Credential)];

    private static string Setting(string name, string value) => SettingPrefix + name + Assignment + value;
}
