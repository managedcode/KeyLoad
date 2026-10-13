using System.Text;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipUnnamed
{
    internal static async Task ReplaceAsync(Stream stream, PostgresRf3State state, CancellationToken token)
    {
        var second = PostgresPortalOwnershipProtocol.SecondStatement;
        await PostgresPortalOwnershipCommands.ParseAsync(stream, second, state.Sql, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, string.Empty, second, token);
        await PostgresPortalOwnershipCommands.ParseAsync(stream, string.Empty, state.Sql, token);
        await PostgresPortalOwnershipCommands.DescribeAsync(stream, string.Empty, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, PostgresPortalOwnershipProtocol.OldPortal, string.Empty, token);
        await PostgresPortalOwnershipCommands.ParseAsync(stream, string.Empty, state.Sql, token);
        await PostgresPortalOwnershipCommands.DescribeAsync(stream, string.Empty, token);
        await PostgresPortalOwnershipCommands.ExecuteAsync(stream, state, string.Empty, token);
        await PostgresPortalOwnershipCommands.MissingAsync(stream, PostgresPortalOwnershipProtocol.Describe,
            PostgresPortalOwnershipFrames.Target(PostgresPortalOwnershipProtocol.Portal,
                PostgresPortalOwnershipProtocol.OldPortal), token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, second, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, string.Empty, token);
    }

    internal static async Task SimpleAsync(Stream stream, PostgresRf3State state, CancellationToken token)
    {
        await PostgresPortalOwnershipCommands.ParseAsync(stream, string.Empty, state.Sql, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, PostgresPortalOwnershipProtocol.OldPortal, string.Empty, token);
        await PostgresRf3WireMessage.WriteAsync(stream, PostgresPortalOwnershipProtocol.SimpleQuery,
            Encoding.UTF8.GetBytes(state.Sql + PostgresPortalOwnershipProtocol.Terminator), token);
        await PostgresPortalOwnershipReplies.ExactAsync(stream, PostgresPortalOwnershipProtocol.RowDescription,
            PostgresPortalOwnershipFrames.Description(), token);
        await PostgresPortalOwnershipAssertions.PageAsync(state,
            await PostgresPortalOwnershipReplies.PageAsync(stream, token));
        await PostgresPortalOwnershipReplies.ExactAsync(stream, PostgresPortalOwnershipProtocol.Ready,
            [PostgresPortalOwnershipProtocol.Idle], token);
        await PostgresPortalOwnershipCommands.MissingAsync(stream, PostgresPortalOwnershipProtocol.Bind,
            PostgresPortalOwnershipFrames.Bind(PostgresPortalOwnershipProtocol.OldPortal, string.Empty), token);
        await PostgresPortalOwnershipCommands.ParseAsync(stream, string.Empty, state.Sql, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, PostgresPortalOwnershipProtocol.OldPortal, string.Empty, token);
        await PostgresPortalOwnershipCommands.ExecuteAsync(stream, state, PostgresPortalOwnershipProtocol.OldPortal, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, string.Empty, token);
    }
}
