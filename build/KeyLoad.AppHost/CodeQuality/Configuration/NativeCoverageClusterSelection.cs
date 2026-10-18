using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

[ConfigurationBinding]
internal sealed record NativeCoverageClusterSelection(NativeCoverageClusterAdmission Admission, string Filter)
{
    internal static NativeCoverageClusterSelection? Read(IConfiguration configuration, string suite,
        string? filter, string? settings, string? output, string format,
        IOptions<NativeCoverageExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        var mode = configuration[NativeCoverageClusterProtocol.ModeSetting];
        var manifest = configuration[NativeCoverageClusterProtocol.SourceManifestSetting];
        if (mode is null && manifest is null)
        {
            return null;
        }
        if (mode != NativeCoverageClusterProtocol.Mode || suite != TestSuiteProtocol.ClusterSuite
            || filter != NativeCoverageClusterProtocol.Filter || settings is null || output is null
            || format != NativeCoverageProtocol.BinaryFormat || manifest is null)
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        var admission = NativeCoverageClusterManifestReader.ReadAsync(manifest, options, CancellationToken.None)
            .GetAwaiter().GetResult();
        var sourceRevision = Environment.GetEnvironmentVariable(NativeCoverageClusterProtocol.GithubShaEnvironment);
        if (!string.Equals(sourceRevision, admission.SourceRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        return new(admission, filter);
    }
}
