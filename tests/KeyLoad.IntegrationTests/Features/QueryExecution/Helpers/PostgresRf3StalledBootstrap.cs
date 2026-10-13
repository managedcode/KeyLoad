using System.Net.Security;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3StalledBootstrap
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state)
    {
        foreach (var phase in Enum.GetValues<Phase>())
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var deadline = McpCallerDeadline.Create();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                        fixture.AdminKey, async root =>
                    {
                        var before = await PostgresRf3Assertions.ImageAsync(root.Sdk, deadline.Token);
                        await RejectAsync(fixture, state, phase, deadline.Token).ConfigureAwait(false);
                        await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root.Sdk, deadline.Token)).IsEqualTo(before);
                        return true;
                    }, deadline.Token);
                    await PostgresRf3Parity.RunAsync(fixture, state, deadline.Token).ConfigureAwait(false);
                    await PostgresRf3Receipts.RunAsync(fixture, state, deadline.Token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task RejectAsync(ClusterFixture fixture, PostgresRf3State state, Phase phase, CancellationToken token)
    {
        var endpoint = fixture.App.GetEndpoint(McpCallerProtocol.Node3, PostgresRf3Protocol.Endpoint);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var socket = new TcpClient();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await socket.ConnectAsync(endpoint.Host, endpoint.Port, token).ConfigureAwait(false);
                await using var network = socket.GetStream();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    if (phase == Phase.SslHeader)
                    {
                        await network.WriteAsync(PartialHeader, token).ConfigureAwait(false);
                        await PostgresRf3TransportClosed.RequireAsync(network, token).ConfigureAwait(false);
                        return;
                    }
                    await network.WriteAsync(PostgresRf3WireFrames.SslRequest(), token).ConfigureAwait(false);
                    var accepted = new byte[PostgresRf3Protocol.OneColumn];
                    await network.ReadExactlyAsync(accepted, token).ConfigureAwait(false);
                    await global::TUnit.Assertions.Assert.That(accepted[PostgresRf3Protocol.FirstColumn]).IsEqualTo(PostgresRf3WireFrames.SslAccepted);
                    await using var tls = new SslStream(network, leaveInnerStreamOpen: true);
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                    {
                        await PostgresRf3WireTrust.AuthenticateAsync(tls, endpoint.Host, PostgresRf3Certificate.RootPath(fixture), token);
                        await WithholdAsync(tls, state, phase, token).ConfigureAwait(false);
                    }, failures).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithholdAsync(Stream tls, PostgresRf3State state, Phase phase, CancellationToken token)
    {
        if (phase == Phase.Startup)
        { await tls.WriteAsync(PostgresRf3WireFrames.StalledStartup(), token).ConfigureAwait(false); }
        else
        {
            await tls.WriteAsync(PostgresRf3WireFrames.Startup(state.Reader.Principal.Id), token).ConfigureAwait(false);
            await PostgresRf3WireFrames.PasswordChallengeAsync(tls, token).ConfigureAwait(false);
            await tls.WriteAsync(PostgresRf3WireFrames.StalledPassword(), token).ConfigureAwait(false);
        }
        await PostgresRf3TransportClosed.RequireAsync(tls, token).ConfigureAwait(false);
    }

    private enum Phase { SslHeader, Startup, Password }
    private static readonly byte[] PartialHeader = [default(byte)];
}
