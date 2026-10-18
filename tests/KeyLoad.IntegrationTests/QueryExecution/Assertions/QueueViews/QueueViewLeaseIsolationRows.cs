using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationRows
{
    internal static async Task AssertAsync(QueryPage page, QueueViewLeaseIsolationState state, bool hidden = false)
    {
        await Assert.That(page.CutPosition).IsGreaterThan((long)QueueViewLeaseIsolationProtocol.Empty);
        await Assert.That(page.AccessPath).IsEqualTo(QueueViewLeaseIsolationProtocol.QueuePath);
        await Assert.That(page.Cursor).IsNull();
        var expected = new List<QueryRow>
        {
            Row(SqlModelViewClusterScenario.FirstMessageId, state.Acked ? MessageState.Acked : MessageState.Leased,
                QueueViewLeaseIsolationProtocol.One, state.Version, QueueViewLeaseIsolationProtocol.Created,
                QueueViewLeaseIsolationProtocol.Orders, state.Acked, hidden),
            Row(SqlModelViewClusterScenario.SecondMessageId, MessageState.Ready, QueueViewLeaseIsolationProtocol.Empty,
                QueueViewLeaseIsolationProtocol.InitialVersion, QueueViewLeaseIsolationProtocol.Paid,
                QueueViewLeaseIsolationProtocol.Billing, false, hidden)
        };
        if (state.Healthy)
        {
            expected.Add(Row(QueueViewLeaseIsolationProtocol.Healthy, MessageState.Ready, QueueViewLeaseIsolationProtocol.Empty,
                QueueViewLeaseIsolationProtocol.InitialVersion, QueueViewLeaseIsolationProtocol.HealthyValue,
                QueueViewLeaseIsolationProtocol.Health, false, hidden));
        }
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Count);
        foreach (var (actual, golden) in page.Rows.Zip(expected))
        {
            await Assert.That(StringComparer.Ordinal.Equals(
                JsonSerializer.Serialize(actual, JsonDefaults.Options),
                JsonSerializer.Serialize(golden, JsonDefaults.Options))).IsTrue();
            using var observed = JsonDocument.Parse(actual.Json);
            using var value = JsonDocument.Parse(golden.Json);
            await Assert.That(JsonElement.DeepEquals(observed.RootElement, value.RootElement)).IsTrue();
            await Assert.That(actual.Json.Contains(state.Reader.Secret, StringComparison.Ordinal)
                || actual.Json.Contains(state.Delivery.Token, StringComparison.Ordinal)
                || actual.Json.Contains(SqlModelViewClusterScenario.QueueCanary, StringComparison.Ordinal)
                || actual.Json.Contains(SqlModelViewClusterScenario.QueueHeaderCanary, StringComparison.Ordinal)).IsFalse();
        }
    }

    private static QueryRow Row(string id, MessageState state, int attempts, long version, string value,
        string source, bool empty, bool hidden)
    {
        var json = hidden ? QueueViewLeaseIsolationProtocol.HiddenJson : JsonSerializer.Serialize(new
        {
            id,
            state = state.ToString(),
            attempts,
            stateVersion = version,
            notBefore = QueueViewLeaseIsolationProtocol.MaskedNotBeforeJsonValue,
            expiresAt = (DateTimeOffset?)null,
            payload = empty ? null : new
            {
                value
            },
            headers = empty ? null : new
            {
                source
            }
        }, JsonDefaults.Options);
        return new(id, version, json, !hidden,
            hidden ? [] : [QueueViewLeaseIsolationProtocol.PayloadPath, QueueViewLeaseIsolationProtocol.HeaderPath]);
    }
}
