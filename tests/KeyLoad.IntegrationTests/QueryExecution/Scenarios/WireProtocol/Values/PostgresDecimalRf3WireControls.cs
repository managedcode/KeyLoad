using System.Text;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDecimalRf3WireControls
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullOrderingRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3WireOwner.OpenAsync(fixture, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await PostgresRf3WireAuthentication.AuthenticateAsync(wire, state.Reader, token);
                await PostgresPortalOwnershipReplies.ControlAsync(wire.Stream, PostgresPortalOwnershipProtocol.Parse,
                    PostgresDecimalRf3Frames.Parse(), PostgresPortalOwnershipProtocol.ParseComplete, token);
                foreach (var fault in Faults())
                {
                    await PostgresPortalOwnershipReplies.RefusedAsync(wire.Stream, PostgresPortalOwnershipProtocol.Bind,
                        PostgresDecimalRf3Frames.Bind(fault.Format, fault.Bytes), fault.Error, token);
                    await HealthyAsync(wire.Stream, token);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static (short Format, byte[] Bytes, string Error)[] Faults() =>
    [
        (PostgresDecimalRf3Protocol.Binary, [0], PostgresPortalOwnershipProtocol.Validation),
        (PostgresDecimalRf3Protocol.Binary, PostgresDecimalRf3Frames.Numeric(0, PostgresDecimalRf3Protocol.Positive, 0, 10000), PostgresPortalOwnershipProtocol.Validation),
        (PostgresDecimalRf3Protocol.Binary, PostgresDecimalRf3Frames.Numeric(0, PostgresDecimalRf3Protocol.NaN, 0), PostgresDecimalRf3Protocol.Unsupported),
        (PostgresDecimalRf3Protocol.Binary, PostgresDecimalRf3Frames.Numeric(-8, PostgresDecimalRf3Protocol.Positive, 29, 1000), PostgresDecimalRf3Protocol.Unsupported),
        (PostgresDecimalRf3Protocol.Binary, PostgresDecimalRf3Frames.Numeric(7, PostgresDecimalRf3Protocol.Positive, 0,
            7, 9228, 1625, 1426, 4337, 5935, 4395, 336), PostgresDecimalRf3Protocol.Unsupported),
        (PostgresDecimalRf3Protocol.Binary, PostgresDecimalRf3Frames.Numeric(0, PostgresDecimalRf3Protocol.Positive, 0, 1, 5000), PostgresDecimalRf3Protocol.Unsupported),
        (PostgresDecimalRf3Protocol.Text, Encoding.UTF8.GetBytes("1e"), PostgresPortalOwnershipProtocol.Validation),
        (PostgresDecimalRf3Protocol.Text, Encoding.UTF8.GetBytes("NaN"), PostgresDecimalRf3Protocol.Unsupported),
        (PostgresDecimalRf3Protocol.Text, Encoding.UTF8.GetBytes("0.00000000000000000000000000001"), PostgresDecimalRf3Protocol.Unsupported)
    ];

    private static async Task HealthyAsync(Stream stream, CancellationToken token)
    {
        await PostgresPortalOwnershipReplies.ControlAsync(stream, PostgresPortalOwnershipProtocol.Bind,
            PostgresDecimalRf3Frames.Bind(PostgresDecimalRf3Protocol.Text, Encoding.UTF8.GetBytes("1.50")),
            PostgresPortalOwnershipProtocol.BindComplete, token);
        await PostgresPortalOwnershipReplies.SendAsync(stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPortalOwnershipFrames.Execute(PostgresDecimalRf3Protocol.Portal), token);
        await PostgresDecimalRf3Pages.PageAsync(await PostgresPortalOwnershipReplies.PageAsync(stream, token), PostgresDecimalRf3Protocol.BetweenScores);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Portal,
            PostgresDecimalRf3Protocol.Portal, token);
        await PostgresRf3WireMessage.WriteAsync(stream, PostgresPortalOwnershipProtocol.Sync, [], token);
        await PostgresPortalOwnershipReplies.ExactAsync(stream, PostgresPortalOwnershipProtocol.Ready,
            [PostgresPortalOwnershipProtocol.Idle], token);
    }
}
