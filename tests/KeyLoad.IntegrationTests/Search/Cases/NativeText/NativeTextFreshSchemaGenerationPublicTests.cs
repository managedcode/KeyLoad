using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "native-text-fresh-schema-generation")]
[NotInParallel]
internal sealed class NativeTextFreshSchemaGenerationPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task NewConsumerBuildAfterSchemaChangeKeepsOldSelectionFencedAndFullLiteralPagesAcrossTwoColdCycles()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            await ServerFailureObserver.ObserveAsync(
                () => NativeTextFreshSchemaGenerationPublicTrial.RunAsync(fixture, linked.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
