using System.Text.Json;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlModelViewClusterAssertions
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
            await Assert.That(json.RootElement.GetProperty(SqlModelViewClusterJsonKeys.Payload)
                .TryGetProperty(SqlModelViewClusterJsonKeys.Secret, out _)).IsFalse();
            await Assert.That(json.RootElement.GetProperty(SqlModelViewClusterJsonKeys.Headers)
                .TryGetProperty(SqlModelViewClusterJsonKeys.PrivateHeader, out _)).IsFalse();
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
