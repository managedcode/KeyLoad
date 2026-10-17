namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPositiveExecuteTrial
{
    internal static async Task RunAsync(Stream stream, PostgresRf3State state, CancellationToken token)
    {
        var statement = PostgresPositiveExecuteProtocol.Statement;
        var portal = PostgresPositiveExecuteProtocol.Portal;
        await PostgresPortalOwnershipCommands.ParseAsync(stream, statement, state.Sql, token);
        foreach (var limit in PostgresPositiveExecuteProtocol.Limits)
        {
            await PostgresPortalOwnershipCommands.BindAsync(stream, portal, statement, token);
            await ExecuteAsync(stream, state, portal, limit, token);
            await PostgresPortalOwnershipReplies.RefusedAsync(stream, PostgresPortalOwnershipProtocol.Execute,
                PostgresPositiveExecuteFrames.Execute(portal, limit), PostgresPositiveExecuteProtocol.Unsupported, token);
        }
        await PostgresPortalOwnershipCommands.BindAsync(stream, portal, statement, token);
        await PostgresPortalOwnershipReplies.RefusedAsync(stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPositiveExecuteFrames.Execute(portal, PostgresPositiveExecuteProtocol.InvalidRows),
            PostgresPortalOwnershipProtocol.Validation, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, portal, statement, token);
        await ExecuteAsync(stream, state, portal, PostgresPositiveExecuteProtocol.OneRow, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement, statement, token);
    }
    internal static async Task ExecuteAsync(Stream stream, PostgresRf3State state, string portal, int limit, CancellationToken token)
    {
        await PostgresPortalOwnershipReplies.SendAsync(stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPositiveExecuteFrames.Execute(portal, limit), token);
        await PostgresPortalOwnershipAssertions.PageAsync(state, await PostgresPortalOwnershipReplies.PageAsync(stream, token));
    }
}
