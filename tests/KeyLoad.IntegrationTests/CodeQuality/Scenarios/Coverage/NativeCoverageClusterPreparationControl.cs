using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

/// <summary>Supplies controlled configuration for an ordinary preparation control, never authenticated evidence.</summary>
internal static class NativeCoverageClusterPreparationControl
{
    internal const string StartupPoll = "100";
    private const string DurationFormat = "c";
    private const string SourceName = "controlled-source.json";
    private const string RunName = "controlled-run.json";

    internal static NativeCoverageClusterFixtureContext Create(string root)
    {
        var runId = Guid.NewGuid().ToString(NativeCoverageClusterProtocol.GuidFormat);
        var serverAssembly = typeof(ServerRuntimeOptions).Assembly;
        var dll = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(serverAssembly.Location)));
        var pdb = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.ChangeExtension(serverAssembly.Location, ".pdb"))));
        var server = new NativeCoverageClusterServer(serverAssembly.ManifestModule.ModuleVersionId
            .ToString(NativeCoverageClusterProtocol.GuidFormat), dll, pdb, dll);
        var policy = new NativeCoverageExecutionOptions();
        var bounds = new NativeCoverageClusterExecutionBounds(policy.MaximumDescriptorBytes, policy.MaximumFiles,
            policy.ReadBufferBytes, policy.MaximumTotalBytes, policy.MaximumFileBytes, policy.MaximumPathCharacters,
            policy.MaximumManifestBytes, policy.MaximumReportBytes,
            policy.ShutdownTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture),
            policy.SettlementTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture),
            policy.ContainerStopTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture),
            policy.ApplicationCleanupTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture));
        return new(runId, Path.Combine(root, RunName), Path.Combine(root, SourceName), dll, dll,
            NativeCoverageClusterProtocol.CoverageImagePrefix + Guid.Parse(runId).ToString(NativeCoverageClusterProtocol.GuidCompactFormat),
            NativeCoverageClusterProtocol.JsonShaPrefix + dll, root, pdb, pdb, root, runId, root, [], server,
            new(NativeCoverageClusterProtocol.CoverageToolPackageId, "18.11.2", dll, pdb), bounds, string.Empty, dll);
    }
}
