using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal sealed class StorageBatchAcknowledgementFailureTests
{
    private const string Collection = "batch-acknowledgement-failures";
    private const string Document = "original";
    private const string Json = "{\"version\":1}";
    private const string Updated = "{\"version\":2}";

    [Test]
    [Arguments(StorageBatchAcknowledgementFailure.Sequence)]
    [Arguments(StorageBatchAcknowledgementFailure.Decision)]
    [Arguments(StorageBatchAcknowledgementFailure.Prefix)]
    [Arguments(StorageBatchAcknowledgementFailure.MissingOutcome)]
    public Task InvalidAcknowledgementLeavesTheWriteUnknownThenExactRetryAndHealthyWriteSucceed(
        StorageBatchAcknowledgementFailure failure)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var commandId = Guid.NewGuid();
        var fault = new StorageBatchAcknowledgementFault(commandId, failure);
        return RequestCqrsHostedCluster.RunAsync(cluster => ExecuteAsync(cluster, commandId, token),
            token, configureServices: (_, services, _) => services.AddSingleton<IOutgoingGrainCallFilter>(fault));
    }

    private static async Task ExecuteAsync(RequestCqrsHostedCluster cluster, Guid commandId, CancellationToken token)
    {
        var source = cluster.Nodes[0];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(source.Timing.CompletionTimeout);
        var bounded = timeout.Token;
        var creation = Guid.NewGuid();
        source.Database.Configure(Collection, ResourceKind.Collection, id: creation);
        var before = source.Database.ReadCanonicalCut().AppliedPosition;
        var request = new CommandRequest(commandId, source.Database.Partition, [new PutDocument(Collection, Document, Json, 0)]);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            source.Database.Submit(OperationKind.Batch, request, id: commandId, cancellationToken: bounded));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await RequireCopiesAsync(cluster, creation, before + 1, Json, 1);
        var replayed = source.Database.Submit(OperationKind.Batch, request, id: commandId,
            cancellationToken: bounded).Get<CommitReceipt>();
        await Assert.That(replayed.PersistedCopies).IsEqualTo(cluster.Nodes.Length);
        await Assert.That(replayed.Token.Position).IsEqualTo(before + 1);
        await RequireCopiesAsync(cluster, creation, before + 1, Json, 1);
        var healthyId = Guid.NewGuid();
        var healthy = new CommandRequest(healthyId, source.Database.Partition,
            [new PutDocument(Collection, Document, Updated, 1, ExplicitReplacement: true)]);
        await Assert.That(source.Database.Submit(OperationKind.Batch, healthy, id: healthyId,
            cancellationToken: bounded).Get<CommitReceipt>().PersistedCopies).IsEqualTo(cluster.Nodes.Length);
        await RequireCopiesAsync(cluster, creation, before + 2, Updated, 2);
    }

    private static async Task RequireCopiesAsync(RequestCqrsHostedCluster cluster, Guid creation,
        long sequence, string json, long revision)
    {
        foreach (var node in cluster.Nodes)
        {
            await Assert.That(node.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(sequence);
            var reference = node.Database.Reference(creation, Collection, Document);
            var document = node.Database.Database.GetDocument(RequestCqrsHostedProfile.Root, reference);
            await Assert.That(document).IsNotNull();
            await Assert.That(document!.Json).IsEqualTo(json);
            await Assert.That(document.Revision).IsEqualTo(revision);
        }
    }
}
