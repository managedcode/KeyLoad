using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class AnalyticsCommandIdentityData
{
    internal const string Root = "root";
    internal const string Store = "identity-analytics";
    internal const string Collection = "identity-documents";
    internal const string Document = "mixed";
    internal const string Json = "{\"z\":1,\"a\":2}";
    internal const string First = "a";
    internal const string Last = "z";
    internal static readonly DateTimeOffset From = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static AnalyticsEventData Event(string id, bool reverse, bool changed = false)
    {
        var dimensions = Map(reverse, (First, "alpha"), (Last, "omega"));
        var metrics = Map(reverse, (First, changed ? 9d : 2d), (Last, 4d));
        return new(id, From.AddMinutes(1), dimensions, metrics, null, []);
    }

    internal static ImmutableDictionary<string, T> Map<T>(bool reverse, params (string Key, T Value)[] values)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
        foreach (var value in reverse ? values.Reverse() : values)
        {
            builder.Add(value.Key, value.Value);
        }
        return builder.ToImmutable();
    }

    internal static Mutation Analytics(bool delete, bool reverse, bool changed = false)
        => delete
            ? new DeleteAnalyticsEvents(Store, Map(reverse, (First, changed ? 2L : 1L), (Last, 1L)))
            : new UpsertAnalyticsEvents(Store, [Event(First, reverse, changed), Event(Last, reverse)],
                Map(reverse, (First, 0L), (Last, 0L)));

    internal static CommandRequest Request(TestDatabase db, Guid id, bool delete, bool reverse, bool changed = false)
        => new(id, db.Partition, [new PutDocument(Collection, Document, Json, 0), Analytics(delete, reverse, changed)]);
}
