using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferNativeCallers : IAsyncDisposable
{
    private const string MissingCaller = "The native caller owner has not been initialized.";
    private RequestCqrsRf3Callers? original;

    private RemoteTransferNativeCallers() { }

    private RequestCqrsRf3Callers Original => original ?? throw new InvalidOperationException(MissingCaller);

    internal KeyLoadClient Sdk => Original.Sdk;

    internal static async Task<RemoteTransferNativeCallers> ConnectAsync(TwoRf3MembershipWave wave,
        string node, string credential, CancellationToken token)
    {
        var holder = new RemoteTransferNativeCallers();
        try
        {
            await holder.InitializeAsync(wave, node, credential, token).ConfigureAwait(false);
            return holder;
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            try
            { await holder.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { failures.Add(cleanup); }
            catch (Exception cleanup) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private async Task InitializeAsync(TwoRf3MembershipWave wave, string node, string credential,
        CancellationToken token)
        => original = await RequestCqrsRf3Callers.ConnectAsync(wave.Application, node, credential, token)
            .ConfigureAwait(false);

    internal async Task RequireAsync<TRequest, TResult>(PartitionRef partition, string tool, TRequest request,
        Func<CancellationToken, Task<Result<TResult>>> invoke, TResult expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await invoke(token)));
        await SqlRf3Protocol.EqualAsync(expected, await ToolAsync<TRequest, TResult>(tool, request, token));
        var sql = SqlRf3Protocol.Call(partition, tool, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<TResult>(Sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(expected,
            await ToolAsync<SqlOperationRequest, TResult>(SqlOperationProtocol.ToolName, sql, token));
    }

    internal async Task<TResult> ToolAsync<TRequest, TResult>(string tool, TRequest request,
        CancellationToken token)
        => (await McpCallerAssertions.SuccessAsync<TResult>(await Original.Mcp.CallAsync(tool, request, token))).Value;

    internal Task ReplayAsync(CommandRequest command, CommitReceipt expected, CancellationToken token)
        => RequireAsync(command.Partition, McpCallerTools.DocumentsCommit, command,
            ct => Sdk.CommitAsync(command, ct), expected, token);

    internal async Task DeniedCommandAsync(CommandRequest command, ErrorCode expected, CancellationToken token)
    {
        await McpCallerAssertions.ErrorAsync(await Original.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            command, token), expected, true);
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueProducerRf3Assertions.DeniedAsync(await Sdk.ExecuteSqlAsync(sql, token), expected);
        await McpCallerAssertions.ErrorAsync(await Original.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            sql, token), expected, true);
    }

    internal async Task DeniedReadAsync<TRequest, TResult>(PartitionRef partition, string tool,
        TRequest request, Func<CancellationToken, Task<Result<TResult>>> invoke, ErrorCode expected,
        CancellationToken token)
    {
        await QueueProducerRf3Assertions.DeniedAsync(await invoke(token), expected);
        await McpCallerAssertions.ErrorAsync(await Original.Mcp.CallAsync(tool, request, token), expected, true);
        var sql = SqlRf3Protocol.Call(partition, tool, request);
        await QueueProducerRf3Assertions.DeniedAsync(await Sdk.ExecuteSqlAsync(sql, token), expected);
        await McpCallerAssertions.ErrorAsync(await Original.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            sql, token), expected, true);
    }

    public ValueTask DisposeAsync() => original is { } callers ? callers.DisposeAsync() : ValueTask.CompletedTask;
}
