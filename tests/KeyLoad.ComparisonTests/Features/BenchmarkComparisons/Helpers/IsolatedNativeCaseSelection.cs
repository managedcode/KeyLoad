using KeyLoad.AppHost.Hosting;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[ConfigurationBinding]
internal static class IsolatedNativeCaseSelection
{
    internal const string ProofSetting = "Benchmarks:OpenLoopCancellationProof";
    internal const string ProofArgument = "--Benchmarks:OpenLoopCancellationProof=true";
    internal const string RateArgument = "--Benchmarks:OpenLoopRate=";
    private const string InvalidIntent = "IsolatedNativeCaseIntentInvalid";
    private const string ProofTarget = "KeyLoad";
    private const int ProofNodeCount = 3;

    internal static IsolatedNativeCasePlan Read(IConfiguration configuration, IsolatedNativeCaseIntent intent)
    {
        var selection = ComparisonWorkerSelection.Read(configuration);
        if (!Enum.IsDefined(intent) || configuration[ProofSetting] is not null
            || (intent == IsolatedNativeCaseIntent.ClosedLoop) != (selection.OpenLoopRate is null)
            || intent == IsolatedNativeCaseIntent.OpenLoopCancellationProof
                && (selection.Target != ProofTarget || selection.NodeCount != ProofNodeCount
                    || selection.Scenario != Scenario.PointRead))
        {
            throw new InvalidOperationException(InvalidIntent);
        }
        return new(selection, intent, AppHostOptionsRegistration.BindTestExecution(configuration));
    }
}
