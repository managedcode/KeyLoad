using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3ResumeConflict
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official, PartitionRef partition,
        Guid originalId, CancellationToken cancellationToken)
    {
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.SetDispatchAsync(originalId, paused: true,
            cancellationToken).ConfigureAwait(false), ErrorCode.Conflict);
        var arguments = McpOfficialClient.Arguments(true);
        arguments.Add(McpCallerProtocol.CommandId, originalId);
        await McpCallerAssertions.ErrorAsync(await official.Client.InvokeKeyLoadToolAsync(McpCallerTools.AdminDispatch,
            arguments, cancellationToken: cancellationToken).ConfigureAwait(false), ErrorCode.Conflict, dispatched: true);
        var changed = SqlRf3Protocol.Call(partition, McpCallerTools.AdminDispatch, true, originalId);
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await sdk.ExecuteSqlAsync(changed,
            cancellationToken).ConfigureAwait(false), ErrorCode.Conflict);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(SqlOperationProtocol.ToolName, changed,
            cancellationToken).ConfigureAwait(false), ErrorCode.Conflict, dispatched: true);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.SetDispatchAsync(originalId,
            paused: false, cancellationToken).ConfigureAwait(false))).IsTrue();
    }
}
