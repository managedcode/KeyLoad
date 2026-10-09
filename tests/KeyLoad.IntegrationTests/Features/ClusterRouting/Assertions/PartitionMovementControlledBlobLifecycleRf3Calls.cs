using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobLifecycleRf3Calls
{
    internal static PartitionMovementControlledBlobCommandRf3Proof Capture<TRequest, TResult>(OperationKind kind,
        Guid commandId, string tool, TRequest request, TResult? value, ErrorCode? error, bool hasAuthority,
        Func<CancellationToken, Task<Result<TResult>>> invoke) where TRequest : notnull
        => new(kind, commandId, tool, request, value, error, hasAuthority,
            async (overrideError, token) => await RequireSdkAsync(await invoke(token).ConfigureAwait(false),
                value, overrideError ?? error).ConfigureAwait(false));

    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementControlledBlobCommandRf3Proof proof, ErrorCode? overrideError, CancellationToken token)
    {
        var error = overrideError ?? proof.Error;
        await proof.InvokeSdk(overrideError, token).ConfigureAwait(false);
        var direct = await seed.Official.CallAsync(proof.Tool, proof.Request, token).ConfigureAwait(false);
        var sql = SqlRf3Protocol.Call(seed.Partition, proof.Tool, proof.Request);
        if (error is { } denied)
        {
            await McpCallerAssertions.ErrorAsync(direct, denied, dispatched: true);
            await RequireSdkAsync(await seed.Source.ExecuteSqlAsync(sql, token).ConfigureAwait(false),
                default, denied).ConfigureAwait(false);
            await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(SqlOperationProtocol.ToolName,
                sql, token).ConfigureAwait(false), denied, dispatched: true);
            return;
        }
        await SqlRf3Protocol.EqualAsync(proof.Value, (await McpCallerAssertions.SuccessAsync<object>(direct)).Value);
        await SqlRf3Protocol.EqualAsync(proof.Value, await SqlRf3Protocol.SdkAsync<object>(seed.Source, sql, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(proof.Value, await SqlRf3Protocol.McpAsync<object>(seed.Official, sql, token).ConfigureAwait(false));
    }

    private static async Task RequireSdkAsync<T>(Result<T> actual, T? expected, ErrorCode? error)
    {
        if (error is { } denied)
        {
            await Assert.That(actual.IsFailed).IsTrue();
            if (actual.Value is JsonElement json)
            { await Assert.That(json.ValueKind).IsEqualTo(JsonValueKind.Undefined); }
            else
            { await Assert.That(actual.Value).IsNull(); }
            await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(denied.ToString());
            return;
        }
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(actual));
    }
}
