using P = KeyLoad.FunctionalTests.ClientApi.ResponseSuffixProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class ResponseSuffixTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task CompleteDtoWithTrailingJsonRefusesWholeResponseThenReplaysAndAdvancesHealthy(bool declareLength)
        => SuccessBodyFlow.RunAsync(P.SecondRoot, appendPrefix: true, declareLength: declareLength);
}
