using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlModelViewRf3JsonKeys
{
    internal const string Payload = "payload";
    internal const string Headers = "headers";
    internal const string Secret = "secret";
    internal const string PrivateHeader = "privateHeader";
    internal const string EventId = "eventId";
    internal const string EventType = "eventType";
    internal const string SchemaVersion = "schemaVersion";
    internal const string Revision = "revision";
    internal const string EventSequence = "eventSequence";
    internal const string RecordedAt = "recordedAt";
    internal const string Id = "id";
    internal const string State = "state";
    internal const string Attempts = "attempts";
    internal const string StateVersion = "stateVersion";
    internal const string NotBefore = "notBefore";
    internal const string ExpiresAt = "expiresAt";
}

internal static class SqlModelViewRf3Assertions
{
    internal static async Task AssertRedactedRowsAsync(QueryPage page, string payloadCanary, string headerCanary)
    {
        await Assert.That(page.Rows.Length).IsEqualTo(2);
        await Assert.That(page.Rows.All(row => row.Redacted)).IsTrue();
        foreach (var row in page.Rows)
        {
            await Assert.That(row.Json.Contains(payloadCanary, StringComparison.Ordinal)).IsFalse();
            await Assert.That(row.Json.Contains(headerCanary, StringComparison.Ordinal)).IsFalse();
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.GetProperty(SqlModelViewRf3JsonKeys.Payload)
                .TryGetProperty(SqlModelViewRf3JsonKeys.Secret, out _)).IsFalse();
            await Assert.That(json.RootElement.GetProperty(SqlModelViewRf3JsonKeys.Headers)
                .TryGetProperty(SqlModelViewRf3JsonKeys.PrivateHeader, out _)).IsFalse();
            await AssertNoDeliveryAuthorityAsync(json.RootElement);
        }
    }

    internal static async Task AssertNoDeliveryAuthorityAsync(JsonElement value)
    {
        foreach (var property in PropertyNames(value))
        {
            await Assert.That(IsDeliveryAuthorityName(property)).IsFalse();
        }
    }

    private static IEnumerable<string> PropertyNames(JsonElement value)
    {
        var pending = new Stack<JsonElement>();
        pending.Push(value);
        while (pending.TryPop(out var current))
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                {
                    yield return property.Name;
                    pending.Push(property.Value);
                }
                continue;
            }
            if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in current.EnumerateArray())
                {
                    pending.Push(item);
                }
            }
        }
    }

    private static bool IsDeliveryAuthorityName(string name)
        => name.Contains("lease", StringComparison.OrdinalIgnoreCase)
            || name.Contains("token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("owner", StringComparison.OrdinalIgnoreCase)
            || name.Contains("deliverygeneration", StringComparison.OrdinalIgnoreCase)
            || name.Contains("fingerprint", StringComparison.OrdinalIgnoreCase)
            || name.Contains("signing", StringComparison.OrdinalIgnoreCase);
}
