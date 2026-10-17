using P = KeyLoad.UnitTests.Features.ClientApi.NativeResponseSuffixProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeResponseSuffixTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task CompleteNativeDtoWithTrailingJsonRefusesWholeResponseThenReplaysAndAdvancesHealthy(bool declareLength)
        => NativeSuccessBodyFlow.RunAsync(P.SecondRoot, appendNativePrefix: true, declareLength: declareLength);
}
