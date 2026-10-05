using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Admits one centrally validated native open-loop dispatch.</summary>
[ConfigurationBinding]
internal sealed record IsolatedOpenLoopSettings(ScaledComparisonProfile Profile, int Rate,
    bool CancellationProof, IOptions<OpenLoopExecutionOptions> ExecutionOptions)
{
    internal const string ProofSetting = "Benchmarks:OpenLoopCancellationProof";
    internal const string ProofEnvironment = "Benchmarks__OpenLoopCancellationProof";
    internal const string ProofSelected = "true";
    private const int ProofNodeCount = 3;

    internal static IsolatedOpenLoopSettings? Read(IConfiguration configuration, ComparisonWorkerSelection selection)
    {
        var proofText = configuration[ProofSetting];
        if (selection.OpenLoopRate is not { } rate)
        {
            if (proofText is not null) { throw Invalid(); }
            return null;
        }
        if (proofText is not null && proofText != ProofSelected) { throw Invalid(); }
        var profile = selection.ScaledProfile ?? throw Invalid();
        var proof = proofText is not null;
        if (proof && (selection.Target != IsolatedHostConstants.KeyLoad
            || selection.NodeCount != ProofNodeCount || selection.Scenario != Scenario.PointRead))
        {
            throw Invalid();
        }
        return new(profile, rate, proof, OpenLoopExecutionRegistration.Read(configuration));
    }

    private static InvalidOperationException Invalid() => new(IsolatedHostConstants.Failure);
}
