using KeyLoad.Server;

namespace KeyLoad.ClusterTests.ClientApi;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "sdk-problem-budget-public")]
[NotInParallel]
internal sealed class SdkProblemBudgetPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task MinimumErrorBudgetPreservesDeniedDataThenHealthyReceiptAcrossTwoColdCycles()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            await ServerFailureObserver.ObserveAsync(
                () => SdkProblemBudgetPublicTrial.RunAsync(fixture, linked.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
