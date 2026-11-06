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
        return page.Records
            .Select(record => Convert.ToHexString(record.Key.Span) + ":" + Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static DateTimeOffset Clock(ZoneTreeStore store, PartitionRef partition) => store.Read(view =>
    {
        var data = view.ReadOwnedValue(StorageCommandKeys.Clock(partition)) is { } dataBytes
            ? OrleansSerialization.Deserialize<DateTimeOffset>(dataBytes) : DateTimeOffset.MinValue;
        var metadata = view.ReadOwnedValue(StorageCommandKeys.Clock(null)) is { } metadataBytes
            ? OrleansSerialization.Deserialize<DateTimeOffset>(metadataBytes) : DateTimeOffset.MinValue;
        return data > metadata ? data : metadata;
    });

    internal static DatabaseEngine Open(ZoneTreeStore store, TimeProvider? timeProvider = null) => new(store, new AuthorizationPolicy(),
        UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(),
        UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
        UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), timeProvider);

    internal static Result<object> Apply<T>(DatabaseEngine database, OperationKind kind, T payload,
        Guid id, DateTimeOffset time) => database.Apply(database.CreateCommand(kind, id, "root", time, JsonSerializer.Serialize(payload, JsonDefaults.Options)));
}
