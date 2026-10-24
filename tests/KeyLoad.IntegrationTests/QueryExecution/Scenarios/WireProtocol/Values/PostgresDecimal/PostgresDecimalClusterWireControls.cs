using System.Text;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class PostgresDecimalClusterWireControls
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullOrderingClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresClusterWireOwner.OpenAsync(fixture, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await PostgresClusterWireAuthentication.AuthenticateAsync(wire, state.Reader, token);
                await PostgresPortalOwnershipReplies.ControlAsync(wire.Stream, PostgresPortalOwnershipProtocol.Parse,
                    PostgresDecimalClusterFrames.Parse(), PostgresPortalOwnershipProtocol.ParseComplete, token);
                foreach (var fault in Faults())
                {
                    await PostgresPortalOwnershipReplies.RefusedAsync(wire.Stream, PostgresPortalOwnershipProtocol.Bind,
                        PostgresDecimalClusterFrames.Bind(fault.Format, fault.Bytes), fault.Error, token);
                    await HealthyAsync(wire.Stream, token);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static (short Format, byte[] Bytes, string Error)[] Faults() =>
    [
        (PostgresDecimalClusterProtocol.Binary, [0], PostgresPortalOwnershipProtocol.Validation),
        (PostgresDecimalClusterProtocol.Binary, PostgresDecimalClusterFrames.Numeric(0, PostgresDecimalClusterProtocol.Positive, 0, 10000), PostgresPortalOwnershipProtocol.Validation),
        (PostgresDecimalClusterProtocol.Binary, PostgresDecimalClusterFrames.Numeric(0, PostgresDecimalClusterProtocol.NaN, 0), PostgresDecimalClusterProtocol.Unsupported),
        (PostgresDecimalClusterProtocol.Binary, PostgresDecimalClusterFrames.Numeric(-8, PostgresDecimalClusterProtocol.Positive, 29, 1000), PostgresDecimalClusterProtocol.Unsupported),
        (PostgresDecimalClusterProtocol.Binary, PostgresDecimalClusterFrames.Numeric(7, PostgresDecimalClusterProtocol.Positive, 0,
            7, 9228, 1625, 1426, 4337, 5935, 4395, 336), PostgresDecimalClusterProtocol.Unsupported),
        (PostgresDecimalClusterProtocol.Binary, PostgresDecimalClusterFrames.Numeric(0, PostgresDecimalClusterProtocol.Positive, 0, 1, 5000), PostgresDecimalClusterProtocol.Unsupported),
        (PostgresDecimalClusterProtocol.Text, Encoding.UTF8.GetBytes("1e"), PostgresPortalOwnershipProtocol.Validation),
        (PostgresDecimalClusterProtocol.Text, Encoding.UTF8.GetBytes("NaN"), PostgresDecimalClusterProtocol.Unsupported),
        (PostgresDecimalClusterProtocol.Text, Encoding.UTF8.GetBytes("0.00000000000000000000000000001"), PostgresDecimalClusterProtocol.Unsupported)
    ];

    private static async Task HealthyAsync(Stream stream, CancellationToken token)
    {
        await PostgresPortalOwnershipReplies.ControlAsync(stream, PostgresPortalOwnershipProtocol.Bind,
            PostgresDecimalClusterFrames.Bind(PostgresDecimalClusterProtocol.Text, Encoding.UTF8.GetBytes("1.50")),
            PostgresPortalOwnershipProtocol.BindComplete, token);
        await PostgresPortalOwnershipReplies.SendAsync(stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPortalOwnershipFrames.Execute(PostgresDecimalClusterProtocol.Portal), token);
        await PostgresDecimalClusterPages.PageAsync(await PostgresPortalOwnershipReplies.PageAsync(stream, token), PostgresDecimalClusterProtocol.BetweenScores);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Portal,
            PostgresDecimalClusterProtocol.Portal, token);
        await PostgresClusterWireMessage.WriteAsync(stream, PostgresPortalOwnershipProtocol.Sync, [], token);
        await PostgresPortalOwnershipReplies.ExactAsync(stream, PostgresPortalOwnershipProtocol.Ready,
            [PostgresPortalOwnershipProtocol.Idle], token);
    }
}
