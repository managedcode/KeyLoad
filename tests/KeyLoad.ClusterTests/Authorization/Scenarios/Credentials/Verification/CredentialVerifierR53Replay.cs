using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class CredentialVerifierR53Replay
{
    private const string UnsupportedSql = "The unified SQL version, statement or target operation is unsupported.";

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
        var sql = await root.ExecuteSqlAsync(call, token);
        await Assert.That(sql.IsFailed).IsTrue();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.UnsupportedCapability, UnsupportedSql), sql.Problem);
        await afterResult();
        _ = (await McpCallerAssertions.ErrorAsync(await administrator.CallAsync(SqlOperationProtocol.ToolName, call, token),
            ErrorCode.UnsupportedCapability, dispatched: false)).Value;
        await afterResult();
    }
}
