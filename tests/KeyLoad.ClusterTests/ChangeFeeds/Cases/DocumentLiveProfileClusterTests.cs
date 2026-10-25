using KeyLoad.Query;
namespace KeyLoad.ClusterTests.ChangeFeeds;

[NotInParallel]
internal sealed class DocumentLiveProfileClusterTests
{
    [Test]
    [Arguments(ModelQuerySourceKind.Events)]
    [Arguments(ModelQuerySourceKind.QueueMessages)]
    [Arguments(ModelQuerySourceKind.TopicEvents)]
    public async Task ModelRefusalPreservesAuthorizedBoundedDocumentPollingAcrossTwoColdOwners(ModelQuerySourceKind kind)
        => await DocumentLiveProfileClusterFlow.RunAsync(kind);
}
