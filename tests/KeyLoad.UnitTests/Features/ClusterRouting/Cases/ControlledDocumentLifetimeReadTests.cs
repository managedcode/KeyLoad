using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledDocumentLifetimeReadTests
{
    private const string Collection = "movement-documents";
    private const string PartitionKey = "partition-a";
    private const string Document = "knowledge-1";
    private const string Json = "{\"text\":\"Київ knowledge\",\"state\":\"original\"}";
    private const long Revision = 1;
    private const int Reads = 3;
    private const int ColdCount = 2;
    private static readonly PartitionRef Partition = new("system", "movement", "knowledge", PartitionKey);

    [Test]
    public Task ActualRetiredNativeOwnersUseMandatorySourceCoverageThenReplayAndTwoColdCuts()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
            var reference = new EntityRef(Partition, Collection, Document);
            for (var attempt = default(int); attempt < Reads; attempt++)
            {
                var sourceBytes = ControlledPartitionMovementRawImage.Bytes(source.Store);
                var targetBytes = ControlledPartitionMovementRawImage.Bytes(target.Store);
                var sourcePosition = source.Store.Position;
                var targetPosition = target.Store.Position;
                var parent = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(source.Database.Limits),
                    source.Database.EvaluationClock, TestContext.Current!.Execution.CancellationToken);
                var frame = source.Database.TryCaptureControlledDocumentReadV2(PhysicalShardCatalogFixture.RootPrincipalId,
                    reference, null, source.Database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(source.Database.Limits.QueryDeadlineSeconds), parent)
                    ?? throw new InvalidOperationException(Collection);
                var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(target.Database.Limits),
                    target.Database.EvaluationClock, TestContext.Current!.Execution.CancellationToken);
                var grant = work.CreateReadGrant(target.Database.Limits.MaxQueryReadBytes, target.Database.Limits.MaxScanRecords);
                var result = target.Database.ReadControlledDocumentV2(PhysicalShardCatalogFixture.RootPrincipalId,
                    frame, work, grant, TestContext.Current!.Execution.CancellationToken);
                source.Database.ValidateControlledDocumentReadV2(frame, parent);
                var expected = new DocumentResult(reference, Revision, Json, false, []);
                await Assert.That(JsonDefaults.Serialize(result).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).AsSpan().SequenceEqual(sourceBytes)).IsTrue();
                await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).AsSpan().SequenceEqual(targetBytes)).IsTrue();
                await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
                await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
                if (attempt < ColdCount)
                {
                    source.Reopen();
                    target.Reopen();
                }
            }
        });
}
