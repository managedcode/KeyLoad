namespace KeyLoad.FunctionalTests.Messaging;

internal static class SourceReadBusinessTime
{
    internal static DateTimeOffset Next(TestDatabase database)
    {
        var previous = database.Store.Read(view => view.ReadOwnedValue(StorageCommandKeys.Clock(database.Partition)) is { } bytes
            ? OrleansSerialization.Deserialize<DateTimeOffset>(bytes) : DateTimeOffset.MinValue);
        var current = TimeProvider.System.GetUtcNow();
        return current > previous ? current : previous.AddTicks(1);
    }
}
