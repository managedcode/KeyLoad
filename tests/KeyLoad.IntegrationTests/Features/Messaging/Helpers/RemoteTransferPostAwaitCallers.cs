using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;
using ManagedCode.Communication;
using ModelContextProtocol.Client;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferPostAwaitCallers : IAsyncDisposable
{
    private readonly PartitionMovementLateNativeHttp connection;
    private HttpClientTransport? transport;
    private McpClient? official;
    internal KeyLoadClient Sdk { get; }

    internal RemoteTransferPostAwaitCallers(Uri origin, string credential)
    {
        connection = PartitionMovementLateNativeHttp.Create(origin);
        Sdk = new(connection.Client, credential, IntegrationClientOptions.Execution());
    }

    internal async Task InitializeAsync(string credential, CancellationToken token)
    {
        transport = new(new HttpClientTransportOptions
        {
            Endpoint = new Uri(connection.Client.BaseAddress!, McpCallerProtocol.Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            { [McpCallerProtocol.AuthorizationHeader] = McpCallerProtocol.BearerPrefix + credential }
        }, connection.Client);
        official = await McpClient.CreateAsync(transport, new McpClientOptions
        { ProtocolVersion = McpCallerProtocol.ProtocolVersion }, cancellationToken: token);
    }

    internal async Task RequireAsync<TRequest, TResult>(PartitionRef partition, string tool, TRequest request,
        Func<CancellationToken, Task<Result<TResult>>> invoke, TResult expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await invoke(token)));
        await SqlRf3Protocol.EqualAsync(expected, await ToolAsync<TRequest, TResult>(tool, request, token));
        var sql = SqlRf3Protocol.Call(partition, tool, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<TResult>(Sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(expected, await ToolAsync<SqlOperationRequest, TResult>(SqlOperationProtocol.ToolName, sql, token));
    }

    internal async Task<TResult> ToolAsync<TRequest, TResult>(string tool, TRequest request, CancellationToken token)
    {
        var client = official ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        return (await McpCallerAssertions.SuccessAsync<TResult>(await client.InvokeKeyLoadToolAsync(
            tool, McpOfficialClient.Arguments(request), token))).Value;
    }

    internal Task ReplayAsync(CommandRequest command, CommitReceipt expected, CancellationToken token)
        => RequireAsync(command.Partition, McpCallerTools.DocumentsCommit, command,
            ct => Sdk.CommitAsync(command, ct), expected, token);

    internal async Task DeniedCommandAsync(CommandRequest command, ErrorCode expected, CancellationToken token)
    {
        var client = official ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        await McpCallerAssertions.ErrorAsync(await client.InvokeKeyLoadToolAsync(McpCallerTools.DocumentsCommit,
            McpOfficialClient.Arguments(command), token), expected, true);
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueProducerRf3Assertions.DeniedAsync(await Sdk.ExecuteSqlAsync(sql, token), expected);
        await McpCallerAssertions.ErrorAsync(await client.InvokeKeyLoadToolAsync(SqlOperationProtocol.ToolName,
            McpOfficialClient.Arguments(sql), token), expected, true);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (official is not null)
        { await ServerFailureObserver.ObserveAsync(() => official.DisposeAsync().AsTask(), failures); }
        if (transport is not null)
        {
            try
            { await transport.DisposeAsync(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        try
        { connection.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
