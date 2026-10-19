namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SdkResponseAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task WriteReceiptAndDocumentResponseRespectInclusiveSdkAdmission(bool contentLength)
        => SdkResponseAdmissionFlow.RunAsync(contentLength);
}
