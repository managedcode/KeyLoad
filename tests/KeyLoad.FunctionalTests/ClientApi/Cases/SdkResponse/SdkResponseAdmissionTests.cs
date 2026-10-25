namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class SdkResponseAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task WriteReceiptAndDocumentResponseRespectInclusiveSdkAdmission(bool contentLength)
        => SdkResponseAdmissionFlow.RunAsync(contentLength);
}
