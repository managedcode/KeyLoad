using System.Globalization;
using KeyLoad.Comparisons;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Retains actual local execution or original workflow provenance for the same execution fault workload.</summary>
[ConfigurationBinding]
internal static class IsolatedKeyLoadFaultChecksProvider
{
    private const string Repository = "managedcode/KeyLoad";
    private const string Main = "refs/heads/main";
    private const string Workflow = "Benchmarks";
    private const string WorkflowRef = "managedcode/KeyLoad/.github/workflows/benchmarks.yml@refs/heads/main";
    private const string WorkflowRefSetting = "GITHUB_WORKFLOW_REF";

    internal static IsolatedKeyLoadFaultChecksEvidence Read(int nodes)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        var selected = BenchmarkSessionCallerOptions.Read(configuration).Value;
        var selection = BenchmarkSessionContract.ResourceSelection(selected.Target, selected.NodeCount);
        var provenance = BenchmarkProvenanceRegistration.Bind().Value;
        IsolatedKeyLoadFaultChecksProtocol.Require(selection.Target == "KeyLoad" && selection.NodeCount == nodes);
        if (!string.Equals(provenance.Actions, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            return new(selected.SessionId, new(selection.Target, nodes, BenchmarkSessionContract.Profile,
                null, null, null, null, null, null, null));
        }
        var source = provenance.SourceRevision;
        IsolatedKeyLoadFaultChecksProtocol.Require(source is { Length: 40 }
            && source.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && provenance.Repository == Repository && provenance.Reference == Main && provenance.Workflow == Workflow
            && configuration[WorkflowRefSetting] == WorkflowRef
            && selected.SessionId == "keyload-n" + nodes.ToString(CultureInfo.InvariantCulture));
        var attempt = Positive(provenance.RunAttempt);
        IsolatedKeyLoadFaultChecksProtocol.Require(attempt <= int.MaxValue);
        return new(selected.SessionId, new(selection.Target, nodes, BenchmarkSessionContract.Profile, source,
            Positive(provenance.WorkflowRunId), checked((int)attempt), Repository, Main, Workflow, Positive(provenance.JobId)));
    }

    private static long Positive(string? value)
    {
        IsolatedKeyLoadFaultChecksProtocol.Require(long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
            out var result) && result > 0);
        return result;
    }
}
