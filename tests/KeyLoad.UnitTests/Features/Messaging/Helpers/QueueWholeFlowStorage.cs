using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueWholeFlowStorage
{
    internal static string[] Bytes(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        { throw new InvalidOperationException("Whole store state exceeded the fixture bound."); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static DateTimeOffset Clock(ZoneTreeStore store) => store.Read(view =>
        NativeSerialization.Deserialize<DateTimeOffset>(view.ReadOwnedValue(KeySpace.ClockBytes)!));

    internal static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(),
        UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
        UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());

    internal static OperationResult Apply<T>(DatabaseEngine database, OperationKind kind, T payload,
        Guid id, DateTimeOffset time) => database.Apply(new(id, kind, "root", time,
            JsonSerializer.Serialize(payload, JsonDefaults.Options)));
}
