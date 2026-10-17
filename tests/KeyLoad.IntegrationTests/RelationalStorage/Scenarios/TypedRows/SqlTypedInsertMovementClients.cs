using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class SqlTypedInsertMovementClients
{
    internal static Task RunAsync(TwoRf3MembershipWave wave, SqlTypedInsertMovementTls tls,
        SqlTypedInsertMovementState state, string node,
        Func<RequestCqrsRf3Callers, PostgresRf3Caller, Task> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, state.Identity.Secret, async caller =>
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
    {
        await using var wire = await PostgresRf3Caller.ConnectAsync(wave.Application, node,
                state.Identity.Principal.Id, state.Identity.Secret, tls.RootCertificate, token);
        await ServerFailureObserver.ObserveAsync(() => operation(caller, wire), failures);
    }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return true;
    }, token);
    internal static async Task ArmAsync(PostgresRf3Caller wire, SqlOperationRequest request, CancellationToken token)
        => await wire.ExecuteAsync<bool>(SqlTypedInsertMovementProtocol.Arm(request.CommandId
            ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing), request.OwnershipEpoch
            ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing)), [], prepared: true, token);
}

