using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Options;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertRetiredSeed
{
    internal static async Task<SqlTypedInsertRetiredState> RunAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, CancellationToken token)
    {
        var sourceIndex = source.Journal.Log.State.LastIndex;
        var targetIndex = target.Journal.Log.State.LastIndex;
        var partition = ControlledPartitionMovementCorpus.Partition;
        var definition = SqlTypedInsertRetiredLiteral.Definition(partition);
        await SqlTypedInsertAssertions.SameAsync(definition, Submit(source, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), token).Get<ResourceDefinition>());
        var writer = SqlTypedInsertRetiredLiteral.Principal(partition);
        await SqlTypedInsertAssertions.SameAsync(writer, Configure(source, writer, token));
        await SqlTypedInsertAssertions.SameAsync(writer, Configure(target, writer, token));
        await Assert.That(writer.ClusterAdministrator).IsFalse();
        var original = Create(source, SqlTypedInsertRetiredLiteral.OriginalId, token);
        var placement = source.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId, new(SqlTypedInsertRetiredLiteral.Version, partition));
        var expected = new CommitReceipt(original.Id, new(placement.Incarnation, partition.AtomicPartitionId,
            checked(source.Journal.Log.State.LastIndex + NextEntry), placement.PlacementEpoch),
            [new(SqlTypedInsertRetiredLiteral.PutKind, SqlTypedInsertRetiredLiteral.Table, SqlTypedInsertRetiredLiteral.One,
                SqlTypedInsertRetiredLiteral.Revision)], DurabilityProfile.ProcessDurable);
        await SqlTypedInsertAssertions.SameAsync(expected, source.Journal.Submit(original, token).Get<CommitReceipt>());
        var outbox = source.Database.GetOutboxStatus(PhysicalShardCatalogFixture.RootPrincipalId, partition);
        var failed = Create(source, SqlTypedInsertRetiredLiteral.FailedId, token);
        await SqlTypedInsertAssertions.SameAsync(new OperationResult(null, ErrorCode.Conflict, SqlTypedInsertRetiredLiteral.Existing), source.Journal.Submit(failed, token));
        await SqlTypedInsertAssertions.SameAsync(outbox, source.Database.GetOutboxStatus(PhysicalShardCatalogFixture.RootPrincipalId, partition));
        await SqlTypedInsertAssertions.SameAsync(new DocumentResult(new(partition, SqlTypedInsertRetiredLiteral.Table,
            SqlTypedInsertRetiredLiteral.One), SqlTypedInsertRetiredLiteral.Revision, SqlTypedInsertRetiredLiteral.OneJson, false, []),
            source.Database.GetDocument(writer.Id, new(partition, SqlTypedInsertRetiredLiteral.Table, SqlTypedInsertRetiredLiteral.One), cancellationToken: token));
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(checked(sourceIndex + SqlTypedInsertRetiredLiteral.SourceSeedEntries));
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(checked(targetIndex + SqlTypedInsertRetiredLiteral.TargetSeedEntries));
        source.AdditionalSeedResources = [definition];
        source.AdditionalSeedEntries = SqlTypedInsertRetiredLiteral.SourceSeedEntries;
        target.AdditionalSeedEntries = SqlTypedInsertRetiredLiteral.TargetSeedEntries;
        return new(writer, original, failed, expected, new(source, original), new(source, failed));
    }
    internal static PrincipalRecord Configure(ControlledPartitionMovementNode node, PrincipalRecord principal, CancellationToken token)
        => Submit(node, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), token).Get<PrincipalRecord>();
    private static OperationResult Submit<T>(ControlledPartitionMovementNode node, OperationKind kind, T payload, CancellationToken token)
        => node.Journal.Submit(node.Database.CreateNativeOperation(kind, Guid.NewGuid(), PhysicalShardCatalogFixture.RootPrincipalId,
            node.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload)), token);
    private static ReplicatedOperation Create(ControlledPartitionMovementNode source, Guid id, CancellationToken token)
    {
        var limits = Options.Create(source.Database.Limits);
        var decoded = SqlOperationCompiler.Compile(new(ControlledPartitionMovementCorpus.Partition,
            SqlTypedInsertRetiredLiteral.InsertSql, CommandId: id), limits, limits.Value.MaxBatchBytes,
            UnitExecutionOptions.QueryExecution(), token);
        return source.Database.CreateNativeOperation(OperationKind.Batch, decoded.CommandId, SqlTypedInsertRetiredLiteral.Writer,
            source.Database.EvaluationClock.GetUtcNow(), decoded.Payload);
    }
    private const long NextEntry = 1;
}

