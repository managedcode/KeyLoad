namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipClose
{
    internal static async Task RunAsync(Stream stream, PostgresRf3State state, CancellationToken token)
    {
        var first = PostgresPortalOwnershipProtocol.FirstStatement;
        var second = PostgresPortalOwnershipProtocol.SecondStatement;
        var old = PostgresPortalOwnershipProtocol.OldPortal;
        var survivor = PostgresPortalOwnershipProtocol.Survivor;
        await PostgresPortalOwnershipCommands.ParseAsync(stream, first, state.Sql, token);
        await PostgresPortalOwnershipCommands.ParseAsync(stream, second, state.Sql, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, old, first, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, survivor, second, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, first, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, first, token);
        await PostgresPortalOwnershipCommands.DescribeAsync(stream, survivor, token);
        await PostgresPortalOwnershipCommands.ExecuteAsync(stream, state, survivor, token);
        await PostgresPortalOwnershipCommands.MissingAsync(stream, PostgresPortalOwnershipProtocol.Describe,
            PostgresPortalOwnershipFrames.Target(PostgresPortalOwnershipProtocol.Portal, old), token);
        await PostgresPortalOwnershipCommands.ParseAsync(stream, first, state.Sql, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, old, first, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, first, token);
        await PostgresPortalOwnershipCommands.MissingAsync(stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPortalOwnershipFrames.Execute(old), token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Portal, old, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, second, token);
    }

    internal static async Task CapacityAsync(Stream stream, PostgresRf3State state, CancellationToken token)
    {
        var first = PostgresPortalOwnershipProtocol.FirstStatement;
        var second = PostgresPortalOwnershipProtocol.SecondStatement;
        await PostgresPortalOwnershipCommands.ParseAsync(stream, first, state.Sql, token);
        await PostgresPortalOwnershipCommands.ParseAsync(stream, second, state.Sql, token);
        for (var index = PostgresRf3Protocol.FirstColumn; index < PostgresPortalOwnershipProtocol.Slots; index++)
        { await PostgresPortalOwnershipCommands.BindAsync(stream, PostgresPortalOwnershipProtocol.Slot(index), first, token); }
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, first, token);
        for (var index = PostgresRf3Protocol.FirstColumn; index < PostgresPortalOwnershipProtocol.Slots; index++)
        { await PostgresPortalOwnershipCommands.BindAsync(stream, PostgresPortalOwnershipProtocol.Slot(index), second, token); }
        await PostgresPortalOwnershipReplies.RefusedAsync(stream, PostgresPortalOwnershipProtocol.Bind,
            PostgresPortalOwnershipFrames.Bind(PostgresPortalOwnershipProtocol.OverflowPortal, second),
            PostgresPortalOwnershipProtocol.Exhausted, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, PostgresPortalOwnershipProtocol.Survivor, second, token);
        await PostgresPortalOwnershipCommands.ExecuteAsync(stream, state, PostgresPortalOwnershipProtocol.Survivor, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, second, token);
    }
}
