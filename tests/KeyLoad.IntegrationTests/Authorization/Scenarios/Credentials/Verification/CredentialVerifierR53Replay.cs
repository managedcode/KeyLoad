using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialVerifierR53Replay
{
    internal static async Task RunAsync(KeyLoadClient root, McpOfficialClient administrator,
        CredentialSubjectReplacementState state, CredentialVerifierR53State verifier,
        Func<Task> afterResult, CancellationToken token)
    {
        await StatusAsync(root, administrator, state.First.Scenario.Partition, state.RebindCommandId,
            state.Rebound, state.RebindResult, afterResult, token);
        await StatusAsync(root, administrator, state.First.Scenario.Partition, state.RepairCommandId,
            state.First.Identity.Credential, state.RepairResult, afterResult, token);
        await StatusAsync(root, administrator, state.First.Scenario.Partition, verifier.ExpireId,
            verifier.Expired, verifier.ExpireResult, afterResult, token);
        await StatusAsync(root, administrator, state.First.Scenario.Partition, verifier.ReplaceId,
            verifier.Replacement, verifier.ReplaceResult, afterResult, token);
        var first = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.FirstSeedCommand, token));
        await SqlClusterProtocol.EqualAsync(state.FirstSeedReceipt, first);
        await afterResult();
        var second = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.SecondSeedCommand, token));
        await SqlClusterProtocol.EqualAsync(state.SecondSeedReceipt, second);
        await afterResult();
    }

    private static async Task StatusAsync(KeyLoadClient root, McpOfficialClient administrator, PartitionRef partition,
        Guid id, ApiKeyRecord credential, global::ManagedCode.Communication.Result? original,
        Func<Task> afterResult, CancellationToken token)
    {
        if (original is not { IsSuccess: true, Problem: null })
        {
            throw new InvalidOperationException(CredentialVerifierR53Protocol.MissingState);
        }
        var sdk = await McpCallerAssertions.SdkStatusAsync(await root.ConfigureApiKeyAsync(id, credential, token));
        await SqlClusterProtocol.EqualAsync(original, sdk);
        await afterResult();
        var request = new ConfigureApiKeyRequest(credential);
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, id);
        await McpCallerAssertions.StatusAsync(await administrator.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.CredentialsConfigure, arguments, token));
        await afterResult();
        var call = SqlClusterProtocol.Call(partition, McpCallerTools.CredentialsConfigure, request, id);
        var sql = await McpCallerAssertions.SdkSuccessAsync(await root.ExecuteSqlAsync(call, token));
        await Assert.That(sql.ValueKind).IsEqualTo(JsonValueKind.Null);
        await afterResult();
        await McpCallerAssertions.StatusAsync(await administrator.CallAsync(SqlOperationProtocol.ToolName, call, token));
        await afterResult();
    }
}
