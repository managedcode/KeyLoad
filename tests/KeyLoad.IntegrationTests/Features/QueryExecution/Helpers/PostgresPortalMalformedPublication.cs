namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalMalformedPublication
{
    internal static async Task RunAsync(Stream stream, PostgresRf3State state,
        Func<Task> requireConservation, CancellationToken token)
    {
        var named = PostgresPortalOwnershipProtocol.FirstStatement;
        await PostgresPortalOwnershipCommands.ParseAsync(stream, string.Empty, state.Sql, token);
        await PostgresPortalOwnershipCommands.ParseAsync(stream, named, state.Sql, token);
        await HealthyAsync(stream, state, requireConservation, token);
        foreach (var statement in new[] { string.Empty, named })
        {
            var malformed = Trailing(PostgresPortalOwnershipFrames.Parse(statement, state.Sql));
            await RefusedAsync(stream, state, PostgresPortalOwnershipProtocol.Parse,
                malformed, requireConservation, token);
        }
        await RefusedAsync(stream, state, PostgresPortalOwnershipProtocol.Bind,
            Trailing(PostgresPortalOwnershipFrames.Bind(string.Empty, named)), requireConservation, token);
        await RefusedAsync(stream, state, PostgresPortalOwnershipProtocol.Parse,
            PostgresPortalOwnershipFrames.Parse(named, state.Sql), requireConservation, token);
        await RefusedAsync(stream, state, PostgresPortalOwnershipProtocol.Bind,
            PostgresPortalOwnershipFrames.Bind(PostgresPortalOwnershipProtocol.Survivor, string.Empty),
            requireConservation, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement,
            named, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Statement,
            string.Empty, token);
        await requireConservation();
    }

    private static async Task RefusedAsync(Stream stream, PostgresRf3State state, byte kind,
        byte[] payload, Func<Task> requireConservation, CancellationToken token)
    {
        await PostgresPortalOwnershipReplies.RefusedAsync(stream, kind, payload,
            PostgresPortalOwnershipProtocol.Validation, token);
        await requireConservation();
        // Sync retires every portal; surviving statements are proved by new Bind and complete Execute.
        await HealthyAsync(stream, state, requireConservation, token);
    }

    private static async Task HealthyAsync(Stream stream, PostgresRf3State state,
        Func<Task> requireConservation, CancellationToken token)
    {
        var old = PostgresPortalOwnershipProtocol.OldPortal;
        var survivor = PostgresPortalOwnershipProtocol.Survivor;
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Portal, old, token);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Portal, survivor, token);
        await PostgresPortalOwnershipCommands.BindAsync(stream, old, string.Empty, token);
        await PostgresPortalOwnershipCommands.ExecuteAsync(stream, state, old, token);
        await requireConservation();
        await PostgresPortalOwnershipCommands.BindAsync(stream, survivor,
            PostgresPortalOwnershipProtocol.FirstStatement, token);
        await PostgresPortalOwnershipCommands.ExecuteAsync(stream, state, survivor, token);
        await requireConservation();
    }

    private static byte[] Trailing(byte[] validPayload)
        => [.. validPayload, default(byte)];
}
