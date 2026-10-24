using System.Text;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class PostgresInt16ClusterWireControls
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
                    PostgresInt16ClusterFrames.Parse(), PostgresPortalOwnershipProtocol.ParseComplete, token);
                foreach (var fault in Faults())
                {
                    await PostgresPortalOwnershipReplies.RefusedAsync(wire.Stream, PostgresPortalOwnershipProtocol.Bind,
                        PostgresInt16ClusterFrames.Bind(fault.Format, fault.Bytes), fault.Error, token);
                    await HealthyAsync(wire.Stream, token);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static (short Format, byte[] Bytes, string Error)[] Faults() =>
    [
        (PostgresInt16ClusterProtocol.Binary, [], PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16ClusterProtocol.Binary, [0], PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16ClusterProtocol.Binary, [0, 0, 0], PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16ClusterProtocol.Text, Encoding.UTF8.GetBytes("32768"), PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16ClusterProtocol.Text, Encoding.UTF8.GetBytes("-32769"), PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16ClusterProtocol.Text, Encoding.UTF8.GetBytes("1.5"), PostgresPortalOwnershipProtocol.Validation),
        (PostgresInt16ClusterProtocol.Text, Encoding.UTF8.GetBytes("1e"), PostgresPortalOwnershipProtocol.Validation)
    ];

    private static async Task HealthyAsync(Stream stream, CancellationToken token)
    {
        await PostgresPortalOwnershipReplies.ControlAsync(stream, PostgresPortalOwnershipProtocol.Bind,
            PostgresInt16ClusterFrames.Bind(PostgresInt16ClusterProtocol.Text, Encoding.UTF8.GetBytes("2")),
            PostgresPortalOwnershipProtocol.BindComplete, token);
        await PostgresPortalOwnershipReplies.SendAsync(stream, PostgresPortalOwnershipProtocol.Execute,
            PostgresPortalOwnershipFrames.Execute(PostgresInt16ClusterProtocol.Portal), token);
        await PostgresInt16ClusterPages.PageAsync(await PostgresPortalOwnershipReplies.PageAsync(stream, token), PostgresInt16ClusterProtocol.ProbeValue);
        await PostgresPortalOwnershipCommands.CloseAsync(stream, PostgresPortalOwnershipProtocol.Portal,
            PostgresInt16ClusterProtocol.Portal, token);
        await PostgresClusterWireMessage.WriteAsync(stream, PostgresPortalOwnershipProtocol.Sync, [], token);
        await PostgresPortalOwnershipReplies.ExactAsync(stream, PostgresPortalOwnershipProtocol.Ready,
            [PostgresPortalOwnershipProtocol.Idle], token);
    }
}
