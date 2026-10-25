using System.Text.Json;
using KeyLoad.ClusterTests.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.ClusterTests.Authorization;

internal static class FoundationOtlpClusterSnapshotAssertions
{
    internal static async Task<bool> HasExpectedAsync(JsonDocument? snapshot, bool logs, string node, string trace,
        ClusterFixture fixture, SensitiveAuthorityState state, FoundationOtlpClusterReceiver receiver,
        IOptions<FoundationOtlpClusterExecutionOptions> options)
    {
        if (snapshot is null)
        {
            return false;
        }
        var root = snapshot.RootElement;
        var total = root.GetProperty(FoundationOtlpClusterProtocol.TotalCountProperty).GetInt32();
        var returned = root.GetProperty(FoundationOtlpClusterProtocol.ReturnedCountProperty).GetInt32();
        await Assert.That(total >= 0 && total <= options.Value.MaximumRecords && total == returned).IsTrue();
        var text = root.GetRawText();
        var secrets = new[] { fixture.AdminKey, fixture.Root, state.Identity.Secret, state.Identity.Principal.Id,
            state.Identity.Credential.Id, state.Scenario.Partition.TenantId, McpDocumentProtocol.PrivateValue,
            McpDocumentProtocol.ConflictValue, FoundationOtlpClusterProtocol.HeaderCanary }.Concat(receiver.Secrets);
        foreach (var secret in secrets)
        {
            await Assert.That(text.Contains(secret, StringComparison.Ordinal)).IsFalse();
        }
        var records = ReadRecords(root.GetProperty(FoundationOtlpClusterProtocol.DataProperty), logs, node).ToArray();
        await Assert.That(records.Length).IsEqualTo(returned);
        foreach (var record in records)
        {
            await Assert.That(record.GetProperty(FoundationOtlpClusterProtocol.TraceIdProperty).GetString() == trace).IsTrue();
        }
        if (logs)
        {
            return records.Any(record => record.GetProperty(FoundationOtlpClusterProtocol.SeverityNumberProperty).GetInt32() >= FoundationOtlpClusterProtocol.WarningSeverity
                && record.GetProperty(FoundationOtlpClusterProtocol.BodyProperty).GetProperty(FoundationOtlpClusterProtocol.StringValueProperty).GetString() == "Runtime event");
        }
        return records.Any(record => record.GetProperty(FoundationOtlpClusterProtocol.KindProperty).GetInt32() == FoundationOtlpClusterProtocol.ServerSpanKind
            && record.GetProperty(FoundationOtlpClusterProtocol.NameProperty).GetString() == "HTTP server request");
    }

    private static IEnumerable<JsonElement> ReadRecords(JsonElement data, bool logs, string node)
    {
        var resourceName = logs ? FoundationOtlpClusterProtocol.ResourceLogsProperty : FoundationOtlpClusterProtocol.ResourceSpansProperty;
        if (!data.TryGetProperty(resourceName, out var resources))
        {
            yield break;
        }
        foreach (var resource in resources.EnumerateArray())
        {
            var attributes = resource.GetProperty(FoundationOtlpClusterProtocol.ResourceProperty).GetProperty(FoundationOtlpClusterProtocol.AttributesProperty).EnumerateArray().ToArray();
            if (!attributes.Any(attribute => attribute.GetProperty(FoundationOtlpClusterProtocol.KeyProperty).GetString() == FoundationOtlpClusterProtocol.ServiceNameAttribute
                    && attribute.GetProperty(FoundationOtlpClusterProtocol.ValueProperty).GetProperty(FoundationOtlpClusterProtocol.StringValueProperty).GetString() == node)
                || !attributes.Any(attribute => attribute.GetProperty(FoundationOtlpClusterProtocol.KeyProperty).GetString() == FoundationOtlpClusterProtocol.ServiceInstanceAttribute
                    && !string.IsNullOrWhiteSpace(attribute.GetProperty(FoundationOtlpClusterProtocol.ValueProperty).GetProperty(FoundationOtlpClusterProtocol.StringValueProperty).GetString())))
            {
                throw new InvalidOperationException(FoundationOtlpClusterProtocol.Failure);
            }
            foreach (var record in ReadResourceRecords(resource, logs))
            {
                yield return record;
            }
        }
    }

    private static IEnumerable<JsonElement> ReadResourceRecords(JsonElement resource, bool logs)
    {
        foreach (var scope in resource.GetProperty(logs ? FoundationOtlpClusterProtocol.ScopeLogsProperty : FoundationOtlpClusterProtocol.ScopeSpansProperty).EnumerateArray())
        {
            foreach (var record in scope.GetProperty(logs ? FoundationOtlpClusterProtocol.LogRecordsProperty : FoundationOtlpClusterProtocol.SpansProperty).EnumerateArray())
            {
                yield return record;
            }
        }
    }
}
