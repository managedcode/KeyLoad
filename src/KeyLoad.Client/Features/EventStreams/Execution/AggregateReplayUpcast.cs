using System.Collections.Immutable;

namespace KeyLoad.Client;

internal static class AggregateReplayUpcast
{
    internal static Dictionary<int, EventUpcaster> BuildMap(
        IEnumerable<EventUpcaster>? upcasters,
        int maximumRegisteredUpcasters,
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
            ValidateRegistration(upcaster, result, maximumRegisteredUpcasters);
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
            throw new InvalidDataException(AggregateReplayMessages.FutureEventSchema);
        }
        var path = ImmutableArray.CreateBuilder<EventUpcaster>();
        var version = sourceVersion;
        while (version < targetVersion)
        {
            if (!upcasters.TryGetValue(version, out var next) || next.ToVersion != version + AggregateReplayProtocol.SchemaVersionStep)
            {
                throw new InvalidDataException(AggregateReplayMessages.MissingUpcastPath);
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
            var changed = upcaster.Transform(current) ?? throw new InvalidDataException(AggregateReplayMessages.NullUpcastResult);
            ValidateChange(current, changed, upcaster, limits);
            current = changed;
        }
        return record with { Data = current };
    }

    private static void ValidateRegistration(EventUpcaster? upcaster, Dictionary<int, EventUpcaster> entries,
        int maximumRegisteredUpcasters)
    {
        if (entries.Count >= maximumRegisteredUpcasters)
        {
            throw new InvalidDataException(AggregateReplayMessages.UpcasterLimitExceeded);
        }
        if (upcaster is null || upcaster.FromVersion <= AggregateReplayProtocol.InvalidSchemaVersion || upcaster.FromVersion == int.MaxValue ||
            upcaster.ToVersion != upcaster.FromVersion + AggregateReplayProtocol.SchemaVersionStep || upcaster.Transform is null ||
            !entries.TryAdd(upcaster.FromVersion, upcaster))
        {
            throw new InvalidDataException(AggregateReplayMessages.InvalidUpcastTransition);
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
            throw new InvalidDataException(AggregateReplayMessages.InvalidUpcastMutation);
        }
        AggregateReplayJson.ValidateJson(changed.PayloadJson, limits.MaximumInputBytes,
            limits.MaximumJsonDepth, AggregateReplayMessages.UpcastPayload);
    }
}
