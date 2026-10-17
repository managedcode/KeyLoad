using System.Collections.Immutable;
using System.Text.Json;
using static KeyLoad.UnitTests.Features.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationRows
{
    internal static QueryPage Page(QueueViewLeaseIsolationState state, bool hidden)
    {
        var rows = ImmutableArray.CreateBuilder<QueryRow>();
        rows.Add(Row(First, state.Acked ? MessageState.Acked : MessageState.Leased, One,
            state.Version, CreatedValue, Orders, state.Acked, hidden));
        rows.Add(Row(Second, MessageState.Ready, Empty, Initial, PaidValue, Billing, false, hidden));
        if (state.Healthy)
        {
            rows.Add(Row(Healthy, MessageState.Ready, Empty, Initial, HealthyValue, Health, false, hidden));
        }
        return new(rows.ToImmutable(), null, state.Owner.Store.Position, QueuePath);
    }

    private static QueryRow Row(string id, MessageState status, int attempts, long version,
        string value, string source, bool absentBody, bool hidden)
    {
        var json = hidden ? HiddenJson : JsonSerializer.Serialize(new
        {
            id,
            state = status.ToString(),
            attempts,
            stateVersion = version,
            notBefore = MaskedNotBeforeJsonValue,
            expiresAt = (DateTimeOffset?)null,
            payload = absentBody ? null : new
            {
                value
            },
            headers = absentBody ? null : new
            {
                source
            }
        }, JsonDefaults.Options);
        return new(id, version, json, !hidden,
            hidden ? [] : [ModelPayloadPath, ModelHeaderPath]);
    }

    internal static async Task SameAsync(QueryPage actual, QueryPage expected)
        => await Assert.That(StringComparer.Ordinal.Equals(
            JsonSerializer.Serialize(actual, JsonDefaults.Options),
            JsonSerializer.Serialize(expected, JsonDefaults.Options))).IsTrue();
}
