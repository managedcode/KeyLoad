using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class WholeAtomicMoveInputBoundary
{
    private const string PartitionField = "partition";
    private const string RangeStart = "rangeStart";
    private const string RangeEnd = "rangeEnd";
    private const int FirstBoundary = 0;
    private const int SecondBoundary = 1;

    internal static async Task RunAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain, CancellationToken token)
    {
        foreach (var nested in new[] { false, true })
        {
            var arguments = Arguments(seed.FirstRequest, nested);
            var refused = await seed.Official.Client.InvokeKeyLoadToolAsync(
                PartitionMovePublicProtocol.ToolName, arguments, cancellationToken: token);
            await McpCallerAssertions.ErrorAsync(refused, ErrorCode.Validation, dispatched: false);
            var expected = JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.Validation,
                PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail), JsonDefaults.Options);
            var actual = refused.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
            await Assert.That(JsonElement.DeepEquals(expected, actual)).IsTrue();
            await MigratedAtomicDomainCurrent.RunAsync(seed, atomic, domain, token);
            await ReplayAsync(seed, atomic, domain, token);
        }
    }

    private static Dictionary<string, object?> Arguments(PartitionMoveRequest original, bool nested)
    {
        var request = Properties(JsonSerializer.SerializeToElement(original, JsonDefaults.Options));
        var partition = (JsonElement)request[PartitionField]!;
        var selected = nested ? Properties(partition) : request;
        selected.Add(RangeStart, FirstBoundary);
        selected.Add(RangeEnd, SecondBoundary);
        if (nested)
        {
            request[PartitionField] = selected;
        }
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpCallerProtocol.Request] = request
        };
    }

    private static Dictionary<string, object?> Properties(JsonElement value)
        => value.EnumerateObject().ToDictionary(property => property.Name,
            property => (object?)property.Value.Clone(), StringComparer.Ordinal);

    private static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.MovePartitionAsync(seed.FirstRequest, token));
        await SqlRf3Protocol.EqualAsync(domain.OriginalMove, sdk);
        await MigratedAtomicDomainCurrent.RunAsync(seed, atomic, domain, token);
        var official = (await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(
            await seed.Official.CallAsync(PartitionMovePublicProtocol.ToolName, seed.FirstRequest, token))).Value;
        await SqlRf3Protocol.EqualAsync(domain.OriginalMove, official);
        await MigratedAtomicDomainCurrent.RunAsync(seed, atomic, domain, token);
    }
}
