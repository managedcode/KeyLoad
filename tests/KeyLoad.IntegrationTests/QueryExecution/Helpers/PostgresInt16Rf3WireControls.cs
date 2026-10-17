using System.Text;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresInt16Rf3WireControls
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
                    PostgresInt16Rf3Frames.Parse(), PostgresPortalOwnershipProtocol.ParseComplete, token);
                foreach (var fault in Faults())
                {
                    await PostgresPortalOwnershipReplies.RefusedAsync(wire.Stream, PostgresPortalOwnershipProtocol.Bind,
                        PostgresInt16Rf3Frames.Bind(fault.Format, fault.Bytes), fault.Error, token);
                    await HealthyAsync(wire.Stream, token);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static (short Format, byte[] Bytes, string Error)[] Faults() =>
    [
        (PostgresInt16Rf3Protocol.Binary, [], PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16Rf3Protocol.Binary, [0], PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16Rf3Protocol.Binary, [0, 0, 0], PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16Rf3Protocol.Text, Encoding.UTF8.GetBytes("32768"), PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16Rf3Protocol.Text, Encoding.UTF8.GetBytes("-32769"), PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16Rf3Protocol.Text, Encoding.UTF8.GetBytes("1.5"), PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16Rf3Protocol.Text, Encoding.UTF8.GetBytes("1e"), PostgresPortalOwnershipProtocol.Validation)
    ];

    private static async Task HealthyAsync(Stream stream, CancellationToken token)
    {
        await PostgresPortalOwnershipReplies.ControlAsync(stream, PostgresPortalOwnershipProtocol.Bind,
            PostgresInt16Rf3Frames.Bind(PostgresInt16Rf3Protocol.Text, Encoding.UTF8.GetBytes("2")),
            PostgresPortalOwnershipProtocol.BindComplete, token);
        await PostgresPortalOwnershipReplies.SendAsync(stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPortalOwnershipFrames.Execute(PostgresInt16Rf3Protocol.Portal), token);
        await PostgresInt16Rf3Pages.PageAsync(await PostgresPortalOwnershipReplies.PageAsync(stream, token), PostgresInt16Rf3Protocol.ProbeValue);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Portal,
            PostgresInt16Rf3Protocol.Portal, token);
        await PostgresRf3WireMessage.WriteAsync(stream, PostgresPortalOwnershipProtocol.Sync, [], token);
        await PostgresPortalOwnershipReplies.ExactAsync(stream, PostgresPortalOwnershipProtocol.Ready,
            [PostgresPortalOwnershipProtocol.Idle], token);
    }
}
