using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class TopicSqlNativeTests
{
    [Test]
    public async Task ActualTopicPublishSqlAstRetainedPurgeAndOriginalReceiptReplayPreserveOneCut()
    {
        using var database = TopicSqlNativeSeed.Create();
        var command = TopicSqlNativeSeed.Command(database);
        var receipt = TopicSqlNativeSeed.Publish(database, command);
        var native = TopicSqlNativeSeed.Read(database);
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var engine = TopicSqlNativeSeed.Engine(database);
        await TopicSqlNativeAssertions.RecordsAsync(native, engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database)));
        await TopicSqlNativeAssertions.RecordsAsync(native, engine.ExecuteAst(TopicSqlProtocol.Root, TopicSqlNativeSeed.Ast(database)));
        var selected = engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database, TopicSqlProtocol.Predicate));
        var row = await Assert.That(selected.Rows).HasSingleItem();
        await Assert.That(row.EntityId).IsEqualTo(TopicSqlProtocol.Second);
        await Assert.That(row.Json).IsEqualTo(TopicSqlProtocol.ScoreRow);
        await Assert.That(engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database, TopicSqlProtocol.EmptySql)).Rows).IsEmpty();
        await TopicSqlNativeAssertions.UnchangedAsync(database, bytes, position);
        await Assert.That(JsonDefaults.Serialize(TopicSqlNativeSeed.Publish(database, command)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        var conflict = database.Submit(OperationKind.Batch, command with
        {
            Mutations = [new PublishTopic(TopicSqlProtocol.Topic,
            [new(TopicSqlProtocol.First, TopicSqlProtocol.Updated, TopicSqlProtocol.Payload)])]
        }, id: command.CommandId);
        await Assert.That(conflict.Error).IsEqualTo(ErrorCode.Conflict);
        database.Commit(new PurgeTopic(TopicSqlProtocol.Topic, TopicSqlProtocol.FirstPosition));
        var retainedBytes = QueueWholeFlowStorage.Bytes(database.Store);
        var retainedPosition = database.Store.Position;
        var expired = Assert.ThrowsExactly<KeyLoadException>(() => TopicSqlNativeSeed.Read(database));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await TopicSqlNativeAssertions.UnchangedAsync(database, retainedBytes, retainedPosition);
        var retained = TopicSqlNativeSeed.Read(database, TopicSqlProtocol.FirstPosition);
        await Assert.That(retained.Events.Length).IsEqualTo(TopicSqlProtocol.FirstPosition);
        await Assert.That(retained.Events.Single().Position).IsEqualTo(TopicSqlProtocol.SecondPosition);
        await TopicSqlNativeAssertions.RecordsAsync(retained, engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database)));
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Root,
            TopicSqlNativeSeed.Request(database, TopicSqlProtocol.StaleSql)));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await TopicSqlNativeBoundary.RunAsync(database, engine, TopicSqlProtocol.FirstPosition);
    }

    [Test]
    public async Task ActualTopicCurrentGrantsPayloadAndHeaderUseRefusalRevokeThenHealthyProjectionLeaveNativeBytes()
    {
        using var database = TopicSqlNativeSeed.Create();
        TopicSqlNativeSeed.Publish(database, TopicSqlNativeSeed.Command(database));
        var engine = TopicSqlNativeSeed.Engine(database);
        var principal = TopicSqlNativeSeed.Grant(database, Capability.Query, TopicSqlProtocol.FirstPosition);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Reader, TopicSqlNativeSeed.Request(database)));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(TopicSqlProtocol.Foreign,
            TopicSqlProtocol.ForeignTenant, [new(database.Partition.DatabaseId, TopicSqlProtocol.Topic,
                Capability.Query | Capability.TopicsRead)], []))).Get<PrincipalRecord>();
        var foreign = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Foreign, TopicSqlNativeSeed.Request(database)));
        await Assert.That(foreign.Code).IsEqualTo(ErrorCode.PermissionDenied);
        principal = TopicSqlNativeSeed.Grant(database, Capability.Query | Capability.TopicsRead,
            principal.PolicyEpoch + TopicSqlProtocol.EpochIncrement);
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        await TopicSqlNativeAssertions.RedactedAsync(engine.Execute(TopicSqlProtocol.Reader, TopicSqlNativeSeed.Request(database)));
        foreach (var sql in new[] { TopicSqlProtocol.PrivatePredicate, TopicSqlProtocol.PrivateOrder })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Reader, TopicSqlNativeSeed.Request(database, sql)));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
        await TopicSqlNativeAssertions.UnchangedAsync(database, bytes, position);
        principal = TopicSqlNativeSeed.Grant(database, Capability.Query | Capability.TopicsRead,
            principal.PolicyEpoch + TopicSqlProtocol.EpochIncrement,
            [TopicSqlProtocol.ReadGrant, TopicSqlProtocol.UseGrant, TopicSqlProtocol.HeaderRead, TopicSqlProtocol.HeaderUse]);
        await TopicSqlNativeAssertions.RecordsAsync(TopicSqlNativeSeed.Read(database), engine.Execute(TopicSqlProtocol.Reader, TopicSqlNativeSeed.Request(database)));
        TopicSqlNativeSeed.Grant(database, Capability.TopicsRead, principal.PolicyEpoch + TopicSqlProtocol.EpochIncrement);
        var revoked = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Reader, TopicSqlNativeSeed.Request(database)));
        await Assert.That(revoked.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await TopicSqlNativeAssertions.RecordsAsync(TopicSqlNativeSeed.Read(database), engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database)));
    }
}
