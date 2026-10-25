using System.Text.Json;
using KeyLoad.Core.Authorization;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueWholeFlowStorage
{
    internal static string[] Bytes(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        {
            throw new InvalidOperationException("Whole store state exceeded the fixture bound.");
        }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static DateTimeOffset Clock(ZoneTreeStore store) => store.Read(view =>
        OrleansSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.ClockBytes)!));

    internal static DatabaseEngine Open(ZoneTreeStore store, TimeProvider? timeProvider = null) => new(store, new AuthorizationPolicy(),
        UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(),
        UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
        UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), timeProvider);

    internal static Result<object> Apply<T>(DatabaseEngine database, OperationKind kind, T payload,
        Guid id, DateTimeOffset time) => database.Apply(database.CreateCommand(kind, id, "root", time, JsonSerializer.Serialize(payload, JsonDefaults.Options)));
}
