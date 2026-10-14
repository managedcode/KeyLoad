using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Healthy
{
    internal static async Task<SqlCreateTableRf3State> RunAsync(ClusterFixture fixture, SqlCreateTableRf3State state,
        bool final, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            var id = Guid.NewGuid();
            var command = new CommandRequest(id, SqlCreateTableRf3Literal.Partition,
                [new PatchDocument(SqlCreateTableRf3Literal.Table, SqlCreateTableRf3Literal.One,
                    [new(SqlCreateTableRf3Literal.AmountPath, PatchKind.Set,
                        final ? SqlCreateTableRf3Literal.Nine : SqlCreateTableRf3Literal.Eight)], state.Revision)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
            var revision = state.Revision + NextRevision;
            await Assert.That(receipt.CommandId).IsEqualTo(id);
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(new MutationReceipt[]
                { new("patchDocument", SqlCreateTableRf3Literal.Table, SqlCreateTableRf3Literal.One, revision) }, receipt.Mutations.ToArray());
            var image = await SqlCreateTableRf3Images.ReadAsync(caller.Sdk, token);
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
            var sql = SqlRf3Protocol.Call(SqlCreateTableRf3Literal.Partition, SqlCreateTableRf3Literal.CommitOperation, command)
                with
            {
                CommandId = id
            };
            await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(caller.Mcp, sql, token));
            await SqlCreateTableRf3Images.RequireAsync(caller.Sdk, image, token);
            var next = state with
            {
                Seed = command,
                Receipt = receipt,
                Revision = revision,
                Selected = final ? SqlCreateTableRf3Literal.NineSelected : SqlCreateTableRf3Literal.EightSelected,
                SeedEpoch = state.Identity.Principal.PolicyEpoch
            };
            await SqlCreateTableRf3Pages.RunAsync(fixture, next, token);
            return next;
        }, token);
    private const long NextRevision = 1;
}
