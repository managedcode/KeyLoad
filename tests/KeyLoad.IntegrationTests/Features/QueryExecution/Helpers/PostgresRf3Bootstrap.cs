using System.Security.Authentication;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Npgsql;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Bootstrap
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state, CancellationToken token)
    {
        var untrusted = PostgresRf3Certificate.Create(Path.Combine(fixture.Root, UntrustedDirectory));
        var trustRejected = await RejectTransportAsync(fixture, state, untrusted.RootCertificate, SslMode.VerifyFull, token);
        await global::TUnit.Assertions.Assert.That(trustRejected.InnerException is AuthenticationException).IsTrue();
        await RejectTransportAsync(fixture, state, PostgresRf3Certificate.RootPath(fixture), SslMode.Disable, token);
        var rejected = await global::TUnit.Assertions.Assert.ThrowsAsync<PostgresException>(async () =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var unexpected = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Writer.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }) ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        await global::TUnit.Assertions.Assert.That(rejected.SqlState).IsEqualTo(PostgresRf3Protocol.UnauthenticatedSqlState);
        await global::TUnit.Assertions.Assert.That(rejected.MessageText).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        await global::TUnit.Assertions.Assert.That(rejected.Message.Contains(state.Writer.Secret, StringComparison.Ordinal)).IsFalse();
        var failuresAfterRejection = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var healthy = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                state.Writer.Principal.Id, state.Writer.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlRf3Protocol.EqualAsync(state.Receipt, await healthy.CommitAsync(state.Seed, true, token));
            }, failuresAfterRejection);
        }, failuresAfterRejection);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failuresAfterRejection);
    }
    private static async Task<NpgsqlException> RejectTransportAsync(ClusterFixture fixture, PostgresRf3State state,
        string rootCertificate, SslMode sslMode, CancellationToken token)
    {
        var rejected = await global::TUnit.Assertions.Assert.ThrowsAsync<NpgsqlException>(async () =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var unexpected = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Writer.Principal.Id, state.Writer.Secret, rootCertificate, token, sslMode);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }) ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        await global::TUnit.Assertions.Assert.That(rejected is PostgresException).IsFalse();
        await global::TUnit.Assertions.Assert.That(rejected.Message.Contains(state.Writer.Secret, StringComparison.Ordinal)).IsFalse();
        return rejected;
    }

    private const string UntrustedDirectory = "native-sql-untrusted";
}
