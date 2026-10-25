using KeyLoad.Core.GraphTraversal.Serialization;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;
using D = KeyLoad.UnitTests.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class GraphMutationEffectClosureFlow
{
    private const string Delete = "deleteEdge";
    private const int DeletedRevision = 3;

    internal static Task RunAsync(bool stagedReplacement, CancellationToken token)
        => CompositionPrefixPrepareOwner.RunAsync(null, async owner =>
        {
            await CompositionPrefixPrepareSeed.ConfigureAsync(owner);
            await CompositionPrefixPrepareSeed.VerticesAsync(owner, first: !stagedReplacement);
            var from = CompositionPrefixPrepareSeed.Fresh(owner, D.From, D.First);
            var to = CompositionPrefixPrepareSeed.Fresh(owner, D.To, D.Second);
            var command = stagedReplacement
                ? await CompositionPrefixPrepareCommands.CommitAsync(owner,
                    [new PutDocument(D.From, D.First, D.Json, D.Absent),
                     new UpsertEdge(D.Graph, D.Edge, from, to, D.Label, ExpectedRevision: D.Absent),
                     new UpsertEdge(D.Graph, D.Edge, from, from, D.Label, ExpectedRevision: D.FirstRevision),
                     new DeleteEdge(D.Graph, D.Edge, D.CancelRevision)],
                    [new(D.Put, D.From, D.First, D.FirstRevision),
                     new(D.Upsert, D.Graph, D.Edge, D.FirstRevision),
                     new(D.Upsert, D.Graph, D.Edge, D.CancelRevision),
                     new(Delete, D.Graph, D.Edge, DeletedRevision)])
                : await CompositionPrefixPrepareCommands.CommitAsync(owner,
                    [new UpsertEdge(D.Graph, D.Edge, from, to, D.Label, ExpectedRevision: D.Absent)],
                    [new(D.Upsert, D.Graph, D.Edge, D.FirstRevision)]);
            var admission = ResourceEffectAdmissionValidation.Read(command.Original);
            await Assert.That(admission.Error).IsNull();
            await Assert.That(admission.Bindings.ToHashSet().SetEquals(new[] { D.From, D.To, D.Graph }
                .Select(name => owner.Lifetimes[name]))).IsTrue();
            await CompositionPrefixPrepareAssertions.ColdAsync(owner, async () =>
            {
                await CompositionPrefixPrepareAssertions.EqualAsync(new DocumentResult(
                    CompositionPrefixPrepareSeed.Bound(owner, D.From, D.First), D.FirstRevision, D.Json, false, []),
                    owner.Database.GetDocument(D.Root, from));
                if (stagedReplacement)
                {
                    await Assert.That(owner.Database.Traverse(D.Root, owner.Source.Partition, D.Graph, from).Edges).IsEmpty();
                    await Assert.That(owner.Database.Traverse(D.Root, owner.Source.Partition, D.Graph, to).Edges).IsEmpty();
                    await Assert.That(owner.Store.Read(view => view.GetRecord<EdgeRecord>(
                        GraphCrossPartitionKeys.CanonicalEdge(owner.Source.Partition, D.Graph, D.Edge)))).IsNull();
                    await CompositionPrefixPrepareAssertions.EqualAsync(new GraphEdgeOwnerVersion(
                        GraphCrossPartitionProtocol.CurrentVersion, DeletedRevision, true),
                        owner.Store.Read(view => view.GetRecord<GraphEdgeOwnerVersion>(
                            GraphCrossPartitionKeys.OwnerVersion(owner.Source.Partition, D.Graph, D.Edge))));
                }
                else
                {
                    await CompositionPrefixPrepareAssertions.EdgeAsync(owner, D.Graph, D.Edge);
                }
            });
        }, token);
}
