using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "execution-text-nested-wildcard-public")]
[NotInParallel]
internal sealed class TextNestedWildcardPublicTests(ClusterFixture fixture)
{
    [Test]
    public async Task NestedWildcardRawReadAndFieldUseEpochsPreserveLiteralPublicPagesAndOriginalReceipt()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await ServerFailureObserver.ObserveAsync(
                () => TextNestedWildcardPublicTrial.RunAsync(fixture, deadline.Token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
