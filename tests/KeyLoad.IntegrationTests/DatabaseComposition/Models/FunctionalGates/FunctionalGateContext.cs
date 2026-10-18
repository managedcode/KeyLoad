using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Shares actual public callers and counts only completed successful database operations.</summary>
internal sealed record FunctionalGateContext(
    RequestCqrsClusterCallers Administrator,
    RequestCqrsClusterCallers Writer,
    RequestCqrsClusterCallers Reader,
    PartitionRef Partition,
    EntityRef Anchor,
    int ResourceCount)
{
    private const int MaximumProblemDetailCharacters = 2_048;
    private long sdkOperations;
    private long mcpOperations;

    internal RequestCqrsClusterCallers QueueAdministrator { get; init; } = null!;
    internal FunctionalGateReferenceBindings ReferenceBindings { get; init; } = new();
    internal EntityRef Bind(EntityRef reference) => ReferenceBindings.Bind(reference);
    internal string WriterPrincipalId { get; init; } = string.Empty;
    internal string ReaderPrincipalId { get; init; } = string.Empty;
    internal string MutableDocumentCollection { get; init; } = FunctionalGateProtocol.OpenDocumentCollection;
    internal int ReplicaCount
    {
        get; init;
    }
    internal Dictionary<string, long> AnalyticsGenerations { get; } = new(StringComparer.Ordinal);
    internal long SdkOperations => Interlocked.Read(ref sdkOperations);
    internal long McpOperations => Interlocked.Read(ref mcpOperations);
    internal long CompletedOperations => SdkOperations + McpOperations;

    internal async Task<T> SdkAsync<T>(Task<Result<T>> operation)
    {
        var observed = await operation;
        if (observed.IsFailed)
        {
            await Assert.That(observed.IsSuccess).IsTrue().Because(
                $"SDK failed with {observed.Problem?.ErrorCode}: {BoundedDetail(observed.Problem?.Detail)}");
        }
        var value = await McpCallerAssertions.SdkSuccessAsync(observed);
        Interlocked.Increment(ref sdkOperations);
        return value;
    }

    internal async Task SdkAsync(Task<Result> operation)
    {
        var observed = await operation;
        if (observed.IsFailed)
        {
            await Assert.That(observed.IsSuccess).IsTrue().Because(
                $"SDK failed with {observed.Problem?.ErrorCode}: {BoundedDetail(observed.Problem?.Detail)}");
        }
        await McpCallerAssertions.SdkStatusAsync(observed);
        Interlocked.Increment(ref sdkOperations);
    }

    internal async Task<T> McpAsync<T>(RequestCqrsClusterCallers caller, string tool, object request,
        CancellationToken cancellationToken)
    {
        var reply = await caller.Mcp.CallAsync(tool, request, cancellationToken);
        var result = await McpCallerAssertions.SuccessAsync<T>(reply);
        Interlocked.Increment(ref mcpOperations);
        return result.Value;
    }

    internal async Task<T> McpHeaderAsync<T>(RequestCqrsClusterCallers caller, string tool, object request,
        Guid commandId, CancellationToken cancellationToken)
    {
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, commandId);
        var reply = await caller.Mcp.Client.InvokeKeyLoadToolAsync(tool, arguments, cancellationToken);
        var result = await McpCallerAssertions.SuccessAsync<T>(reply);
        Interlocked.Increment(ref mcpOperations);
        return result.Value;
    }

    internal async Task<T> McpWithoutBodyAsync<T>(RequestCqrsClusterCallers caller, string tool,
        CancellationToken cancellationToken)
    {
        var reply = await caller.Mcp.CallWithoutBodyAsync(tool, cancellationToken);
        var result = await McpCallerAssertions.SuccessAsync<T>(reply);
        Interlocked.Increment(ref mcpOperations);
        return result.Value;
    }

    internal async Task McpStatusAsync(RequestCqrsClusterCallers caller, string tool, object request,
        CancellationToken cancellationToken)
    {
        await McpCallerAssertions.StatusAsync(await caller.Mcp.CallAsync(tool, request, cancellationToken));
        Interlocked.Increment(ref mcpOperations);
    }

    private static string BoundedDetail(string? detail)
        => detail is null ? string.Empty : detail[..Math.Min(detail.Length, MaximumProblemDetailCharacters)];
}
