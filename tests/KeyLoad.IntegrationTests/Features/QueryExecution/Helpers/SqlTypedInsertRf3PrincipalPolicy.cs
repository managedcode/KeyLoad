using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3PrincipalPolicy
{
    internal static async Task<SqlTypedInsertRf3State> RunAsync(ClusterFixture fixture, SqlTypedInsertRf3State state,
        KeyLoadClient root, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            var failures = new List<Exception>();
            var repair = SqlTypedInsertRf3Literal.Request(Guid.NewGuid(), SqlTypedInsertRf3Literal.Repair);
            CommitReceipt? repairedReceipt = null;
            var original = state.Identity.Principal;
            var denied = original with
            {
                PolicyEpoch = checked(original.PolicyEpoch + NextEpoch),
                Grants = [new(SqlTypedInsertRf3Literal.Partition.DatabaseId, SqlTypedInsertRf3Literal.Table,
                    Capability.DocumentsRead | Capability.Query)]
            };
            var restored = original with { PolicyEpoch = checked(denied.PolicyEpoch + NextEpoch) };
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    original.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var before = await SqlTypedInsertRf3Images.ReadAsync(root, token);
                    await ConfigureAsync(root, denied, token);
                    await SqlTypedInsertRf3Wave.FenceAsync(caller, wire, state.Created, token);
                    await SqlTypedInsertRf3Wave.FenceAsync(caller, wire, state.Failed, token);
                    await SqlTypedInsertRf3Wave.FenceAsync(caller, wire, repair, token);
                    await SqlTypedInsertRf3Images.RequireAsync(root, before, token);
                    await ConfigureAsync(root, restored, token);
                    await SqlTypedInsertRf3Wave.FenceAsync(caller, wire, state.Created, token);
                    await SqlTypedInsertRf3Wave.FenceAsync(caller, wire, state.Failed, token);
                    await SqlTypedInsertRf3Wave.FenceAsync(caller, wire, repair, token);
                    await SqlTypedInsertRf3Images.RequireAsync(root, before, token);
                    repair = SqlTypedInsertRf3Literal.Request(Guid.NewGuid(), SqlTypedInsertRf3Literal.Repair);
                    repairedReceipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(caller.Sdk, repair, token);
                    await SqlTypedInsertRf3Hot.ReceiptAsync(repair, repairedReceipt, state.Owner,
                        new MutationReceipt(SqlTypedInsertRf3Literal.PutKind, SqlTypedInsertRf3Literal.Table, SqlTypedInsertRf3Literal.Three, SqlTypedInsertRf3Literal.InitialRevision));
                    await SqlTypedInsertRf3Wave.ReplayAsync(caller, wire, repair, repairedReceipt, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return state with
            {
                Identity = state.Identity with { Principal = restored },
                EpochFenced = true,
                Repaired = repair,
                RepairedReceipt = repairedReceipt ?? throw new InvalidOperationException(MissingRepair)
            };
        }, token);
    private static async Task ConfigureAsync(KeyLoadClient root, PrincipalRecord principal, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
    private const long NextEpoch = 1;
    private const string MissingRepair = "The original healthy INSERT receipt is missing.";
}
