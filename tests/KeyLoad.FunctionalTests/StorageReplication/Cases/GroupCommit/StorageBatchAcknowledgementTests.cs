using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.StorageReplication;

internal sealed class StorageBatchAcknowledgementTests
{
    private const string Collection = "batch-acknowledgements";
    private const string Json = "{\"value\":42}";
    private const int Documents = 64;

    [Test]
    public Task CopiesConfirmEveryDecisionWhileOnlyOneReturnsMutationPayloads()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var commandId = Guid.NewGuid();
        var observation = new StorageBatchAcknowledgementObservation(commandId);
        return RequestCqrsHostedCluster.RunAsync(cluster => ExecuteAsync(cluster, observation, commandId, token),
            token, configureServices: (_, services, _) => services.AddSingleton<IOutgoingGrainCallFilter>(observation));
    }

    private static async Task ExecuteAsync(RequestCqrsHostedCluster cluster, StorageBatchAcknowledgementObservation observation,
        Guid commandId, CancellationToken token)
    {
        var source = cluster.Nodes[0];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(source.Timing.CompletionTimeout);
        var bounded = timeout.Token;
        var creation = Guid.NewGuid();
        source.Database.Configure(Collection, ResourceKind.Collection, id: creation);
        await StorageCoordinatorPlacement.ActivateAsync(cluster, remote: true, bounded);
        var before = source.Database.ReadCanonicalCut().AppliedPosition;
        var mutations = Enumerable.Range(0, Documents).Select(index => (Mutation)new PutDocument(Collection, Id(index), Json, 0))
            .ToImmutableArray();
        var request = new CommandRequest(commandId, source.Database.Partition, mutations);
        var receipt = source.Database.Submit(OperationKind.Batch, request, id: commandId, cancellationToken: bounded).Get<CommitReceipt>();
        await Assert.That(receipt.PersistedCopies).IsEqualTo(cluster.Nodes.Length);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(Documents);
        var replies = observation.Replies;
        await Assert.That(replies.Length).IsEqualTo(cluster.Nodes.Length);
        var full = replies.Single(reply => reply.Outcome is not null);
        var compact = replies.Single(reply => reply.Outcome is null);
        await Assert.That(compact.Position).IsEqualTo(full.Position);
        await Assert.That(compact.Applied).IsEquivalentTo(full.Applied, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(compact.Applied.Single().Sequence).IsEqualTo(before + 1);
        await Assert.That(compact.Applied.Single().Succeeded).IsTrue();
        await Assert.That(compact.Applied.Single().Error).IsNull();
        var serializer = source.Services.GetRequiredService<Serializer<StorageCopyBatchAcknowledgement>>();
        await Assert.That(serializer.SerializeToArray(compact).Length).IsLessThan(serializer.SerializeToArray(full).Length / 4);
        var replayed = source.Database.Submit(OperationKind.Batch, request, id: commandId, cancellationToken: bounded).Get<CommitReceipt>();
        await Assert.That(OrleansSerialization.Serialize(replayed).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        foreach (var node in cluster.Nodes)
        {
            await Assert.That(node.Database.ReadCanonicalCut().AppliedPosition).IsEqualTo(before + 1);
            for (var index = 0; index < Documents; index++)
            {
                await RequireDocumentAsync(node, creation, Id(index));
            }
        }
        var healthyId = Guid.NewGuid();
        var healthy = new CommandRequest(healthyId, source.Database.Partition, [new PutDocument(Collection, "healthy", Json)]);
        await Assert.That(source.Database.Submit(OperationKind.Batch, healthy, id: healthyId,
            cancellationToken: bounded).Get<CommitReceipt>().PersistedCopies).IsEqualTo(cluster.Nodes.Length);
        foreach (var node in cluster.Nodes)
        {
            await RequireDocumentAsync(node, creation, "healthy");
        }
    }

    private static async Task RequireDocumentAsync(RequestCqrsHostedRuntimeFixture node, Guid creation, string id)
    {
        var reference = node.Database.Reference(creation, Collection, id);
        var document = node.Database.Database.GetDocument(RequestCqrsHostedProfile.Root, reference);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Json).IsEqualTo(Json);
        await Assert.That(document.Revision).IsEqualTo(1L);
    }

    private static string Id(int index) => "document-" + index.ToString(CultureInfo.InvariantCulture);
}
