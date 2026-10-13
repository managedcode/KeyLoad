using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresErrorReceiverPolicy
{
    internal static async Task SetAsync(global::KeyLoad.Client.KeyLoadClient root, PostgresRf3State state,
        bool denied, CancellationToken token)
    {
        var original = state.Reader.Principal;
        var expected = original with
        {
            PolicyEpoch = original.PolicyEpoch + (denied ? PostgresErrorReceiverProtocol.DenialAdvance
                : PostgresErrorReceiverProtocol.RepairAdvance),
            Grants = denied
                ? [new(PostgresRf3State.Partition.DatabaseId, PostgresRf3State.Left.Collection, Capability.DocumentsRead),
                    new(PostgresRf3State.Partition.DatabaseId, PostgresRf3State.Right.Collection, Capability.DocumentsRead)]
                : original.Grants
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
