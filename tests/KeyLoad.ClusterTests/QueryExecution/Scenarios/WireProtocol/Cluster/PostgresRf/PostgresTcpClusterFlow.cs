using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.ClusterTests.ClientApi;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class PostgresTcpClusterFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new PostgresClusterSelection(
                PostgresClusterProtocol.ScalarDialect, RequireTls: false));
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var state = await SqlNullOrderingClusterSetup.CreateAsync(fixture, deadline.Token);
                await TlsRefusedAsync(fixture, deadline.Token);
                await RefuseCredentialAsync(fixture, state, deadline.Token);
                foreach (var mode in new[] { SslMode.Disable, SslMode.Prefer })
                {
                    await HealthyAsync(fixture, state, mode, deadline.Token);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefuseCredentialAsync(ClusterFixture fixture,
        SqlNullOrderingClusterState state, CancellationToken token)
    {
        var error = await global::TUnit.Assertions.Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var caller = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                state.Reader.Principal.Id, fixture.AdminKey, token, SslMode.Disable);
        });
        await global::TUnit.Assertions.Assert.That(error?.SqlState).IsEqualTo(PostgresClusterProtocol.UnauthenticatedSqlState);
    }

    private static async Task HealthyAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        SslMode mode, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var caller = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                state.Reader.Principal.Id, state.Reader.Secret, token, mode);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                foreach (var prepared in new[] { false, true })
                {
                    var value = PostgresInt16ClusterProtocol.OneValue;
                    var parameter = new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Smallint, Value = value };
                    await PostgresInt16ClusterPages.PageAsync(await caller.ExecuteAsync<QueryPage>(
                        PostgresInt16ClusterProtocol.DriverSql, [parameter], prepared, token), value);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task TlsRefusedAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var socket = new TcpClient();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var endpoint = fixture.App.GetEndpoint(McpCallerProtocol.Node3, PostgresClusterProtocol.Endpoint);
                await socket.ConnectAsync(endpoint.Host, endpoint.Port, token);
                await using var stream = socket.GetStream();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await stream.WriteAsync(PostgresClusterWireFrames.SslRequest(), token);
                    var reply = new byte[PostgresClusterProtocol.OneColumn];
                    await stream.ReadExactlyAsync(reply, token);
                    await global::TUnit.Assertions.Assert.That(reply[PostgresClusterProtocol.FirstColumn])
                        .IsEqualTo(global::KeyLoad.Server.PostgresProtocol.SslRejected);
                }, failures);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
