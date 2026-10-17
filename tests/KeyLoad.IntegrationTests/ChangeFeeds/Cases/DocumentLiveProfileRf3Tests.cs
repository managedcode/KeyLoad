using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

[NotInParallel]
internal sealed class DocumentLiveProfileRf3Tests
{
    [Test]
    [Arguments(ModelQuerySourceKind.Events)]
    [Arguments(ModelQuerySourceKind.QueueMessages)]
    [Arguments(ModelQuerySourceKind.TopicEvents)]
    public async Task ModelRefusalPreservesAuthorizedBoundedDocumentPollingAcrossTwoColdOwners(ModelQuerySourceKind kind)
        => await DocumentLiveProfileRf3Flow.RunAsync(kind);
}
