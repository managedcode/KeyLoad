using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SourceReadBusinessTime
{
    internal static DateTimeOffset Next(TestDatabase database)
    {
        var previous = database.Store.Read(view => view.ReadOwnedValue(KeySpace.Clock.ToArray()) is { } bytes
            ? NativeSerialization.Deserialize<DateTimeOffset>(bytes) : DateTimeOffset.MinValue);
        var current = TimeProvider.System.GetUtcNow();
        return current > previous ? current : previous.AddTicks(1);
    }
}
