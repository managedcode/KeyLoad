using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record IsolatedNativeCasePlan(ComparisonWorkerSelection Selection,
    IsolatedNativeCaseIntent Intent, IOptions<TestExecutionOptions> ExecutionOptions)
{
    internal bool CancellationProof => Intent == IsolatedNativeCaseIntent.OpenLoopCancellationProof;
    internal bool OpenLoop => Intent != IsolatedNativeCaseIntent.ClosedLoop;
    internal TimeSpan Timeout => Selection.VectorProfile is not null
        ? ExecutionOptions.Value.NativeVectorTimeout
        : Selection.ScaledProfile is not null ? ExecutionOptions.Value.NativeScaledTimeout
        : ExecutionOptions.Value.NativeControlTimeout;
}
