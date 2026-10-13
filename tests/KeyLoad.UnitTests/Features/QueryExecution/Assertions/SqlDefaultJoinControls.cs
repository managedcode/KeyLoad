using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlDefaultJoinControls
{
    private const string ExplicitJoin = " INNER JOIN ";
    private const string DefaultJoin = " JOIN ";
    private const string On = " ON ";
    private const string Where = " WHERE ";
    private const string MissingOn = "The original joined query has no ON clause.";

    internal static async Task InvalidAsync(TestDatabase database, QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var original = SqlOffsetProtocol.JoinRequest(database.Partition);
        var plain = original with { Sql = original.Sql.Replace(ExplicitJoin, DefaultJoin, StringComparison.Ordinal) };
        var on = plain.Sql.IndexOf(On, StringComparison.Ordinal);
        if (on < default(int))
        { throw new InvalidOperationException(MissingOn); }
        foreach (var sql in new[] { plain.Sql[..on], plain.Sql.Replace(On, Where, StringComparison.Ordinal) })
        {
            await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Root, plain with { Sql = sql }), ErrorCode.Validation);
            await SqlOffsetRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlOffsetPages.ExactAsync(engine.Execute(SqlOffsetProtocol.Root, plain), SqlOffsetPages.JoinPage(position));
            await SqlOffsetRefusals.UnchangedAsync(database.Store, image, position, identity);
        }
        var scalar = SqlOffsetProtocol.ScalarRequest(database.Partition) with { Sql = plain.Sql };
        await ErrorAsync(() => engine.Execute(SqlOffsetProtocol.Root, scalar), ErrorCode.UnsupportedCapability);
        await SqlOffsetRefusals.UnchangedAsync(database.Store, image, position, identity);
        await SqlOffsetPages.ExactAsync(engine.Execute(SqlOffsetProtocol.Root, plain), SqlOffsetPages.JoinPage(position));
    }

    internal static async Task CancelledAsync(TestDatabase database, QueryEngine engine, CancellationToken originalToken)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var original = SqlOffsetProtocol.JoinRequest(database.Partition);
        var plain = original with { Sql = original.Sql.Replace(ExplicitJoin, DefaultJoin, StringComparison.Ordinal) };
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlOffsetProtocol.Root,
            plain, cancellationToken: originalToken));
        await Assert.That(failure.CancellationToken).IsEqualTo(originalToken);
        await SqlOffsetRefusals.UnchangedAsync(database.Store, image, position, identity);
        await SqlOffsetPages.ExactAsync(engine.Execute(SqlOffsetProtocol.Root, plain, cancellationToken: originalToken), SqlOffsetPages.JoinPage(position));
        await SqlOffsetRefusals.UnchangedAsync(database.Store, image, position, identity);
    }

    private static async Task ErrorAsync(Action operation, ErrorCode code)
        => await Assert.That(Assert.ThrowsExactly<KeyLoadException>(operation).Code).IsEqualTo(code);
}
