using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

[ConfigurationBinding]
internal sealed record NativeCoverageRf3Selection(NativeCoverageRf3Admission Admission, string Filter)
{
    internal static NativeCoverageRf3Selection? Read(IConfiguration configuration, string suite,
        string? filter, string? settings, string? output, string format,
        IOptions<NativeCoverageExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        var mode = configuration[NativeCoverageRf3Protocol.ModeSetting];
        var manifest = configuration[NativeCoverageRf3Protocol.SourceManifestSetting];
        if (mode is null && manifest is null)
        {
            return null;
        }
        if (mode != NativeCoverageRf3Protocol.Mode || suite != TestSuiteProtocol.Rf3Suite
            || filter != NativeCoverageRf3Protocol.Filter || settings is null || output is null
            || format != NativeCoverageProtocol.BinaryFormat || manifest is null)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        var admission = NativeCoverageRf3ManifestReader.ReadAsync(manifest, options, CancellationToken.None)
            .GetAwaiter().GetResult();
        var sourceRevision = Environment.GetEnvironmentVariable(NativeCoverageRf3Protocol.GithubShaEnvironment);
        if (!string.Equals(sourceRevision, admission.SourceRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        return new(admission, filter);
    }
}
