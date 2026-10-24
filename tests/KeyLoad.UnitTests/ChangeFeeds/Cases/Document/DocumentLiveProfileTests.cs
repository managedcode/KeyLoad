using KeyLoad.Query;

namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed class DocumentLiveProfileTests
{
    [Test]
    [Arguments(ModelQuerySourceKind.Events)]
    [Arguments(ModelQuerySourceKind.QueueMessages)]
    [Arguments(ModelQuerySourceKind.TopicEvents)]
    public Task UnsupportedModelCannotBecomeDocumentSubscriptionAndOrdinaryProtectedTailRemainsResumable(ModelQuerySourceKind kind)
        => DocumentLiveProfileFlow.RunAsync(kind, TestContext.Current!.Execution.CancellationToken);
}
