using P = KeyLoad.UnitTests.Authorization.RowRevisionInferenceProtocol;

namespace KeyLoad.UnitTests.Authorization;

internal sealed class RowRevisionBatchInferenceTests
{
    [Test]
    [Arguments(P.Put)]
    [Arguments(P.Patch)]
    [Arguments(P.Delete)]
    public async Task AcAuthRowRevisionBatch001HiddenRevisionRefusalRollsBackEarlierOwnWriteAndRepairReplaysAcrossColdCuts(string kind)
        => await RowRevisionBatchInferenceFlow.RunAsync(kind, TestContext.Current!.Execution.CancellationToken);
}
