using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal static class StorageOwnerDiscoveryScenario
{
    private const string Collection = "owner-cut-documents";
    private const string First = "first-after-discovery";
    private const string Following = "following-command";
    private const string Json = "{\"value\":42}";
    private const int DiscoveryCalls = 4;

    internal static async Task RunAsync(RequestCqrsHostedCluster cluster, StorageOwnerDiscoveryCallFault observation,
        CancellationToken token)
    {
        var source = cluster.Nodes[0];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(source.Timing.CompletionTimeout);
        var bounded = timeout.Token;
        var creation = Guid.NewGuid();
        source.Database.Configure(Collection, ResourceKind.Collection, id: creation);
        var before = source.Database.ReadCanonicalCut().AppliedPosition;
        var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            SubmitAsync(source, observation.CommandId, First, bounded));
        await Assert.That(refused).IsNotNull();
        await Assert.That(refused!.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(source.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(before);
        observation.Arm();
        var firstId = Guid.NewGuid();
        var original = SubmitAsync(source, firstId, First, bounded);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                await observation.Arrived.Task.WaitAsync(bounded);
                await source.Database.Coordinator.ReadBarrierAsync(bounded);
                await Assert.That(observation.CutCalls).IsEqualTo(DiscoveryCalls);
            }
            finally
            {
                observation.ReleaseDiscovery();
            }
        }, failures);
        Result<object> first = default;
        await ServerFailureObserver.ObserveAsync(async () => first = await original, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(first.Get<CommitReceipt>().PersistedCopies).IsEqualTo(cluster.Nodes.Length);
        var following = await SubmitAsync(source, Guid.NewGuid(), Following, bounded);
        await Assert.That(following.IsSuccess).IsTrue();
        await RequireCopiesAsync(cluster, creation, before + 2);
        var retry = await SubmitAsync(source, firstId, First, bounded);
        await Assert.That(retry.IsSuccess).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(retry.Get<CommitReceipt>())
            .SequenceEqual(OrleansSerialization.Serialize(first.Get<CommitReceipt>()))).IsTrue();
        await RequireCopiesAsync(cluster, creation, before + 2);
    }

    private static Task<Result<object>> SubmitAsync(RequestCqrsHostedRuntimeFixture node, Guid id, string document,
        CancellationToken token)
        => node.Database.Coordinator.RuntimeSubmitAsync(OperationKind.Batch, id, RequestCqrsHostedProfile.Root,
            OrleansSerialization.Serialize(new CommandRequest(id, node.Database.Partition,
                [new PutDocument(Collection, document, Json)])), cancellationToken: token);

    private static async Task RequireCopiesAsync(RequestCqrsHostedCluster cluster, Guid creation, long sequence)
    {
        foreach (var node in cluster.Nodes)
        {
            await Assert.That(node.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(sequence);
            foreach (var id in new[] { First, Following })
            {
                var reference = node.Database.Reference(creation, Collection, id);
                var document = node.Database.Database.GetDocument(RequestCqrsHostedProfile.Root, reference);
                await Assert.That(document).IsNotNull();
                await Assert.That(document!.Json).IsEqualTo(Json);
                await Assert.That(document.Revision).IsEqualTo(1L);
            }
        }
    }
}
