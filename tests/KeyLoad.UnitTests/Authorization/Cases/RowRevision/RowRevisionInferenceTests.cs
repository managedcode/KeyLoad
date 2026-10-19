using P = KeyLoad.UnitTests.Features.Authorization.RowRevisionInferenceProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class RowRevisionInferenceTests
{
    [Test]
    [Arguments(P.Put)]
    [Arguments(P.Patch)]
    [Arguments(P.Delete)]
    public async Task HiddenRowRevisionGuessesStayDeniedBeforeCasAndOwnedRepairReplaysAcrossTwoColdCuts(string kind)
        => await RowRevisionInferenceFlow.RunAsync(kind, TestContext.Current!.Execution.CancellationToken);
}
