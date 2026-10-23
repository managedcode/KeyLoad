using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Validates the selected execution session and retains original workflow provenance when present.</summary>
[KeyLoad.ConfigurationBinding]
internal sealed record BenchmarkSessionHostSettings(string SessionId, IsolatedHostSettings Execution)
{
    internal const string Profile = BenchmarkSessionContract.Profile;
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
        if (!int.TryParse(IsolatedHostSettings.Required(configuration, BenchmarkSessionSelection.NodeCountSetting),
            NumberStyles.None, CultureInfo.InvariantCulture, out var nodes))
        {
            throw new InvalidOperationException(Profile);
        }
        var selection = BenchmarkSessionContract.ResourceSelection(target, nodes);
        var revision = configuration[ComparisonHostConstants.SourceRevision];
        var identity = ComparisonExecutionIdentity.ReadSession(configuration, revision, selection.Topology);
        long? job = null;
        if (identity.Provenance is not null)
        {
            if (!long.TryParse(IsolatedHostSettings.Required(configuration, IsolatedHostConstants.JobId),
                NumberStyles.None, CultureInfo.InvariantCulture, out var parsedJob))
            {
                throw new InvalidOperationException(Profile);
            }
            job = parsedJob;
        }
        var output = IsolatedHostSettings.Required(configuration, ComparisonHostConstants.Output);
        var unsupported = BenchmarkSessionContract.UnsupportedTopologies
            .SingleOrDefault(item => item.Target == target && item.NodeCounts.Contains(nodes))?.Reason;
        var image = target == IsolatedHostConstants.KeyLoad || unsupported is not null ? null
            : IsolatedHostSettings.Required(configuration, IsolatedHostConstants.Image);
        var execution = ComparisonExecutionRegistration.ReadHost(configuration);
        if (job is <= 0 || !Path.IsPathFullyQualified(output)
            || target != IsolatedHostConstants.KeyLoad && unsupported is null && !ComparisonExecutionIdentityImageReference.IsValid(image!, execution))
        {
            throw new InvalidOperationException(Profile);
        }
        var provenance = identity.Provenance;
        var worker = new BenchmarkSessionWorker(target, nodes, provenance is null ? null : revision,
            provenance?.RunId, provenance?.Attempt, provenance?.Repository, provenance?.Ref, provenance?.Workflow, job);
        return new(IsolatedHostSettings.Required(configuration, SessionIdSetting),
            new(selection, identity, worker, Path.GetFullPath(output),
                IsolatedHostSettings.Required(configuration, ComparisonHostConstants.Storage),
                Guid.NewGuid().ToString(ComparisonHostConstants.GuidFormat), unsupported,
                unsupported is null ? RuntimeIsolatedHostSettings.Read(configuration, selection, image) : null, execution));
    }
}
