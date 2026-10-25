using KeyLoad.FunctionalTests.Messaging;
namespace KeyLoad.FunctionalTests.RelationalStorage;

internal static class SqlCreateTableHot
{
    internal static async Task<SqlCreateTableState> RunAsync(TestDatabase fixture, PrincipalRecord principal, Guid originalId, CancellationToken token)
    {
        var command = SqlCreateTableLiteral.Compile(fixture.Partition, originalId, token);
        var issued = fixture.Database.EvaluationClock.GetUtcNow();
        var original = SqlCreateTableLiteral.Apply(fixture.Database, command, issued, principal.Id);
        await Assert.That(Errors.Code(original.Problem).Value).IsNull();
        await SqlCreateTableAssertions.SameAsync(SqlCreateTableLiteral.Definition(fixture.Partition), original.Get<ResourceDefinition>());
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        await SqlCreateTableAssertions.SameAsync(original, SqlCreateTableLiteral.Apply(fixture.Database, command, issued, principal.Id));
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        var conflicting = SqlCreateTableLiteral.Compile(fixture.Partition, command.CommandId, SqlCreateTableLiteral.ChangedSql, token);
        await Assert.That(Errors.Code(SqlCreateTableLiteral.Apply(fixture.Database, conflicting, issued, principal.Id).Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await SqlCreateTableAssertions.SameAsync(original, SqlCreateTableLiteral.Apply(fixture.Database, command, issued, principal.Id));
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        var duplicate = SqlCreateTableLiteral.Compile(fixture.Partition, Guid.NewGuid(), token);
        var duplicateAt = fixture.Database.EvaluationClock.GetUtcNow();
        var failed = SqlCreateTableLiteral.Apply(fixture.Database, duplicate, duplicateAt, principal.Id);
        await Assert.That(Errors.Code(failed.Problem).Value).IsEqualTo(ErrorCode.Conflict);

        await Assert.That(failed.Value).IsNull();
        image = QueueWholeFlowStorage.Bytes(fixture.Store);
        cut = fixture.Store.Position;
        await SqlCreateTableAssertions.SameAsync(failed, SqlCreateTableLiteral.Apply(fixture.Database, duplicate, duplicateAt, principal.Id));
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        var outbox = fixture.Database.GetOutboxStatus(SqlCreateTableLiteral.Root, fixture.Partition);
        var invalid = RelationalTestData.Submit(fixture, new PutDocument(SqlCreateTableLiteral.Table, SqlCreateTableLiteral.One, SqlCreateTableLiteral.InvalidRow));
        await Assert.That(Errors.Code(invalid.Problem).Value).IsEqualTo(ErrorCode.Validation);
        await Assert.That(fixture.Database.GetDocument(SqlCreateTableLiteral.Root,
            new(fixture.Partition, SqlCreateTableLiteral.Table, SqlCreateTableLiteral.One), cancellationToken: token)).IsNull();
        await SqlCreateTableAssertions.SameAsync(outbox, fixture.Database.GetOutboxStatus(SqlCreateTableLiteral.Root, fixture.Partition));
        var receipt = RelationalTestData.Submit(fixture,
            new PutDocument(SqlCreateTableLiteral.Table, SqlCreateTableLiteral.One, SqlCreateTableLiteral.Row)).Get<CommitReceipt>();
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await SqlCreateTableAssertions.SameAsync(new MutationReceipt[] { new("putDocument", SqlCreateTableLiteral.Table,
            SqlCreateTableLiteral.One, SqlCreateTableLiteral.Revision) }, receipt.Mutations);
        await SqlCreateTableAssertions.RowsAsync(fixture, fixture.Store, fixture.Database);
        return new(command, issued, original, duplicate, duplicateAt, failed, principal,
            SqlCreateTableOutcomeBytes.Read(fixture.Store, principal.Id, command.CommandId),
            SqlCreateTableOutcomeBytes.Read(fixture.Store, principal.Id, duplicate.CommandId));
    }
}
