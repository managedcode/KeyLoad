using System.Collections.Immutable;

namespace KeyLoad.Client;

internal static class AggregateReplayUpcast
{
    private const int MaximumRegisteredUpcasters = 64;

    internal static Dictionary<int, EventUpcaster> BuildMap(
        IEnumerable<EventUpcaster>? upcasters,
        CancellationToken cancellationToken)
    {
        Dictionary<int, EventUpcaster> result = [];
        if (upcasters is null)
        {
            return result;
        }
        foreach (var upcaster in upcasters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRegistration(upcaster, result);
        }
        return result;
    }

    internal static ImmutableArray<EventUpcaster> ResolvePath(
        int sourceVersion,
        int targetVersion,
        IReadOnlyDictionary<int, EventUpcaster> upcasters)
    {
        if (sourceVersion > targetVersion)
        {
            throw new InvalidDataException("An event schema is newer than the reducer schema.");
        }
        var path = ImmutableArray.CreateBuilder<EventUpcaster>();
        var version = sourceVersion;
        while (version < targetVersion)
        {
            if (!upcasters.TryGetValue(version, out var next) || next.ToVersion != version + 1)
            {
                throw new InvalidDataException("No complete one-version upcast path reaches the reducer schema.");
            }
            path.Add(next);
            version = next.ToVersion;
        }
        return path.ToImmutable();
    }

    internal static EventRecord Apply(
        EventRecord record,
        ImmutableArray<EventUpcaster> path,
        AggregateReplayWorkerLimits limits,
        CancellationToken cancellationToken)
    {
        if (path.IsEmpty)
        {
            return record;
        }
        var current = record.Data;
        foreach (var upcaster in path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = upcaster.Transform(current) ?? throw new InvalidDataException("An event upcaster returned null.");
            ValidateChange(current, changed, upcaster, limits);
            current = changed;
        }
        return record with { Data = current };
    }

    private static void ValidateRegistration(EventUpcaster? upcaster, Dictionary<int, EventUpcaster> entries)
    {
        if (entries.Count >= MaximumRegisteredUpcasters)
        {
            throw new InvalidDataException("The registered upcaster count exceeds 64.");
        }
        if (upcaster is null || upcaster.FromVersion <= 0 || upcaster.FromVersion == int.MaxValue ||
            upcaster.ToVersion != upcaster.FromVersion + 1 || upcaster.Transform is null ||
            !entries.TryAdd(upcaster.FromVersion, upcaster))
        {
            throw new InvalidDataException("Upcasters must form unique positive one-version transitions.");
        }
    }

    private static void ValidateChange(
        EventData original,
        EventData changed,
        EventUpcaster upcaster,
        AggregateReplayWorkerLimits limits)
    {
        if (changed.SchemaVersion != upcaster.ToVersion ||
            !string.Equals(original.EventId, changed.EventId, StringComparison.Ordinal) ||
            !string.Equals(original.EventType, changed.EventType, StringComparison.Ordinal) ||
            !string.Equals(original.HeadersJson, changed.HeadersJson, StringComparison.Ordinal) ||
            original.OccurredAt != changed.OccurredAt ||
            !string.Equals(original.CorrelationId, changed.CorrelationId, StringComparison.Ordinal) ||
            !string.Equals(original.CausationId, changed.CausationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("An event upcaster may change payload JSON and schema version only.");
        }
        AggregateReplayJson.ValidateJson(changed.PayloadJson, limits.MaximumInputBytes,
            limits.MaximumJsonDepth, "Upcast payload");
    }
}
