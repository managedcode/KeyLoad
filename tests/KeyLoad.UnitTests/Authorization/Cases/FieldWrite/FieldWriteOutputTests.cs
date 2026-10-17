using KeyLoad.UnitTests.Features.ClusterReplication;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class FieldWriteOutputTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FreshEscapedAndAncestorPatchSeparatesPersistedWriteFromRawOutputAcrossRepairReplayAndTwoCold(bool ancestor)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.ShutdownBound, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        await FieldWriteOutputFlow.RunAsync(ancestor, caller.Token);
    }
}
