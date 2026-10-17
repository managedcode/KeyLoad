using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Calls
{
    internal static async Task<T> SdkAsync<T>(MetadataBootstrapRf3State state, string credential,
        Func<KeyLoadClient, Task<Result<T>>> call)
    {
        T value = default!;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(state.Fixture, state.ActiveNode);
            var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
            await ServerFailureObserver.ObserveAsync(async () => { value = await McpCallerAssertions.SdkSuccessAsync(await call(sdk)); }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return value;
    }
    internal static async Task BindAsync(MetadataBootstrapRf3State state, string credential, Guid id,
        BindAtomicPartitionPlacementRequest request, CancellationToken token)
    {
        await Assert.That(await SdkAsync(state, credential, client => client.BindAtomicPartitionPlacementAsync(id, request, token))).IsTrue();
        await McpAsync(state, credential, async mcp =>
        {
            var reply = await mcp.Client.InvokeKeyLoadToolAsync(AtomicPartitionPlacementPublicRf3Protocol.BindTool,
                Arguments(id, request), cancellationToken: token);
            await Assert.That((await McpCallerAssertions.SuccessAsync<bool>(reply)).Value).IsTrue();
            return true;
        }, token);
    }
    internal static async Task<T> McpAsync<T>(MetadataBootstrapRf3State state, string credential,
        Func<McpOfficialClient, Task<T>> call, CancellationToken token)
    {
        T value = default!;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(state.Fixture, state.ActiveNode, credential, token);
            await ServerFailureObserver.ObserveAsync(async () => { value = await call(mcp); }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return value;
    }
    internal static Dictionary<string, object?> Arguments(Guid id, BindAtomicPartitionPlacementRequest request)
        => new(StringComparer.Ordinal)
        {
            [McpCallerProtocol.CommandId] = id.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            [McpCallerProtocol.Request] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options)
        };
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
}
