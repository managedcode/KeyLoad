namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class PostgresPortalOwnershipCommands
{
    internal static Task ParseAsync(Stream stream, string name, string sql, CancellationToken token)
        => PostgresPortalOwnershipReplies.ControlAsync(stream, PostgresPortalOwnershipProtocol.Parse,
            PostgresPortalOwnershipFrames.Parse(name, sql), PostgresPortalOwnershipProtocol.ParseComplete, token);
    internal static Task BindAsync(Stream stream, string name, string statement, CancellationToken token)
        => PostgresPortalOwnershipReplies.ControlAsync(stream, PostgresPortalOwnershipProtocol.Bind,
            PostgresPortalOwnershipFrames.Bind(name, statement), PostgresPortalOwnershipProtocol.BindComplete, token);
    internal static Task CloseAsync(Stream stream, byte kind, string name, CancellationToken token)
        => PostgresPortalOwnershipReplies.ControlAsync(stream, PostgresPortalOwnershipProtocol.Close,
            PostgresPortalOwnershipFrames.Target(kind, name), PostgresPortalOwnershipProtocol.CloseComplete, token);
    internal static Task MissingAsync(Stream stream, byte type, byte[] payload, CancellationToken token)
        => PostgresPortalOwnershipReplies.RefusedAsync(stream, type, payload,
            PostgresPortalOwnershipProtocol.Validation, token);
    internal static async Task DescribeAsync(Stream stream, string name, CancellationToken token)
    {
        await PostgresPortalOwnershipReplies.SendAsync(stream, PostgresPortalOwnershipProtocol.Describe,
            PostgresPortalOwnershipFrames.Target(PostgresPortalOwnershipProtocol.Portal, name), token);
        await PostgresPortalOwnershipReplies.ExactAsync(stream, PostgresPortalOwnershipProtocol.RowDescription,
            PostgresPortalOwnershipFrames.Description(), token);
    }
}
