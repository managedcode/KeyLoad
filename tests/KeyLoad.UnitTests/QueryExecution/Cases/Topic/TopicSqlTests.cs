using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class TopicSqlTests
{
    [Test]
    public async Task ActualTopicPublishSqlAstRetainedPurgeAndOriginalReceiptReplayPreserveOneCut()
    {
        using var database = TopicSqlSeed.Create();
        var command = TopicSqlSeed.Command(database);
        var receipt = TopicSqlSeed.Publish(database, command);
        var execution = TopicSqlSeed.Read(database);
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var engine = TopicSqlSeed.Engine(database);
        await TopicSqlAssertions.RecordsAsync(execution, engine.Execute(TopicSqlProtocol.Root, TopicSqlSeed.Request(database)));
        await TopicSqlAssertions.RecordsAsync(execution, engine.ExecuteAst(TopicSqlProtocol.Root, TopicSqlSeed.Ast(database)));
        var selected = engine.Execute(TopicSqlProtocol.Root, TopicSqlSeed.Request(database, TopicSqlProtocol.Predicate));
        var row = await Assert.That(selected.Rows).HasSingleItem();
        await Assert.That(row.EntityId).IsEqualTo(TopicSqlProtocol.Second);
        await Assert.That(row.Json).IsEqualTo(TopicSqlProtocol.ScoreRow);
        await Assert.That(engine.Execute(TopicSqlProtocol.Root, TopicSqlSeed.Request(database, TopicSqlProtocol.EmptySql)).Rows).IsEmpty();
        await TopicSqlAssertions.UnchangedAsync(database, bytes, position);
        await Assert.That(JsonDefaults.Serialize(TopicSqlSeed.Publish(database, command)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        var conflict = database.Submit(OperationKind.Batch, command with
        {
            Mutations = [new PublishTopic(TopicSqlProtocol.Topic,
            [new(TopicSqlProtocol.First, TopicSqlProtocol.Updated, TopicSqlProtocol.Payload)])]
        }, id: command.CommandId);
        await Assert.That(Errors.Code(conflict.Problem)).IsEqualTo(ErrorCode.Conflict);
        database.Commit(new PurgeTopic(TopicSqlProtocol.Topic, TopicSqlProtocol.FirstPosition));
        var retainedBytes = QueueWholeFlowStorage.Bytes(database.Store);
        var retainedPosition = database.Store.Position;
        var expired = Assert.ThrowsExactly<KeyLoadException>(() => TopicSqlSeed.Read(database));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await TopicSqlAssertions.UnchangedAsync(database, retainedBytes, retainedPosition);
        var retained = TopicSqlSeed.Read(database, TopicSqlProtocol.FirstPosition);
        await Assert.That(retained.Events.Length).IsEqualTo(TopicSqlProtocol.FirstPosition);
        await Assert.That(retained.Events.Single().Position).IsEqualTo(TopicSqlProtocol.SecondPosition);
        await TopicSqlAssertions.RecordsAsync(retained, engine.Execute(TopicSqlProtocol.Root, TopicSqlSeed.Request(database)));
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Root,
            TopicSqlSeed.Request(database, TopicSqlProtocol.StaleSql)));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await TopicSqlBoundary.RunAsync(database, engine, TopicSqlProtocol.FirstPosition);
    }

    [Test]
    public async Task ActualTopicCurrentGrantsPayloadAndHeaderUseRefusalRevokeThenHealthyProjectionLeaveBytes()
    {
        using var database = TopicSqlSeed.Create();
        TopicSqlSeed.Publish(database, TopicSqlSeed.Command(database));
        var engine = TopicSqlSeed.Engine(database);
        var principal = TopicSqlSeed.Grant(database, Capability.Query, TopicSqlProtocol.FirstPosition);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Reader, TopicSqlSeed.Request(database)));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(TopicSqlProtocol.Foreign,
            TopicSqlProtocol.ForeignTenant, [new(database.Partition.DatabaseId, TopicSqlProtocol.Topic,
                Capability.Query | Capability.TopicsRead)], []))).Get<PrincipalRecord>();
        var foreign = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Foreign, TopicSqlSeed.Request(database)));
        await Assert.That(foreign.Code).IsEqualTo(ErrorCode.PermissionDenied);
        principal = TopicSqlSeed.Grant(database, Capability.Query | Capability.TopicsRead,
            principal.PolicyEpoch + TopicSqlProtocol.EpochIncrement);
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        await TopicSqlAssertions.RedactedAsync(engine.Execute(TopicSqlProtocol.Reader, TopicSqlSeed.Request(database)));
        foreach (var sql in new[] { TopicSqlProtocol.PrivatePredicate, TopicSqlProtocol.PrivateOrder })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Reader, TopicSqlSeed.Request(database, sql)));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
        await TopicSqlAssertions.UnchangedAsync(database, bytes, position);
        principal = TopicSqlSeed.Grant(database, Capability.Query | Capability.TopicsRead,
            principal.PolicyEpoch + TopicSqlProtocol.EpochIncrement,
            [TopicSqlProtocol.ReadGrant, TopicSqlProtocol.UseGrant, TopicSqlProtocol.HeaderRead, TopicSqlProtocol.HeaderUse]);
        await TopicSqlAssertions.RecordsAsync(TopicSqlSeed.Read(database), engine.Execute(TopicSqlProtocol.Reader, TopicSqlSeed.Request(database)));
        TopicSqlSeed.Grant(database, Capability.TopicsRead, principal.PolicyEpoch + TopicSqlProtocol.EpochIncrement);
        var revoked = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Reader, TopicSqlSeed.Request(database)));
        await Assert.That(revoked.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await TopicSqlAssertions.RecordsAsync(TopicSqlSeed.Read(database), engine.Execute(TopicSqlProtocol.Root, TopicSqlSeed.Request(database)));
    }
}
