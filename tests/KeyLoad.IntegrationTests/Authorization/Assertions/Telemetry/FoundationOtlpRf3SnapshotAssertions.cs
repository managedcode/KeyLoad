using System.Text.Json;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class FoundationOtlpRf3SnapshotAssertions
{
    internal static async Task<bool> HasExpectedAsync(JsonDocument? snapshot, bool logs, string node, string trace,
        ClusterFixture fixture, SensitiveAuthorityState state, FoundationOtlpRf3Receiver receiver,
        IOptions<FoundationOtlpRf3ExecutionOptions> options)
    {
        if (snapshot is null)
        {
            return false;
        }
        var root = snapshot.RootElement;
        var total = root.GetProperty(FoundationOtlpRf3Protocol.TotalCountProperty).GetInt32();
        var returned = root.GetProperty(FoundationOtlpRf3Protocol.ReturnedCountProperty).GetInt32();
        await Assert.That(total >= 0 && total <= options.Value.MaximumRecords && total == returned).IsTrue();
        var text = root.GetRawText();
        var secrets = new[] { fixture.AdminKey, fixture.Root, state.Identity.Secret, state.Identity.Principal.Id,
            state.Identity.Credential.Id, state.Scenario.Partition.TenantId, McpDocumentProtocol.PrivateValue,
            McpDocumentProtocol.ConflictValue, FoundationOtlpRf3Protocol.HeaderCanary }.Concat(receiver.Secrets);
        foreach (var secret in secrets)
        {
            await Assert.That(text.Contains(secret, StringComparison.Ordinal)).IsFalse();
        }
        var records = ReadRecords(root.GetProperty(FoundationOtlpRf3Protocol.DataProperty), logs, node).ToArray();
        await Assert.That(records.Length).IsEqualTo(returned);
        foreach (var record in records)
        {
            await Assert.That(record.GetProperty(FoundationOtlpRf3Protocol.TraceIdProperty).GetString() == trace).IsTrue();
        }
        if (logs)
        {
            return records.Any(record => record.GetProperty(FoundationOtlpRf3Protocol.SeverityNumberProperty).GetInt32() >= FoundationOtlpRf3Protocol.WarningSeverity
                && record.GetProperty(FoundationOtlpRf3Protocol.BodyProperty).GetProperty(FoundationOtlpRf3Protocol.StringValueProperty).GetString() == "Runtime event");
        }
        return records.Any(record => record.GetProperty(FoundationOtlpRf3Protocol.KindProperty).GetInt32() == FoundationOtlpRf3Protocol.ServerSpanKind
            && record.GetProperty(FoundationOtlpRf3Protocol.NameProperty).GetString() == "HTTP server request");
    }

    private static IEnumerable<JsonElement> ReadRecords(JsonElement data, bool logs, string node)
    {
        var resourceName = logs ? FoundationOtlpRf3Protocol.ResourceLogsProperty : FoundationOtlpRf3Protocol.ResourceSpansProperty;
        if (!data.TryGetProperty(resourceName, out var resources))
        {
            yield break;
        }
        foreach (var resource in resources.EnumerateArray())
        {
            var attributes = resource.GetProperty(FoundationOtlpRf3Protocol.ResourceProperty).GetProperty(FoundationOtlpRf3Protocol.AttributesProperty).EnumerateArray().ToArray();
            if (!attributes.Any(attribute => attribute.GetProperty(FoundationOtlpRf3Protocol.KeyProperty).GetString() == FoundationOtlpRf3Protocol.ServiceNameAttribute
                    && attribute.GetProperty(FoundationOtlpRf3Protocol.ValueProperty).GetProperty(FoundationOtlpRf3Protocol.StringValueProperty).GetString() == node)
                || !attributes.Any(attribute => attribute.GetProperty(FoundationOtlpRf3Protocol.KeyProperty).GetString() == FoundationOtlpRf3Protocol.ServiceInstanceAttribute
                    && !string.IsNullOrWhiteSpace(attribute.GetProperty(FoundationOtlpRf3Protocol.ValueProperty).GetProperty(FoundationOtlpRf3Protocol.StringValueProperty).GetString())))
            {
                throw new InvalidOperationException(FoundationOtlpRf3Protocol.Failure);
            }
            foreach (var record in ReadResourceRecords(resource, logs))
            {
                yield return record;
            }
        }
    }

    private static IEnumerable<JsonElement> ReadResourceRecords(JsonElement resource, bool logs)
    {
        foreach (var scope in resource.GetProperty(logs ? FoundationOtlpRf3Protocol.ScopeLogsProperty : FoundationOtlpRf3Protocol.ScopeSpansProperty).EnumerateArray())
        {
            foreach (var record in scope.GetProperty(logs ? FoundationOtlpRf3Protocol.LogRecordsProperty : FoundationOtlpRf3Protocol.SpansProperty).EnumerateArray())
            {
                yield return record;
            }
        }
    }
}
