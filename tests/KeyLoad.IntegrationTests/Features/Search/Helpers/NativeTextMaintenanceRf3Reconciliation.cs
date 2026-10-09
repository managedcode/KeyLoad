using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>Retains only deterministic original business/outbox values across native owner refusal.</summary>
internal sealed class NativeTextMaintenanceRf3Reconciliation(byte[] originalOutbox)
{
    private const long OriginalRevision = 1;

    internal static async Task<NativeTextMaintenanceRf3Reconciliation?> CaptureAsync(KeyLoadClient sdk,
        McpOfficialClient mcp, NativeTextMaintenanceRf3Scenario scenario, List<Exception> failures, CancellationToken token)
    {
        var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scenario.Partition, token));
        await Assert.That(status.Consumers).IsEmpty();
        var original = new NativeTextMaintenanceRf3Reconciliation(JsonDefaults.Serialize(status));
        var failureCount = failures.Count;
        await original.RequireAsync(sdk, mcp, scenario, failures, token);
        return failures.Count == failureCount ? original : null;
    }

    internal async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenanceRf3Scenario scenario, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scenario.Partition, token));
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(originalOutbox)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(scenario.Partition), token))).Value;
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(originalOutbox)).IsTrue();
        }, failures);
        foreach (var (id, json) in new[]
        {
            (NativeTextMaintenanceRf3Scenario.Ukrainian, NativeTextMaintenanceRf3Scenario.UkrainianJson),
            (NativeTextMaintenanceRf3Scenario.English, NativeTextMaintenanceRf3Scenario.EnglishJson)
        })
        {
            var expected = new DocumentResult(new(scenario.Partition, NativeTextMaintenanceRf3Scenario.Collection,
                id), OriginalRevision, json, false, []);
            await ServerFailureObserver.ObserveAsync(() => SdkDocumentAsync(sdk, expected, token), failures);
            await ServerFailureObserver.ObserveAsync(() => McpDocumentAsync(mcp, expected, token), failures);
        }
    }

    private static async Task SdkDocumentAsync(KeyLoadClient sdk, DocumentResult expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(expected.Reference, token));
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static async Task McpDocumentAsync(McpOfficialClient mcp, DocumentResult expected, CancellationToken token)
    {
        var actual = (await McpCallerAssertions.SuccessAsync<DocumentResult>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(expected.Reference), token))).Value;
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
