using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadFaultRegressionProvider
{
    private const string CellEnvironment = "KEYLOAD_COMPARISON_CELL_ID";
    private const string JobEnvironment = "KEYLOAD_COMPARISON_JOB_ID";
    private const string WorkflowEnvironment = "GITHUB_WORKFLOW";
    private const string WorkflowRefEnvironment = "GITHUB_WORKFLOW_REF";
    private const string Repository = "managedcode/KeyLoad";
    private const string Main = "refs/heads/main";
    private const string Workflow = "Benchmarks";
    private const string WorkflowRef = "managedcode/KeyLoad/.github/workflows/benchmarks.yml@refs/heads/main";

    internal static IsolatedKeyLoadFaultRegressionEvidence Read(int nodes)
    {
        var selection = ComparisonWorkerSelection.Read(new ConfigurationBuilder().AddEnvironmentVariables().Build());
        var source = Required(ComparisonImageProtocol.ShaEnvironment);
        var cell = Required(CellEnvironment);
        IsolatedKeyLoadFaultRegressionProtocol.Require(selection.Target == "KeyLoad" && selection.NodeCount == nodes
            && selection.Scenario == Scenario.PointRead && IsolatedKeyLoadFaultRegressionNativeIdentity.Hex(source, 40)
            && Required(ComparisonImageProtocol.RepositoryEnvironment) == Repository
            && Required(ComparisonImageProtocol.RefEnvironment) == Main && Required(WorkflowEnvironment) == Workflow
            && Required(WorkflowRefEnvironment) == WorkflowRef
            && cell == "keyload-n" + nodes.ToString(CultureInfo.InvariantCulture) + "-point-read");
        var attempt = Positive(ComparisonImageProtocol.AttemptEnvironment);
        IsolatedKeyLoadFaultRegressionProtocol.Require(attempt <= int.MaxValue);
        return new(cell, new(selection.Target, nodes, selection.Scenario, selection.Profile, source,
            Positive(ComparisonImageProtocol.RunEnvironment), checked((int)attempt), Repository, Main, Workflow, Positive(JobEnvironment)));
    }

    private static string Required(string name) => ComparisonImageProtocol.RequiredEnvironment(name);

    private static long Positive(string name)
    {
        IsolatedKeyLoadFaultRegressionProtocol.Require(long.TryParse(Required(name), NumberStyles.None, CultureInfo.InvariantCulture,
            out var result) && result > 0);
        return result;
    }
}
