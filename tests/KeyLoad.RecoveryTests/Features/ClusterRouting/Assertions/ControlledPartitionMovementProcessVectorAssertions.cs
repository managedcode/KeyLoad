using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Checks complete canonical vector identity and a literal exact native search result.</summary>
internal static class ControlledPartitionMovementProcessVectorAssertions
{
    private const string VectorKeyFamily = "vector";
    private const double SingleRank = 1.0 / 61.0;

    internal static async Task ReadAsync(ControlledPartitionMovementNativeNode node, CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementProcessLiteralCorpus.Partition;
        var vector = node.Store.Read(view => view.GetRecord<VectorRecord>(KeySpace.Partition(VectorKeyFamily,
            partition, ControlledPartitionMovementProcessLiteralCorpus.Collection, ControlledPartitionMovementProcessLiteralCorpus.VectorField,
            ControlledPartitionMovementProcessLiteralCorpus.DocumentId)));
        var expected = new VectorRecord(ControlledPartitionMovementProcessLiteralCorpus.DocumentId,
            ControlledPartitionMovementProcessLiteralCorpus.VectorField, ControlledPartitionMovementProcessLiteralCorpus.Space, [1f, 0f],
            ControlledPartitionMovementProcessLiteralCorpus.InitialRevision);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(vector, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
        var rows = await new SearchEngine(node.Database, CrashExecutionOptions.QueryExecution()).SearchAsync(
            ControlledPartitionMovementProcessLiteralCorpus.PrincipalId, new(partition, ControlledPartitionMovementProcessLiteralCorpus.Collection,
                VectorField: ControlledPartitionMovementProcessLiteralCorpus.VectorField, Vector: [1f, 0f],
                Space: ControlledPartitionMovementProcessLiteralCorpus.Space), cancellationToken);
        var document = new DocumentResult(new(partition, ControlledPartitionMovementProcessLiteralCorpus.Collection,
            ControlledPartitionMovementProcessLiteralCorpus.DocumentId), ControlledPartitionMovementProcessLiteralCorpus.InitialRevision,
            ControlledPartitionMovementProcessLiteralCorpus.OriginalJson, false, []);
        var literal = new[] { new RankedDocument(document, SingleRank) };
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(rows.ToArray(), JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(literal, JsonDefaults.Options))).IsTrue();
    }
}
