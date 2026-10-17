using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Authenticates one source-bound session before opening native clients.</summary>
[KeyLoad.ConfigurationBinding]
internal sealed record BenchmarkSessionHostSettings(string SessionId, IsolatedHostSettings Native)
{
    internal const string Profile = BenchmarkSessionContract.Profile;
    private const int MissingJobId = 0;
    private const string SessionSetting = "Benchmarks:Session";
    private const string SessionIdSetting = "Benchmarks:SessionId";
    private const string SectionPrefix = "Benchmarks:";
    internal static BenchmarkSessionHostSettings Read(IConfiguration configuration)
        => ComparisonHostOptionsRegistration.Bind(() => ReadValidated(configuration)).Value;

    private static BenchmarkSessionHostSettings ReadValidated(IConfiguration configuration)
    {
        if (!string.Equals(configuration[SessionSetting], bool.TrueString, StringComparison.OrdinalIgnoreCase)
            || BenchmarkSessionContract.ForbiddenOverrides.Any(name => !string.IsNullOrEmpty(configuration[SectionPrefix + name])))
        {
            throw new InvalidOperationException(Profile);
        }
        var target = IsolatedHostSettings.Required(configuration, BenchmarkSessionSelection.TargetSetting);
        var nodes = int.Parse(IsolatedHostSettings.Required(configuration, BenchmarkSessionSelection.NodeCountSetting),
            NumberStyles.None, CultureInfo.InvariantCulture);
        var selection = BenchmarkSessionContract.ResourceSelection(target, nodes);
        var revision = IsolatedHostSettings.Required(configuration, ComparisonHostConstants.SourceRevision);
        var identity = ComparisonExecutionIdentity.ReadSession(configuration, revision, selection.Topology);
        var job = long.Parse(IsolatedHostSettings.Required(configuration, IsolatedHostConstants.JobId),
            NumberStyles.None, CultureInfo.InvariantCulture);
        var output = IsolatedHostSettings.Required(configuration, ComparisonHostConstants.Output);
        var image = IsolatedHostSettings.Required(configuration, IsolatedHostConstants.Image);
        var execution = NativeComparisonExecutionRegistration.ReadHost(configuration);
        if (job <= MissingJobId || !Path.IsPathFullyQualified(output)
            || !ComparisonExecutionIdentityImageReference.IsValid(image, execution)
            || target == IsolatedHostConstants.KeyLoad && image != identity.KeyLoadImage)
        {
            throw new InvalidOperationException(Profile);
        }
        var provenance = identity.Provenance;
        var worker = new BenchmarkSessionWorker(target, nodes, revision,
            provenance.RunId, provenance.Attempt, provenance.Repository, provenance.Ref, provenance.Workflow, job);
        var unsupported = BenchmarkSessionContract.UnsupportedTopologies
            .SingleOrDefault(item => item.Target == target && item.NodeCounts.Contains(nodes))?.Reason;
        return new(IsolatedHostSettings.Required(configuration, SessionIdSetting),
            new(selection, identity, worker, Path.GetFullPath(output),
                IsolatedHostSettings.Required(configuration, ComparisonHostConstants.Storage),
                Guid.NewGuid().ToString(ComparisonHostConstants.GuidFormat), unsupported,
                unsupported is null ? IsolatedHostNativeSettings.Read(configuration, selection, image) : null, execution));
    }
}
