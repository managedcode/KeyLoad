using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks complete canonical vector identity and a literal exact native search result.</summary>
internal static class ControlledPartitionMovementVectorAssertions
{
    private const string VectorKeyFamily = "vector";
    private const double SingleRank = 1.0 / 61.0;

    internal static async Task ReadAsync(ControlledPartitionMovementNode node, CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var vector = node.Store.Read(view => view.GetRecord<VectorRecord>(KeySpace.Partition(VectorKeyFamily,
            partition, ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.VectorField,
            ControlledPartitionMovementCorpus.DocumentId)));
        var expected = new VectorRecord(ControlledPartitionMovementCorpus.DocumentId,
            ControlledPartitionMovementCorpus.VectorField, ControlledPartitionMovementCorpus.Space, [1f, 0f],
            ControlledPartitionMovementCorpus.InitialRevision);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(vector, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
        var rows = await new SearchEngine(node.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(
            PhysicalShardCatalogFixture.RootPrincipalId, new(partition, ControlledPartitionMovementCorpus.Collection,
                VectorField: ControlledPartitionMovementCorpus.VectorField, Vector: [1f, 0f],
                Space: ControlledPartitionMovementCorpus.Space), cancellationToken);
        var document = new DocumentResult(new(partition, ControlledPartitionMovementCorpus.Collection,
            ControlledPartitionMovementCorpus.DocumentId), ControlledPartitionMovementCorpus.InitialRevision,
            ControlledPartitionMovementCorpus.OriginalJson, false, []);
        var literal = new[] { new RankedDocument(document, SingleRank) };
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(rows.ToArray(), JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(literal, JsonDefaults.Options))).IsTrue();
    }
}
