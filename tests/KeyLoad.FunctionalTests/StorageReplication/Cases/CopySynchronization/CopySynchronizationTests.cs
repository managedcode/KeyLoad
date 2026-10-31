using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal sealed class CopySynchronizationTests
{
    private const string Collection = "background-copy-documents";
    private const string Document = "retained-original";
    private const string Json = "{\"value\":42}";
    private const string Healthy = "healthy-after-repair";
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    [Test]
    public Task InterruptedCopyCompletesWithoutAnotherWriteAndReadBarriersContinueDuringRepair()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var commandId = Guid.NewGuid();
        var fault = new StorageCopyCallFault(commandId);
        return RequestCqrsHostedCluster.RunAsync(cluster => ExecuteAsync(cluster, fault, commandId, token), token,
            configureServices: (_, services, _) =>
            {
                services.Configure<ReplicaCopyOptions>(options => options.SynchronizationInterval = Interval);
                services.AddSingleton<IOutgoingGrainCallFilter>(fault);
            });
    }

    private static async Task ExecuteAsync(RequestCqrsHostedCluster cluster, StorageCopyCallFault fault,
        Guid commandId, CancellationToken token)
    {
        var source = cluster.Nodes[0];
        var replica = cluster.Nodes[1];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(source.Timing.CompletionTimeout);
        var bounded = timeout.Token;
        var creation = Guid.NewGuid();
        source.Database.Configure(Collection, ResourceKind.Collection, id: creation);
        var before = source.Database.ReadCanonicalCut().AppliedPosition;
        var command = new CommandRequest(commandId, source.Database.Partition, [new PutDocument(Collection, Document, Json)]);
        try
        {
            var interrupted = Assert.ThrowsExactly<KeyLoadException>(() =>
                source.Database.Submit(OperationKind.Batch, command, id: commandId, cancellationToken: bounded));
            await Assert.That(interrupted.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await fault.RepairArrived.Task.WaitAsync(bounded);
            await Assert.That(fault.Sends).IsEqualTo(3);
            await Assert.That(source.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(before + 1);
            await Assert.That(replica.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(before);
            await source.Database.Coordinator.ReadBarrierAsync(bounded);
            await RequireDocumentAsync(source, creation);
        }
        finally
        {
            fault.ReleaseRepair();
        }
        await fault.RepairSettled.Task.WaitAsync(bounded);
        await RequireDocumentAsync(replica, creation);
        await Assert.That(replica.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(before + 1);
        var retried = source.Database.Submit(OperationKind.Batch, command, id: commandId, cancellationToken: bounded);
        await Assert.That(retried.IsSuccess).IsTrue();
        await Assert.That(retried.Get<CommitReceipt>().PersistedCopies).IsEqualTo(cluster.Nodes.Length);
        await Assert.That(source.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(before + 1);
        await Assert.That(replica.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(before + 1);
        var healthyId = Guid.NewGuid();
        var healthy = new CommandRequest(healthyId, source.Database.Partition, [new PutDocument(Collection, Healthy, Json)]);
        await Assert.That(source.Database.Submit(OperationKind.Batch, healthy, id: healthyId,
            cancellationToken: bounded).IsSuccess).IsTrue();
    }

    private static async Task RequireDocumentAsync(RequestCqrsHostedRuntimeFixture node, Guid creation)
    {
        var reference = node.Database.Reference(creation, Collection, Document);
        var document = node.Database.Database.GetDocument(RequestCqrsHostedProfile.Root, reference);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Json).IsEqualTo(Json);
        await Assert.That(document.Revision).IsEqualTo(1L);
    }
}
