namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SdkResponseAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task NativeWriteReceiptAndDocumentResponseRespectInclusiveSdkAdmission(bool contentLength)
        => SdkResponseAdmissionFlow.RunAsync(contentLength);
}
