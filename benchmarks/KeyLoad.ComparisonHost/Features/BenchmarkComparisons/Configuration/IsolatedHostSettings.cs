using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Validated single-cell settings, bound to actual GitHub and immutable image identity.</summary>
internal sealed record IsolatedHostSettings(ComparisonWorkerSelection Selection, ComparisonExecutionIdentity Identity,
    IsolatedComparisonWorker Worker, string OutputDirectory, string Storage, string RunId,
    string? UnsupportedReason, IsolatedHostNativeSettings? Native)
{
    internal static IsolatedHostSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var selection = ComparisonWorkerSelection.Read(configuration);
        var revision = Required(configuration, ComparisonHostConstants.SourceRevision);
        var identity = ComparisonExecutionIdentity.ReadIsolated(configuration, revision, selection.Options.Topology,
            selection.ScaledProfile?.Id)
            ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        var jobText = Required(configuration, IsolatedHostConstants.JobId);
        if (!long.TryParse(jobText, NumberStyles.None, CultureInfo.InvariantCulture, out var jobId) || jobId <= 0)
        {
            throw new InvalidOperationException(IsolatedHostConstants.Failure);
        }
        var image = Required(configuration, IsolatedHostConstants.Image);
        if (!ComparisonExecutionIdentityImageReference.IsValid(image)
            || selection.Target == IsolatedHostConstants.KeyLoad && image != identity.KeyLoadImage)
        {
            throw new InvalidOperationException(IsolatedHostConstants.Failure);
        }
        var output = Required(configuration, ComparisonHostConstants.Output);
        if (!Path.IsPathFullyQualified(output))
        {
            throw new InvalidOperationException(IsolatedHostConstants.Failure);
        }
        var provenance = identity.Provenance;
        var worker = new IsolatedComparisonWorker(selection.Target, selection.NodeCount, selection.Scenario,
            selection.Profile, revision, provenance.RunId, provenance.Attempt, provenance.Repository,
            provenance.Ref, provenance.Workflow, jobId);
        var unsupported = IsolatedComparisonContract.Current.UnsupportedTopologies
            .SingleOrDefault(item => item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount))?.Reason;
        return new(selection, identity, worker, Path.GetFullPath(output), Required(configuration, ComparisonHostConstants.Storage),
            Guid.NewGuid().ToString(ComparisonHostConstants.GuidFormat), unsupported,
            unsupported is null ? IsolatedHostNativeSettings.Read(configuration, selection, image) : null);
    }

    internal static string Required(IConfiguration configuration, string key)
        => string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException(IsolatedHostConstants.Failure)
            : configuration[key]!;
}
